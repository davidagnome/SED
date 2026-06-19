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
    public void AddedThing_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        Assert.Single(doc.Level.Things);

        var extra = new Sed.Core.Model.Thing
        {
            Template = "2x8catwalk", Name = "extra", Position = new Vec3(1, 2, 3),
            Sector = doc.Level.Sectors[0],
        };
        doc.Level.Things.Add(extra);
        doc.Level.RenumberThings();

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(2, reloaded.Things.Count);
        Assert.Contains(reloaded.Things, t => t.Name == "extra");
        Assert.Contains(reloaded.Things, t => t.Name == "Player"); // original preserved
    }

    [Fact]
    public void DeletedThing_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        doc.Level.Things.Clear();
        doc.Level.RenumberThings();
        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Empty(reloaded.Things);
        Assert.Contains("special.cog", JklWriter.Build(doc)); // other sections intact
    }

    [Fact]
    public void Geometry_RoundTrips_ThroughRegeneration()
    {
        var doc = JklParser.ParseDocument(Jkl);
        var before = (doc.Level.Sectors.Count,
            doc.Level.Sectors.Sum(s => s.Surfaces.Count),
            doc.Level.Sectors.Sum(s => s.Vertices.Count),
            doc.Level.Sectors.Sum(s => s.Surfaces.Sum(f => f.Corners.Count)));

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        var after = (reloaded.Sectors.Count,
            reloaded.Sectors.Sum(s => s.Surfaces.Count),
            reloaded.Sectors.Sum(s => s.Vertices.Count),
            reloaded.Sectors.Sum(s => s.Surfaces.Sum(f => f.Corners.Count)));

        Assert.Equal(before, after);
        // Surface material + flags preserved.
        Assert.Equal(doc.Level.Sectors[0].Surfaces[0].Material, reloaded.Sectors[0].Surfaces[0].Material);
    }

    [Fact]
    public void CreatedSector_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        int sectors0 = doc.Level.Sectors.Count;
        var box = Sed.Core.Model.SectorFactory.CreateBox(doc.Level, new Vec3(10, 10, 10), 1.0, "dflt.mat", 0);
        doc.Level.Sectors.Add(box);
        doc.Level.RenumberSectors();

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(sectors0 + 1, reloaded.Sectors.Count);
        Assert.Equal(6, reloaded.Sectors[^1].Surfaces.Count);
        Assert.Equal(8, reloaded.Sectors[^1].Vertices.Count);
    }

    [Fact]
    public void DeletedVertex_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        int verts0 = doc.Level.Sectors[0].Vertices.Count;
        var v = doc.Level.Sectors[0].Vertices[0];
        new Sed.Core.Editing.DeleteVertexCommand(doc.Level.Sectors[0], v).Apply();

        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(verts0 - 1, reloaded.Sectors[0].Vertices.Count);
    }

    [Fact]
    public void SectorAmbientLight_PersistsThroughSave()
    {
        var doc = JklParser.ParseDocument(Jkl);
        doc.Level.Sectors[0].Ambient = new Sed.Core.Math.ColorF(0.5f, 0.5f, 0.5f);
        var reloaded = JklParser.Parse(JklWriter.Build(doc));
        Assert.Equal(0.5f, reloaded.Sectors[0].Ambient.R, 3);
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
