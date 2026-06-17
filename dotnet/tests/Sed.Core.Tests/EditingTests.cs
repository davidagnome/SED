using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

public class EditingTests
{
    [Fact]
    public void MoveThing_Apply_Revert_RoundTrips()
    {
        var thing = new Thing { Position = new Vec3(1, 2, 3) };
        var cmd = new MoveThingCommand(thing, new Vec3(10, 0, -5));
        cmd.Apply();
        Assert.Equal(new Vec3(11, 2, -2), thing.Position);
        cmd.Revert();
        Assert.Equal(new Vec3(1, 2, 3), thing.Position);
    }

    [Fact]
    public void History_Undo_Redo_RestoresState()
    {
        var thing = new Thing { Position = Vec3.Zero };
        var history = new EditHistory();

        history.Do(new MoveThingCommand(thing, new Vec3(1, 0, 0)));
        history.Do(new MoveThingCommand(thing, new Vec3(0, 2, 0)));
        Assert.Equal(new Vec3(1, 2, 0), thing.Position);
        Assert.True(history.CanUndo);

        history.Undo();
        Assert.Equal(new Vec3(1, 0, 0), thing.Position);
        history.Undo();
        Assert.Equal(Vec3.Zero, thing.Position);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(new Vec3(1, 0, 0), thing.Position);
    }

    [Fact]
    public void History_NewEdit_ClearsRedo()
    {
        var thing = new Thing { Position = Vec3.Zero };
        var history = new EditHistory();
        history.Do(new MoveThingCommand(thing, new Vec3(1, 0, 0)));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Do(new MoveThingCommand(thing, new Vec3(0, 0, 1)));
        Assert.False(history.CanRedo);
        Assert.Equal(new Vec3(0, 0, 1), thing.Position);
    }
}

public class GeometryEditTests
{
    [Fact]
    public void MoveVertex_RoundTrips()
    {
        var v = new Vertex(new Vec3(1, 2, 3));
        var cmd = new MoveVertexCommand(v, new Vec3(0, 0, 5));
        cmd.Apply();
        Assert.Equal(new Vec3(1, 2, 8), v.Position);
        cmd.Revert();
        Assert.Equal(new Vec3(1, 2, 3), v.Position);
    }

    [Fact]
    public void MoveSurface_MovesAllDistinctVertices_AndReverts()
    {
        var level = SampleScene.CreateCube();
        var surf = level.Sectors[0].Surfaces[0];
        var before = surf.Corners.Select(c => c.Vertex.Position).ToList();

        var cmd = new MoveSurfaceCommand(surf, new Vec3(10, 0, 0));
        cmd.Apply();
        for (int i = 0; i < surf.Corners.Count; i++)
            Assert.Equal(before[i] + new Vec3(10, 0, 0), surf.Corners[i].Vertex.Position);

        cmd.Revert();
        for (int i = 0; i < surf.Corners.Count; i++)
            Assert.Equal(before[i], surf.Corners[i].Vertex.Position);
    }

    [Fact]
    public void PickVertex_HitsAimedVertex()
    {
        var level = SampleScene.CreateCube(); // vertices at ±1
        var ray = new Ray(new Vec3(1, 1, 5), new Vec3(0, 0, -1)); // aimed at (1,1,1)
        var hit = Picker.PickVertex(level, ray, 0.5);
        Assert.NotNull(hit);
        Assert.Equal(1.0, hit!.Vertex.Position.Z, 6);
    }
}

public class ThingPickTests
{
    [Fact]
    public void RaySphere_HitsCenteredSphere()
    {
        var ray = new Ray(new Vec3(0, 0, 5), new Vec3(0, 0, -1));
        Assert.True(Picker.RaySphere(ray, Vec3.Zero, 1.0, out double t));
        Assert.Equal(4.0, t, 6);
    }

    [Fact]
    public void PickThing_ReturnsNearestThing()
    {
        var level = new Level();
        var near = level.NewThing(); near.Position = new Vec3(0, 0, 0);
        var far = level.NewThing(); far.Position = new Vec3(0, 0, -10);

        var ray = new Ray(new Vec3(0, 0, 5), new Vec3(0, 0, -1));
        var hit = Picker.PickThing(level, ray, 1.0);
        Assert.NotNull(hit);
        Assert.Same(near, hit!.Thing);
    }
}
