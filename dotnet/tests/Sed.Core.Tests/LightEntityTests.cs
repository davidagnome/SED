using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// Lights as first-class selectable objects: selection, create/delete/move
/// commands, and clipboard support.
/// </summary>
public class LightEntityTests
{
    private static (Level level, Light a, Light b) MakeLevelWithLights()
    {
        var level = new Level();
        var sector = SectorFactory.CreateBox(level, Vec3.Zero, 2.0, "dflt.mat", 0);
        level.Sectors.Add(sector);
        level.RenumberSectors();

        var a = new Light { Position = new Vec3(0, 0, 1), Range = 4, Intensity = 1, Color = ColorF.White };
        var b = new Light { Position = new Vec3(1, 0, 1), Range = 6, Intensity = 0.5, Color = new ColorF(1, 0, 0) };
        level.Lights.Add(a);
        level.Lights.Add(b);
        level.RenumberLights();
        return (level, a, b);
    }

    [Fact]
    public void LightsParticipateInTheSelectionSet()
    {
        var (_, a, b) = MakeLevelWithLights();
        var sel = new SelectionSet();

        Assert.True(sel.Add(a));
        Assert.False(sel.Add(a));
        Assert.True(sel.Contains(a));
        Assert.Same(a, sel.PrimaryLight);

        sel.Add(b);
        Assert.Same(b, sel.PrimaryLight);
        Assert.Equal(2, sel.Count);
        Assert.True(sel.IsMultiple);

        sel.Toggle(b);
        Assert.False(sel.Contains(b));
        Assert.Same(a, sel.PrimaryLight);

        sel.SelectOnly(b);
        Assert.Equal(1, sel.Count);
        Assert.Same(b, sel.PrimaryLight);
    }

    [Fact]
    public void SelectOnlyAnotherKindClearsLights()
    {
        var (level, a, _) = MakeLevelWithLights();
        var sel = new SelectionSet();
        sel.Add(a);

        sel.SelectOnly(level.Sectors[0].Surfaces[0]);

        Assert.Null(sel.PrimaryLight);
        Assert.Equal(1, sel.Count);
    }

    [Fact]
    public void PruneDropsLightsRemovedFromTheLevel()
    {
        var (level, a, b) = MakeLevelWithLights();
        var sel = new SelectionSet();
        sel.Add(a);
        sel.Add(b);

        level.Lights.Remove(a);
        sel.Prune(level);

        Assert.Equal(1, sel.Count);
        Assert.Same(b, sel.PrimaryLight);
    }

    [Fact]
    public void CreateLightIsReversible()
    {
        var (level, _, _) = MakeLevelWithLights();
        int before = level.Lights.Count;
        var fresh = new Light { Position = new Vec3(5, 5, 5), Range = 3, Intensity = 1 };

        var history = new EditHistory();
        history.Do(new CreateLightCommand(level, fresh));

        Assert.Equal(before + 1, level.Lights.Count);
        Assert.Equal(before, fresh.Num);            // renumbered on add

        history.Undo();
        Assert.Equal(before, level.Lights.Count);
        Assert.DoesNotContain(fresh, level.Lights);

        history.Redo();
        Assert.Contains(fresh, level.Lights);
    }

    [Fact]
    public void DeleteLightRestoresItsOriginalPosition()
    {
        var (level, a, b) = MakeLevelWithLights();
        var third = new Light { Position = new Vec3(9, 9, 9), Range = 1 };
        level.Lights.Add(third);
        level.RenumberLights();

        // Delete the middle light: undo must put it back at index 1, not append it,
        // because COGs reference lights by number.
        var history = new EditHistory();
        history.Do(new DeleteLightCommand(level, b));

        Assert.Equal(2, level.Lights.Count);
        Assert.Same(a, level.Lights[0]);
        Assert.Same(third, level.Lights[1]);
        Assert.Equal(1, third.Num);

        history.Undo();
        Assert.Equal(3, level.Lights.Count);
        Assert.Same(b, level.Lights[1]);
        Assert.Equal(1, b.Num);
        Assert.Equal(2, third.Num);
    }

    [Fact]
    public void MoveLightIsADeltaAndComposes()
    {
        var (level, a, b) = MakeLevelWithLights();
        var startA = a.Position;
        var startB = b.Position;
        var delta = new Vec3(0, 0, 3);

        var history = new EditHistory();
        history.Do(new CompositeCommand("Move 2 lights", new IEditCommand[]
        {
            new MoveLightCommand(a, delta),
            new MoveLightCommand(b, delta),
        }));

        Assert.Equal(startA.Z + 3, a.Position.Z, 6);
        Assert.Equal(startB.Z + 3, b.Position.Z, 6);

        history.Undo();
        Assert.Equal(startA.Z, a.Position.Z, 6);
        Assert.Equal(startB.Z, b.Position.Z, 6);
        Assert.False(history.CanUndo);      // one step for both lights
    }

    [Fact]
    public void LightsAreCopiedAndPastedAsIndependentClones()
    {
        var (level, a, b) = MakeLevelWithLights();
        var sel = new SelectionSet();
        sel.Add(a);
        sel.Add(b);

        var fragment = LevelFragment.Capture(sel, level);
        Assert.Equal(2, fragment.Lights.Count);

        var history = new EditHistory();
        var paste = new PasteFragmentCommand(level, fragment, new Vec3(10, 0, 0));
        history.Do(paste);

        Assert.Equal(2, paste.PastedLights.Count);
        Assert.Equal(4, level.Lights.Count);

        var copyOfA = paste.PastedLights[0];
        Assert.NotSame(a, copyOfA);
        Assert.Equal(a.Position.X + 10, copyOfA.Position.X, 6);
        Assert.Equal(a.Range, copyOfA.Range, 6);
        Assert.Equal(a.Intensity, copyOfA.Intensity, 6);

        // Colour and flags carry over, and the copy is independent.
        var copyOfB = paste.PastedLights[1];
        Assert.Equal(b.Color, copyOfB.Color);
        copyOfB.Intensity = 99;
        Assert.Equal(0.5, b.Intensity, 6);

        history.Undo();
        Assert.Equal(2, level.Lights.Count);
        Assert.DoesNotContain(copyOfA, level.Lights);
    }

    [Fact]
    public void CopyingOnlyLightsProducesANonEmptyFragment()
    {
        var (level, a, _) = MakeLevelWithLights();
        var sel = new SelectionSet();
        sel.Add(a);

        var fragment = LevelFragment.Capture(sel, level);

        Assert.False(fragment.IsEmpty);
        Assert.Equal(1, fragment.Count);
        Assert.Empty(fragment.Sectors);
        Assert.Empty(fragment.Things);
    }

    [Fact]
    public void FragmentBoundsIncludeLights()
    {
        var (level, a, _) = MakeLevelWithLights();
        a.Position = new Vec3(100, 0, 0);

        var sel = new SelectionSet();
        sel.Add(a);
        var fragment = LevelFragment.Capture(sel, level);

        Assert.Equal(100, fragment.Bounds.Max.X, 6);
    }
}
