using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// Multi-selection editing: a transform over many objects must be a single undo
/// step, and must land every object exactly back where it started on revert.
/// </summary>
public class MultiEditTests
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
    public void CompositeCommand_AppliesInOrder_AndRevertsInReverse()
    {
        var log = new List<string>();

        var composite = new CompositeCommand("test", new[]
        {
            new TraceCommand("a", log),
            new TraceCommand("b", log),
            new TraceCommand("c", log),
        });

        composite.Apply();
        Assert.Equal(new[] { "+a", "+b", "+c" }, log);

        log.Clear();
        composite.Revert();
        Assert.Equal(new[] { "-c", "-b", "-a" }, log);
    }

    [Fact]
    public void CompositeCommand_IsOneUndoStep()
    {
        var (level, sector) = MakeLevel();
        var t1 = new Thing { Name = "one", Sector = sector, Position = Vec3.Zero };
        var t2 = new Thing { Name = "two", Sector = sector, Position = new Vec3(5, 0, 0) };
        level.Things.Add(t1);
        level.Things.Add(t2);

        var history = new EditHistory();
        var delta = new Vec3(0, 0, 2);

        history.Do(new CompositeCommand("Move 2 things", new IEditCommand[]
        {
            new MoveThingCommand(t1, delta),
            new MoveThingCommand(t2, delta),
        }));

        Assert.Equal(2.0, t1.Position.Z, 6);
        Assert.Equal(2.0, t2.Position.Z, 6);

        history.Undo();
        Assert.Equal(0.0, t1.Position.Z, 6);
        Assert.Equal(0.0, t2.Position.Z, 6);
        Assert.False(history.CanUndo);        // one step, not two

        history.Redo();
        Assert.Equal(2.0, t1.Position.Z, 6);
        Assert.Equal(2.0, t2.Position.Z, 6);
    }

    [Fact]
    public void TranslatingASelectionMovesEveryImpliedVertexOnce()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        sel.Add(sector.Surfaces[0]);
        sel.Add(sector.Surfaces[2]);   // adjacent face — shares the edge 0–1

        var verts = sel.AffectedVertices();
        Assert.Equal(6, verts.Count);  // 8 corners, 2 shared
        var before = verts.Select(v => v.Position).ToList();
        var delta = new Vec3(1, 2, 3);

        var history = new EditHistory();
        history.Do(new TransformVerticesCommand(verts, TransformVerticesCommand.Translate(delta), "Move"));

        // Shared vertices must move by exactly one delta, not two.
        for (int i = 0; i < verts.Count; i++)
        {
            Assert.Equal(before[i].X + delta.X, verts[i].Position.X, 6);
            Assert.Equal(before[i].Y + delta.Y, verts[i].Position.Y, 6);
            Assert.Equal(before[i].Z + delta.Z, verts[i].Position.Z, 6);
        }

        history.Undo();
        for (int i = 0; i < verts.Count; i++)
            Assert.Equal(before[i].Z, verts[i].Position.Z, 6);
    }

    [Fact]
    public void CentroidOfASelectionIsItsAveragePosition()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        sel.Add(sector);

        var centroid = TransformVerticesCommand.Centroid(sel.AffectedVertices());

        // The box was built centred on the origin.
        Assert.Equal(0.0, centroid.X, 6);
        Assert.Equal(0.0, centroid.Y, 6);
        Assert.Equal(0.0, centroid.Z, 6);
    }

    [Fact]
    public void RotatingAWholeSectorAboutItsCentroidIsReversible()
    {
        var (_, sector) = MakeLevel();
        var sel = new SelectionSet();
        sel.Add(sector);

        var verts = sel.AffectedVertices();
        var before = verts.Select(v => v.Position).ToList();
        var pivot = TransformVerticesCommand.Centroid(verts);

        var history = new EditHistory();
        history.Do(new TransformVerticesCommand(verts,
            TransformVerticesCommand.RotateZ(pivot, System.Math.PI / 2), "Rotate"));

        Assert.Contains(verts.Zip(before), p =>
            System.Math.Abs(p.First.Position.X - p.Second.X) > 1e-9);

        history.Undo();
        for (int i = 0; i < verts.Count; i++)
        {
            Assert.Equal(before[i].X, verts[i].Position.X, 6);
            Assert.Equal(before[i].Y, verts[i].Position.Y, 6);
        }
    }

    private sealed class TraceCommand(string tag, List<string> log) : IEditCommand
    {
        public string Name => tag;
        public void Apply() => log.Add("+" + tag);
        public void Revert() => log.Add("-" + tag);
    }
}
