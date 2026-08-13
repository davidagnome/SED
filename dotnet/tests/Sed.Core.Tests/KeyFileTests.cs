using Sed.Formats.Keyframe;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>KEY cutscene animation files (PJKEY_IO.INC / U_PJKEY.PAS).</summary>
public class KeyFileTests
{
    private const string Key = """
        SECTION: HEADER
        FLAGS 0
        TYPE 0
        FRAMES 30
        FPS 15
        JOINTS 0

        SECTION: KEYFRAME
        NODES 1
        NODE 0
        MESH NAME table
        ENTRIES 2
        rest 0 0x1 0.0 0.0 0.0 0.0 0.0 0.0
        0.0 0.0 0.0 0.0 0.0 0.0
        rest 30 0x1 0.0 0.0 0.0 0.0 0.0 0.0
        0.0 0.0 0.0 0.0 0.0 0.0
        END
        """;

    [Fact]
    public void ParsesHeaderAndNodes()
    {
        var key = KeyFile.Parse(Key);

        Assert.Equal(30, key.FrameCount);
        Assert.Equal(15, key.Fps);
        Assert.Equal(0, key.Flags);

        var node = Assert.Single(key.Nodes);
        Assert.Equal("TABLE", node.MeshName); // the reader upper-cases lines
        Assert.Equal(2, node.Entries.Count);
        Assert.Equal(0, node.Entries[0].Frame);
        Assert.Equal(30, node.Entries[1].Frame);
    }

    [Fact]
    public void InterpolatesFromTheLastEntryAtOrBeforeTheFrame()
    {
        var key = KeyFile.Parse(Key);
        var node = key.Nodes[0];

        node.Entries[0].CX = 10;
        node.Entries[0].DX = 2;
        node.Entries[1].CX = 70;
        node.Entries[1].DX = 1;

        // Frame 5 is between the entries: uses entry 0 → 10 + 2·5 = 20.
        Assert.True(node.GetFrame(5, out var x, out _, out _, out _, out _, out _));
        Assert.Equal(20, x);

        // Frame 0: the entry itself.
        node.GetFrame(0, out x, out _, out _, out _, out _, out _);
        Assert.Equal(10, x);

        // Frame 40 past the last entry: extrapolates 70 + 1·(40−30) = 80.
        node.GetFrame(40, out x, out _, out _, out _, out _, out _);
        Assert.Equal(80, x);
    }

    [Fact]
    public void PoseAndDeltaLinesAreBothParsed()
    {
        const string twoLine = """
            SECTION: HEADER
            FRAMES 10
            FPS 15

            SECTION: KEYFRAME
            NODES 1
            NODE 0
            MESH NAME box
            ENTRIES 1
            rest 0 0x3 1.0 2.0 3.0 4.0 5.0 6.0
            0.1 0.2 0.3 0.4 0.5 0.6
            END
            """;

        var key = KeyFile.Parse(twoLine);
        var entry = Assert.Single(Assert.Single(key.Nodes).Entries);

        Assert.Equal(0x3, entry.Flags);
        Assert.Equal(1.0, entry.CX);
        Assert.Equal(2.0, entry.CY);
        Assert.Equal(3.0, entry.CZ);
        Assert.Equal(0.1, entry.DX);
        Assert.Equal(0.2, entry.DY);
        Assert.Equal(0.3, entry.DZ);
        Assert.Equal(0.6, entry.DRol);
    }

    [Fact]
    public void EmptyEntriesReportNoFrame()
    {
        var key = KeyFile.Parse(Key);
        key.Nodes[0].Entries.Clear();
        Assert.False(key.Nodes[0].GetFrame(5, out _, out _, out _, out _, out _, out _));
    }
}
