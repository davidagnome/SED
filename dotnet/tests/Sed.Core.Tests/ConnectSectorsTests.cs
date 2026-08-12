using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class ConnectSectorsTests
{
    /// <summary>Two boxes offset along X so they share a slab of volume.</summary>
    private static (Level level, Sector a, Sector b) MakeOverlappingBoxes(double offset = 1.0)
    {
        var level = new Level();
        var a = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "wall.mat", 0);
        var b = SectorFactory.CreateBox(level, new Vec3(offset, 0, 0), 1.0, "wall.mat", 0);
        level.Sectors.Add(a);
        level.Sectors.Add(b);
        level.RenumberSectors();
        return (level, a, b);
    }

    [Fact]
    public void ValidationRejectsTheObviousCases()
    {
        var (level, a, b) = MakeOverlappingBoxes();
        var apart = SectorFactory.CreateBox(level, new Vec3(50, 0, 0), 1.0, "wall.mat", 0);
        level.Sectors.Add(apart);

        Assert.Contains("different sectors", ConnectSectorsCommand.Validate(a, a));
        Assert.Contains("do not overlap", ConnectSectorsCommand.Validate(a, apart));
        Assert.Null(ConnectSectorsCommand.Validate(a, b));
    }

    [Fact]
    public void ConnectingOverlappingBoxesRemovesTheDuplicateVolume()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        var command = new ConnectSectorsCommand(level, a, b);
        var history = new EditHistory();
        history.Do(command);

        Assert.Null(command.Failure);

        // The second sector — which held the duplicated overlap — is gone.
        Assert.DoesNotContain(b, level.Sectors);

        // The remaining sectors no longer overlap each other.
        for (int i = 0; i < level.Sectors.Count; i++)
            for (int j = i + 1; j < level.Sectors.Count; j++)
                Assert.False(GeometryOps.SectorsOverlap(level.Sectors[i], level.Sectors[j]),
                    $"sectors {i} and {j} still overlap");
    }

    [Fact]
    public void TheSharedBoundaryBecomesAPortal()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        var command = new ConnectSectorsCommand(level, a, b);
        command.Apply();

        Assert.Null(command.Failure);

        // Most portals come from the cleaving itself — each cleave adjoins the two
        // halves it creates — so PortalsCreated (the extra pass) is often zero.
        // What matters is that the level ends up with valid portals.
        // Two boxes offset along X become three slabs: remainder | overlap |
        // remainder. Both internal boundaries must be portalled, so the middle
        // slab has a portal on each side — four adjoined surfaces, two pairs.
        Assert.Equal(3, level.Sectors.Count);
        int adjoined = level.Sectors.Sum(s => s.Surfaces.Count(x => x.Adjoin is not null));
        Assert.Equal(4, adjoined);

        var middle = level.Sectors.Single(s => s.Surfaces.Count(x => x.Adjoin is not null) == 2);
        Assert.Same(a, middle);   // the original sector keeps the overlap volume

        // Every adjoin is a valid mirror pair pointing at a live sector.
        var live = new HashSet<Sector>(level.Sectors);
        foreach (var sector in level.Sectors)
            foreach (var surf in sector.Surfaces)
            {
                if (surf.Adjoin is not { } partner) continue;
                Assert.Same(surf, partner.Adjoin);
                Assert.Contains(partner.Sector, live);
                Assert.NotSame(surf.Sector, partner.Sector);
            }
    }

    [Fact]
    public void DeletingASectorClearsAdjoinsPointingIntoIt()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        // Portal a pair of faces, then delete the sector one of them lives in.
        new MakeAdjoinCommand(a.Surfaces[0], b.Surfaces[1]).Apply();
        Assert.NotNull(a.Surfaces[0].Adjoin);

        var history = new EditHistory();
        history.Do(new DeleteSectorCommand(level, b));

        Assert.Null(a.Surfaces[0].Adjoin);
        Assert.Equal(0, a.Surfaces[0].AdjoinFlags);

        history.Undo();
        Assert.Same(b.Surfaces[1], a.Surfaces[0].Adjoin);
    }

    [Fact]
    public void UndoRestoresBothSectorsExactly()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        int sectors = level.Sectors.Count;
        int aSurfaces = a.Surfaces.Count, aVertices = a.Vertices.Count;
        int bSurfaces = b.Surfaces.Count, bVertices = b.Vertices.Count;

        var history = new EditHistory();
        history.Do(new ConnectSectorsCommand(level, a, b));
        history.Undo();

        Assert.Equal(sectors, level.Sectors.Count);
        Assert.Contains(a, level.Sectors);
        Assert.Contains(b, level.Sectors);

        Assert.Equal(aSurfaces, a.Surfaces.Count);
        Assert.Equal(aVertices, a.Vertices.Count);
        Assert.Equal(bSurfaces, b.Surfaces.Count);
        Assert.Equal(bVertices, b.Vertices.Count);

        // And nothing is left adjoined.
        Assert.All(a.Surfaces, s => Assert.Null(s.Adjoin));
        Assert.All(b.Surfaces, s => Assert.Null(s.Adjoin));
        Assert.All(a.Surfaces, s => Assert.Same(a, s.Sector));
        Assert.All(b.Surfaces, s => Assert.Same(b, s.Sector));
    }

    [Fact]
    public void RedoReproducesTheResult()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        var command = new ConnectSectorsCommand(level, a, b);
        var history = new EditHistory();
        history.Do(command);

        int after = level.Sectors.Count;
        int portals = command.PortalsCreated;

        history.Undo();
        history.Redo();

        Assert.Equal(after, level.Sectors.Count);
        Assert.Equal(portals, command.PortalsCreated);
        Assert.DoesNotContain(b, level.Sectors);
    }

    [Fact]
    public void EverySurvivingSectorKeepsItsOwnVertices()
    {
        var (level, a, b) = MakeOverlappingBoxes();

        new ConnectSectorsCommand(level, a, b).Apply();

        foreach (var sector in level.Sectors)
            foreach (var surf in sector.Surfaces)
                foreach (var c in surf.Corners)
                    Assert.Same(sector, c.Vertex.Sector);
    }

    [Fact]
    public void NonOverlappingSectorsAreLeftAlone()
    {
        var (level, a, _) = MakeOverlappingBoxes();
        var apart = SectorFactory.CreateBox(level, new Vec3(50, 0, 0), 1.0, "wall.mat", 0);
        level.Sectors.Add(apart);
        level.RenumberSectors();

        int sectors = level.Sectors.Count;
        int surfaces = a.Surfaces.Count;

        var command = new ConnectSectorsCommand(level, a, apart);
        command.Apply();

        Assert.NotNull(command.Failure);
        Assert.Equal(sectors, level.Sectors.Count);
        Assert.Equal(surfaces, a.Surfaces.Count);
    }
}
