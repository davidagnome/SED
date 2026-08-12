using Sed.Core;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class CleaveSectorTests
{
    private static (Level level, Sector box) MakeBox(Vec3 centre, double half = 1.0)
    {
        var level = new Level();
        var box = SectorFactory.CreateBox(level, centre, half, "dflt.mat", 0);
        level.Sectors.Add(box);
        level.RenumberSectors();
        return (level, box);
    }

    [Fact]
    public void CleavingABoxDownTheMiddleMakesTwoAdjoinedSectors()
    {
        var (level, box) = MakeBox(Vec3.Zero);

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), Vec3.Zero);
        var history = new EditHistory();
        history.Do(command);

        Assert.True(command.Succeeded);
        Assert.Equal(2, level.Sectors.Count);

        var other = command.NewSector!;
        Assert.NotSame(box, other);

        // Each half is a closed box again: 4 side walls + 2 caps.
        Assert.True(box.Surfaces.Count >= 5, $"front half has {box.Surfaces.Count} surfaces");
        Assert.True(other.Surfaces.Count >= 5, $"back half has {other.Surfaces.Count} surfaces");

        // The cut is a portal: exactly one adjoined pair, linking the two halves.
        var portals = box.Surfaces.Where(s => s.Adjoin is not null).ToList();
        var portal = Assert.Single(portals);
        Assert.Same(other, portal.Adjoin!.Sector);
        Assert.Same(portal, portal.Adjoin.Adjoin);
    }

    [Fact]
    public void EachHalfKeepsItsOwnSideOfThePlane()
    {
        var (level, box) = MakeBox(Vec3.Zero);

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), Vec3.Zero);
        command.Apply();
        Assert.True(command.Succeeded);

        // Front half sits at x >= 0, back half at x <= 0 (both touch the plane).
        Assert.All(box.Vertices, v => Assert.True(v.Position.X >= -1e-6,
            $"front-half vertex at x={v.Position.X}"));
        Assert.All(command.NewSector!.Vertices, v => Assert.True(v.Position.X <= 1e-6,
            $"back-half vertex at x={v.Position.X}"));
    }

    [Fact]
    public void HalvesOwnTheirVerticesSeparately()
    {
        var (level, box) = MakeBox(Vec3.Zero);

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), Vec3.Zero);
        command.Apply();

        var other = command.NewSector!;

        // No vertex object is shared between the two sectors, and every corner
        // references a vertex its own sector owns.
        Assert.Empty(box.Vertices.Intersect(other.Vertices));
        foreach (var sector in new[] { box, other })
            foreach (var surf in sector.Surfaces)
                foreach (var c in surf.Corners)
                    Assert.Same(sector, c.Vertex.Sector);
    }

    [Fact]
    public void APlaneThatMissesTheSectorIsANoOp()
    {
        var (level, box) = MakeBox(Vec3.Zero);
        int surfaces = box.Surfaces.Count;
        int vertices = box.Vertices.Count;

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), new Vec3(50, 0, 0));
        command.Apply();

        Assert.False(command.Succeeded);
        Assert.Single(level.Sectors);
        Assert.Equal(surfaces, box.Surfaces.Count);
        Assert.Equal(vertices, box.Vertices.Count);
    }

    [Fact]
    public void UndoRestoresTheOriginalSectorExactly()
    {
        var (level, box) = MakeBox(Vec3.Zero);
        int surfaces = box.Surfaces.Count;
        int vertices = box.Vertices.Count;
        var corners = box.Surfaces.Select(s => s.Corners.Count).ToList();

        var history = new EditHistory();
        history.Do(new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), Vec3.Zero));
        history.Undo();

        Assert.Single(level.Sectors);
        Assert.Equal(surfaces, box.Surfaces.Count);
        Assert.Equal(vertices, box.Vertices.Count);
        Assert.Equal(corners, box.Surfaces.Select(s => s.Corners.Count));
        Assert.All(box.Surfaces, s => Assert.Null(s.Adjoin));
        Assert.All(box.Surfaces, s => Assert.Same(box, s.Sector));
    }

    [Fact]
    public void RedoReproducesTheSameObjects()
    {
        var (level, box) = MakeBox(Vec3.Zero);

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), Vec3.Zero);
        var history = new EditHistory();
        history.Do(command);

        var created = command.NewSector!;
        int frontSurfaces = box.Surfaces.Count;

        history.Undo();
        history.Redo();

        Assert.Equal(2, level.Sectors.Count);
        Assert.Same(created, command.NewSector);           // identity preserved
        Assert.Contains(created, level.Sectors);
        Assert.Equal(frontSurfaces, box.Surfaces.Count);
    }

    [Fact]
    public void CleavingOffCentreSplitsUnevenly()
    {
        var (level, box) = MakeBox(Vec3.Zero, half: 2.0);

        var command = new CleaveSectorCommand(level, box, new Vec3(1, 0, 0), new Vec3(1, 0, 0));
        command.Apply();
        Assert.True(command.Succeeded);

        double frontWidth = box.Vertices.Max(v => v.Position.X) - box.Vertices.Min(v => v.Position.X);
        double backWidth = command.NewSector!.Vertices.Max(v => v.Position.X)
                           - command.NewSector.Vertices.Min(v => v.Position.X);

        Assert.Equal(1.0, frontWidth, 4);   // x from 1 to 2
        Assert.Equal(3.0, backWidth, 4);    // x from -2 to 1
    }

    [Fact]
    public void SectorsOverlapDetectsOverlapAndSeparation()
    {
        var (level, a) = MakeBox(Vec3.Zero);
        var apart = SectorFactory.CreateBox(level, new Vec3(50, 0, 0), 1.0, "dflt.mat", 0);
        var touching = SectorFactory.CreateBox(level, new Vec3(0.5, 0, 0), 1.0, "dflt.mat", 0);

        Assert.False(GeometryOps.SectorsOverlap(a, apart));
        Assert.True(GeometryOps.SectorsOverlap(a, touching));
        Assert.True(GeometryOps.SectorsOverlap(a, a));
    }
}
