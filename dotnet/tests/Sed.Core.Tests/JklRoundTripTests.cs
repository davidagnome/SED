using System.Linq;
using Sed.Core.Math;
using Sed.Formats.Jkl;
using Xunit;

namespace Sed.Core.Tests;

public class JklRoundTripTests
{
    private const string Jkl = """
        SECTION: HEADER
        VERSION 1
        END

        SECTION: GEORESOURCE
        WORLD COLORMAPS 1
        0:	dflt.cmp
        WORLD VERTICES 4
        0:	-1.0 -1.0 0.0
        1:	1.0 -1.0 0.0
        2:	1.0 1.0 0.0
        3:	-1.0 1.0 0.0
        WORLD TEXTURE VERTICES 1
        0:	0.0 0.0
        WORLD ADJOINS 0
        WORLD SURFACES 1
        0:	0 0 0 3 0 0 -1 0.5 4 0,0 1,0 2,0 3,0 0.5 0.5 0.5 0.5
        0:	0.0 0.0 1.0
        END

        SECTION: SECTORS
        World sectors 1
        SECTOR 0
        SURFACES 0 1
        END

        SECTION: COGS
        World cogs 1
        0: special.cog 0 1 2
        END

        SECTION: THINGS
        WORLD THINGS 1
        0:	walkplayer	Player	0.0 0.0 0.5 0.0 0.0 0.0 0
        END
        """;

    [Fact]
    public void EditsPersist_AndOtherSectionsPreserved()
    {
        var doc = JklParser.ParseDocument(Jkl);

        // Move the vertex that came from global index 0, and move the thing.
        var v0 = doc.Level.Sectors[0].Vertices.First(v => v.SourceIndex == 0);
        v0.Position += new Vec3(0, 0, 5);
        doc.Level.Things[0].Position = new Vec3(3, 4, 9);

        var output = JklWriter.Build(doc);

        // COGS section preserved verbatim.
        Assert.Contains("special.cog", output);

        // Re-parse the saved text and confirm the edits round-tripped.
        var reloaded = JklParser.Parse(output);
        var rv = reloaded.Sectors[0].Vertices.First(v => v.SourceIndex == 0);
        Assert.Equal(5.0, rv.Position.Z, 5);
        Assert.Equal(new Vec3(3, 4, 9), reloaded.Things[0].Position);

        // Unmoved vertex unchanged.
        var rv1 = reloaded.Sectors[0].Vertices.First(v => v.SourceIndex == 1);
        Assert.Equal(new Vec3(1, -1, 0), rv1.Position);
    }

    [Fact]
    public void UneditedSave_IsContentEquivalent()
    {
        var doc = JklParser.ParseDocument(Jkl);
        var output = JklWriter.Build(doc);
        var reloaded = JklParser.Parse(output);
        Assert.Equal(doc.Level.Things[0].Position, reloaded.Things[0].Position);
        Assert.Equal(doc.Level.Sectors[0].Vertices.Count, reloaded.Sectors[0].Vertices.Count);
    }
}
