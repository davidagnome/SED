using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class ClipboardTests
{
    private static (Level level, Sector a, Sector b) MakeTwoSectorLevel()
    {
        var level = new Level();
        var a = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "wall.mat", 3);
        var b = SectorFactory.CreateBox(level, new Vec3(2, 0, 0), 1.0, "wall.mat", 3);
        level.Sectors.Add(a);
        level.Sectors.Add(b);
        level.RenumberSectors();
        return (level, a, b);
    }

    [Fact]
    public void CopyingASector_ClonesVerticesRatherThanAliasingThem()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a);

        var fragment = LevelFragment.Capture(sel, level);
        var history = new EditHistory();
        var paste = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        history.Do(paste);

        var pasted = Assert.Single(paste.PastedSectors);
        Assert.NotSame(a, pasted);
        Assert.Equal(a.Vertices.Count, pasted.Vertices.Count);

        // No vertex object may be shared with the source sector.
        foreach (var v in pasted.Vertices)
            Assert.DoesNotContain(v, a.Vertices);

        // Moving the copy must leave the original untouched.
        var originalFirst = a.Vertices[0].Position;
        pasted.Vertices[0].Position += new Vec3(0, 0, 5);
        Assert.Equal(originalFirst.Z, a.Vertices[0].Position.Z, 6);
    }

    [Fact]
    public void PasteAppliesTheOffset()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a);

        var fragment = LevelFragment.Capture(sel, level);
        var paste = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        paste.Apply();

        var pasted = paste.PastedSectors[0];
        for (int i = 0; i < a.Vertices.Count; i++)
            Assert.Equal(a.Vertices[i].Position.X + 10, pasted.Vertices[i].Position.X, 6);
    }

    [Fact]
    public void PasteIsUndoable_AndRedoReusesTheSameObjects()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a);

        var fragment = LevelFragment.Capture(sel, level);
        var history = new EditHistory();
        int before = level.Sectors.Count;

        var paste = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        history.Do(paste);
        Assert.Equal(before + 1, level.Sectors.Count);
        var created = paste.PastedSectors[0];

        history.Undo();
        Assert.Equal(before, level.Sectors.Count);
        Assert.DoesNotContain(created, level.Sectors);

        history.Redo();
        Assert.Equal(before + 1, level.Sectors.Count);
        Assert.Same(created, paste.PastedSectors[0]);   // same object, not a new clone
        Assert.Contains(created, level.Sectors);
    }

    [Fact]
    public void PastingTwiceProducesIndependentCopies()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a);
        var fragment = LevelFragment.Capture(sel, level);

        var first = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        var second = new PasteFragmentCommand(level, fragment, new Vec3(20, 0, 0));
        first.Apply();
        second.Apply();

        var s1 = first.PastedSectors[0];
        var s2 = second.PastedSectors[0];
        Assert.NotSame(s1, s2);

        foreach (var v in s2.Vertices)
            Assert.DoesNotContain(v, s1.Vertices);

        // Editing one copy leaves the other alone.
        double before = s2.Vertices[0].Position.Z;
        s1.Vertices[0].Position += new Vec3(0, 0, 7);
        Assert.Equal(before, s2.Vertices[0].Position.Z, 6);
    }

    [Fact]
    public void AdjoinsInsideTheFragmentAreRemapped_ToTheClonesNotTheOriginals()
    {
        var (level, a, b) = MakeTwoSectorLevel();

        // Portal-link one face of each sector.
        var fa = a.Surfaces[0];
        var fb = b.Surfaces[1];
        new MakeAdjoinCommand(fa, fb).Apply();
        Assert.Same(fb, fa.Adjoin);

        // Copy BOTH sectors — the pair is wholly inside the fragment.
        var sel = new SelectionSet();
        sel.Add(a);
        sel.Add(b);
        var fragment = LevelFragment.Capture(sel, level);

        var paste = new PasteFragmentCommand(level, fragment, new Vec3(50, 0, 0));
        paste.Apply();

        var ca = paste.PastedSectors[0];
        var cb = paste.PastedSectors[1];
        var cfa = ca.Surfaces[0];

        Assert.NotNull(cfa.Adjoin);
        Assert.Same(cb.Surfaces[1], cfa.Adjoin);          // points at the clone
        Assert.NotSame(fb, cfa.Adjoin);                    // not at the original
        Assert.Same(cfa, cfa.Adjoin!.Adjoin);              // mirror pair intact
    }

    [Fact]
    public void AdjoinsLeavingTheFragmentAreCleared_SoAPastedRoomIsSealed()
    {
        var (level, a, b) = MakeTwoSectorLevel();

        var fa = a.Surfaces[0];
        var fb = b.Surfaces[1];
        new MakeAdjoinCommand(fa, fb).Apply();

        // Copy only sector A — its adjoin partner is left behind.
        var sel = new SelectionSet();
        sel.Add(a);
        var fragment = LevelFragment.Capture(sel, level);

        var paste = new PasteFragmentCommand(level, fragment, new Vec3(50, 0, 0));
        paste.Apply();

        var clonedFace = paste.PastedSectors[0].Surfaces[0];
        Assert.Null(clonedFace.Adjoin);
        Assert.Equal(0, clonedFace.AdjoinFlags);

        // The original pair must be undisturbed.
        Assert.Same(fb, fa.Adjoin);
        Assert.Same(fa, fb.Adjoin);
    }

    [Fact]
    public void SelectedSurfacesContributeTheirOwningSector()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a.Surfaces[0]);
        sel.Add(a.Surfaces[1]);   // same sector — must not be captured twice

        var fragment = LevelFragment.Capture(sel, level);

        Assert.Single(fragment.Sectors);
        Assert.Equal(a.Surfaces.Count, fragment.Sectors[0].Surfaces.Count);
    }

    [Fact]
    public void ThingsAreClonedWithValues_AndRemappedIntoACopiedSector()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var thing = new Thing { Name = "crate", Template = "crate_tpl", Sector = a, Position = Vec3.Zero, Yaw = 45 };
        thing.Values["cost"] = "7";
        level.Things.Add(thing);

        var sel = new SelectionSet();
        sel.Add(a);
        sel.Add(thing);
        var fragment = LevelFragment.Capture(sel, level);

        var paste = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        paste.Apply();

        var clone = Assert.Single(paste.PastedThings);
        Assert.NotSame(thing, clone);
        Assert.Equal("crate", clone.Name);
        Assert.Equal("crate_tpl", clone.Template);
        Assert.Equal(45, clone.Yaw, 6);
        Assert.Equal("7", clone.Values["cost"]);
        Assert.Equal(10, clone.Position.X, 6);

        // The copied thing belongs to the copied room, not the original.
        Assert.Same(paste.PastedSectors[0], clone.Sector);

        // Its value dictionary must be independent.
        clone.Values["cost"] = "9";
        Assert.Equal("7", thing.Values["cost"]);
    }

    [Fact]
    public void ThingCopiedWithoutItsSector_StaysInTheOriginalSector()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var thing = new Thing { Name = "lamp", Sector = a, Position = Vec3.Zero };
        level.Things.Add(thing);

        var sel = new SelectionSet();
        sel.Add(thing);                      // no sector selected
        var fragment = LevelFragment.Capture(sel, level);

        var paste = new PasteFragmentCommand(level, fragment, new Vec3(1, 0, 0));
        paste.Apply();

        Assert.Empty(paste.PastedSectors);
        Assert.Same(a, paste.PastedThings[0].Sector);
    }

    [Fact]
    public void CaptureIsASnapshot_LaterEditsToTheSourceDoNotLeakIn()
    {
        var (level, a, _) = MakeTwoSectorLevel();
        var sel = new SelectionSet();
        sel.Add(a);

        var fragment = LevelFragment.Capture(sel, level);

        // Mutate the source after copying.
        foreach (var v in a.Vertices) v.Position += new Vec3(0, 0, 100);

        var paste = new PasteFragmentCommand(level, fragment, Vec3.Zero);
        paste.Apply();

        // The paste reflects the level as it was at copy time.
        Assert.True(paste.PastedSectors[0].Vertices.All(v => v.Position.Z < 50));
    }

    [Fact]
    public void EmptySelectionCapturesNothing()
    {
        var (level, _, _) = MakeTwoSectorLevel();
        var fragment = LevelFragment.Capture(new SelectionSet(), level);

        Assert.True(fragment.IsEmpty);
        Assert.Equal(0, fragment.Count);
    }
}
