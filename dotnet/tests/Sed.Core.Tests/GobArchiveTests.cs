using System.Text;
using Sed.Formats.Gob;
using Xunit;

namespace Sed.Core.Tests;

public class GobArchiveTests
{
    /// <summary>Builds a minimal valid GOB in memory with the given (name, content) files.</summary>
    private static byte[] BuildGob(params (string name, string content)[] files)
    {
        const int nameLen = 128;
        const int entrySize = 4 + 4 + nameLen;
        int dirOffset = 12;
        int dataStart = dirOffset + 4 + files.Length * entrySize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII);

        bw.Write(Encoding.ASCII.GetBytes("GOB "));
        bw.Write((uint)0x14);
        bw.Write((uint)dirOffset);

        bw.Write((uint)files.Length);
        int cursor = dataStart;
        foreach (var (name, content) in files)
        {
            bw.Write((uint)cursor);
            bw.Write((uint)content.Length);
            var nameBytes = new byte[nameLen];
            Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
            bw.Write(nameBytes);
            cursor += content.Length;
        }
        foreach (var (_, content) in files)
            bw.Write(Encoding.ASCII.GetBytes(content));

        return ms.ToArray();
    }

    [Fact]
    public void ReadsDirectoryAndContent()
    {
        var bytes = BuildGob(
            ("cog\\test.cog", "symbols"),
            ("jkl\\level.jkl", "SECTION: HEADER"));

        using var gob = new GobArchive(new MemoryStream(bytes), ownsStream: true);

        Assert.Equal(2, gob.Entries.Count);
        var jkl = Assert.Single(gob.FindByExtension(".jkl").ToList());
        Assert.Equal("jkl\\level.jkl", jkl.Name);
        Assert.Equal("SECTION: HEADER", gob.ReadText(jkl));
    }

    [Fact]
    public void FindMatchesByFileName()
    {
        var bytes = BuildGob(("jkl\\01narshadda.jkl", "x"));
        using var gob = new GobArchive(new MemoryStream(bytes), ownsStream: true);

        Assert.NotNull(gob.Find("01narshadda.jkl"));
        Assert.NotNull(gob.Find("jkl\\01narshadda.jkl"));
    }

    [Fact]
    public void RejectsNonGob()
    {
        var bytes = Encoding.ASCII.GetBytes("NOPE not a gob file at all");
        Assert.Throws<InvalidDataException>(() =>
            new GobArchive(new MemoryStream(bytes), ownsStream: true));
    }
}
