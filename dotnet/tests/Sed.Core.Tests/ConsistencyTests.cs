using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Core.Validation;
using Xunit;

namespace Sed.Core.Tests;

public class ConsistencyTests
{
    private static Level MakeBoxLevel()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        level.Sectors.Add(sector);
        level.RenumberSectors();
        return level;
    }

    [Fact]
    public void ValidLevel_HasNoIssues()
    {
        var level = MakeBoxLevel();

        var issues = ConsistencyChecker.Check(level);

        Assert.Empty(issues);
    }

    [Fact]
    public void SectorWithFewSurfaces_Flagged()
    {
        var level = MakeBoxLevel();
        var sector = level.Sectors[0];
        while (sector.Surfaces.Count > 2)
            sector.Surfaces.RemoveAt(sector.Surfaces.Count - 1);

        var issues = ConsistencyChecker.Check(level);

        Assert.Contains(issues, i =>
            i.Type == ItemType.Sector &&
            i.Severity == IssueSeverity.Error &&
            i.Message.Contains("fewer than 4 surfaces"));
    }

    [Fact]
    public void InvalidAdjoin_Flagged()
    {
        var level = MakeBoxLevel();
        var sector = level.Sectors[0];
        var a = sector.Surfaces[0];
        var b = sector.Surfaces[1];

        // Correct mirror — no error expected.
        a.Adjoin = b;
        b.Adjoin = a;
        Assert.DoesNotContain(ConsistencyChecker.Check(level),
            i => i.Message.Contains("reverse adjoin"));

        // Break mirror — error expected.
        b.Adjoin = null;
        Assert.Contains(ConsistencyChecker.Check(level),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("reverse adjoin"));
    }

    [Fact]
    public void NonPlanarSurface_Flagged()
    {
        var level = MakeBoxLevel();
        var sector = level.Sectors[0];
        var surf = sector.Surfaces[0];
        // Warp the last corner out of plane.
        surf.Corners[3].Vertex.Position += new Vec3(0, 0, 5);

        var issues = ConsistencyChecker.Check(level);

        Assert.Contains(issues, i => i.Message.Contains("not planar"));
    }

    [Fact]
    public void ThingWithoutSector_Flagged()
    {
        var level = MakeBoxLevel();
        level.Things.Add(new Thing { Sector = null });

        var issues = ConsistencyChecker.Check(level);

        Assert.Contains(issues, i =>
            i.Type == ItemType.Thing &&
            i.Severity == IssueSeverity.Warning &&
            i.Message.Contains("not in a sector"));
    }
}
