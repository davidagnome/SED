using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class SelectionSetTests
{
    private static (Level level, Sector sector) MakeLevel()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "dflt.mat", 0);
        level.Sectors.Add(sector);
        level.RenumberSectors();
        return (level, sector);
    }

    [Fact]
    public void AddIsIdempotent_AndPreservesInsertionOrder()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();

        Assert.True(sel.Add(sector.Surfaces[0]));
        Assert.True(sel.Add(sector.Surfaces[1]));
        Assert.False(sel.Add(sector.Surfaces[0]));   // already present

        Assert.Equal(2, sel.Surfaces.Count);
        Assert.Same(sector.Surfaces[0], sel.Surfaces[0]);
        Assert.Same(sector.Surfaces[1], sel.Surfaces[1]);
    }

    [Fact]
    public void PrimaryIsTheMostRecentlyAdded()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();

        sel.Add(sector.Surfaces[0]);
        Assert.Same(sector.Surfaces[0], sel.PrimarySurface);

        sel.Add(sector.Surfaces[1]);
        Assert.Same(sector.Surfaces[1], sel.PrimarySurface);

        // Removing the primary falls back to the previous one.
        sel.Remove(sector.Surfaces[1]);
        Assert.Same(sector.Surfaces[0], sel.PrimarySurface);
    }

    [Fact]
    public void ToggleAddsThenRemoves()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        var s = sector.Surfaces[0];

        sel.Toggle(s);
        Assert.True(sel.Contains(s));

        sel.Toggle(s);
        Assert.False(sel.Contains(s));
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void SelectOnlyReplacesEverything_AcrossKinds()
    {
        var (level, sector) = MakeLevel();
        var thing = new Thing { Name = "t", Sector = sector };
        level.Things.Add(thing);

        var sel = new SelectionSet();
        sel.Add(sector.Surfaces[0]);
        sel.Add(sector.Vertices[0]);
        sel.Add(thing);
        Assert.Equal(3, sel.Count);

        sel.SelectOnly(sector.Surfaces[1]);
        Assert.Equal(1, sel.Count);
        Assert.Same(sector.Surfaces[1], sel.PrimarySurface);
        Assert.Null(sel.PrimaryThing);
        Assert.Null(sel.PrimaryVertex);
    }

    [Fact]
    public void AffectedVertices_UnionsSurfacesAndSectors_WithoutDuplicates()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();

        // Box faces 0 {0,1,2,3} and 2 {0,1,5,4} are adjacent — they share the
        // edge 0–1, so the union is 6 vertices rather than 8 corners.
        sel.Add(sector.Surfaces[0]);
        sel.Add(sector.Surfaces[2]);
        var fromSurfaces = sel.AffectedVertices();
        Assert.Equal(fromSurfaces.Count, fromSurfaces.Distinct().Count());
        Assert.Equal(6, fromSurfaces.Count);

        // Selecting the whole sector covers every vertex, still deduplicated.
        sel.SelectOnly(sector);
        var fromSector = sel.AffectedVertices();
        Assert.Equal(sector.Vertices.Count, fromSector.Count);
        Assert.Equal(fromSector.Count, fromSector.Distinct().Count());
    }

    [Fact]
    public void AffectedVertices_DeduplicatesAcrossKinds()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();

        var surf = sector.Surfaces[0];
        sel.Add(surf);
        sel.Add(surf.Corners[0].Vertex);   // already implied by the surface

        Assert.Equal(surf.Corners.Count, sel.AffectedVertices().Count);
    }

    [Fact]
    public void ChangedFiresOnRealMutationsOnly()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        int fired = 0;
        sel.Changed += () => fired++;

        sel.Add(sector.Surfaces[0]);
        Assert.Equal(1, fired);

        sel.Add(sector.Surfaces[0]);        // duplicate — no change
        Assert.Equal(1, fired);

        sel.Remove(sector.Surfaces[1]);     // not present — no change
        Assert.Equal(1, fired);

        sel.Clear();
        Assert.Equal(2, fired);

        sel.Clear();                        // already empty — no change
        Assert.Equal(2, fired);
    }

    [Fact]
    public void DeferCoalescesIntoASingleChangedEvent()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        int fired = 0;
        sel.Changed += () => fired++;

        using (sel.Defer())
        {
            foreach (var s in sector.Surfaces) sel.Add(s);
            Assert.Equal(0, fired);         // suppressed inside the scope
        }

        Assert.Equal(1, fired);
        Assert.Equal(sector.Surfaces.Count, sel.Surfaces.Count);
    }

    [Fact]
    public void DeferWithNoChanges_DoesNotFire()
    {
        var sel = new SelectionSet();
        int fired = 0;
        sel.Changed += () => fired++;

        using (sel.Defer()) { }

        Assert.Equal(0, fired);
    }

    [Fact]
    public void PruneDropsObjectsRemovedFromTheLevel()
    {
        var (level, sector) = MakeLevel();
        var thing = new Thing { Name = "gone", Sector = sector };
        level.Things.Add(thing);

        var sel = new SelectionSet();
        sel.Add(sector.Surfaces[0]);
        sel.Add(sector.Vertices[0]);
        sel.Add(thing);
        Assert.Equal(3, sel.Count);

        level.Things.Remove(thing);
        sel.Prune(level);

        Assert.Equal(2, sel.Count);
        Assert.Null(sel.PrimaryThing);
        Assert.NotNull(sel.PrimarySurface);

        // Removing the whole sector prunes its surfaces and vertices too.
        level.Sectors.Remove(sector);
        sel.Prune(level);
        Assert.True(sel.IsEmpty);
    }

    [Fact]
    public void IsMultipleCountsAcrossKinds()
    {
        var (level, sector) = MakeLevel();
        var thing = new Thing { Name = "t", Sector = sector };
        level.Things.Add(thing);

        var sel = new SelectionSet();
        sel.Add(sector.Surfaces[0]);
        Assert.False(sel.IsMultiple);

        sel.Add(thing);
        Assert.True(sel.IsMultiple);
        Assert.Equal(2, sel.Count);
    }
}
