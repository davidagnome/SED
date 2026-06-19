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

public class TransformTests
{
    [Fact]
    public void RotateThing_AdjustsYaw_AndReverts()
    {
        var t = new Thing { Yaw = 30 };
        var cmd = new RotateThingCommand(t, 90);
        cmd.Apply(); Assert.Equal(120, t.Yaw, 6);
        cmd.Revert(); Assert.Equal(30, t.Yaw, 6);
    }

    [Fact]
    public void TransformVertices_RotatesAboutPivot_AndReverts()
    {
        var v = new Vertex(new Vec3(1, 0, 0));
        var cmd = new TransformVerticesCommand(new[] { v },
            TransformVerticesCommand.RotateZ(Vec3.Zero, System.Math.PI / 2), "r");
        cmd.Apply();
        Assert.Equal(0, v.Position.X, 6);
        Assert.Equal(1, v.Position.Y, 6);
        cmd.Revert();
        Assert.Equal(new Vec3(1, 0, 0), v.Position);
    }

    [Fact]
    public void TransformVertices_ScalesAboutPivot()
    {
        var v = new Vertex(new Vec3(2, 0, 0));
        var cmd = new TransformVerticesCommand(new[] { v }, TransformVerticesCommand.Scale(Vec3.Zero, 0.5), "s");
        cmd.Apply();
        Assert.Equal(new Vec3(1, 0, 0), v.Position);
    }

    [Fact]
    public void SetMaterial_ChangesAndReverts()
    {
        var level = SampleScene.CreateCube();
        var s = level.Sectors[0].Surfaces[0];
        s.Material = "old.mat"; s.MaterialIndex = 3;
        var cmd = new SetMaterialCommand(s, "new.mat", 7);
        cmd.Apply();
        Assert.Equal("new.mat", s.Material);
        Assert.Equal(7, s.MaterialIndex);
        cmd.Revert();
        Assert.Equal("old.mat", s.Material);
        Assert.Equal(3, s.MaterialIndex);
    }
}

public class LightingTests
{
    [Fact]
    public void SetSectorAmbient_AppliesAndReverts()
    {
        var level = new Level();
        var sec = level.NewSector();
        sec.Ambient = new ColorF(0.2f, 0.2f, 0.2f);
        var cmd = SetSectorAmbientCommand.Adjust(sec, 0.3f);
        cmd.Apply();
        Assert.Equal(0.5f, sec.Ambient.R, 4);
        cmd.Revert();
        Assert.Equal(0.2f, sec.Ambient.R, 4);
    }

    [Fact]
    public void SetVertexLight_ChangesCorners_AndReverts()
    {
        var level = SampleScene.CreateCube();
        var sec = level.Sectors[0];
        foreach (var s in sec.Surfaces)
            foreach (var c in s.Corners) c.Intensity = new ColorF(0.4f, 0.4f, 0.4f);
        var v = sec.Vertices[0];

        var cmd = SetVertexLightCommand.Adjust(sec, v, 0.5f);
        cmd.Apply();
        foreach (var s in sec.Surfaces)
            foreach (var c in s.Corners)
                if (c.Vertex == v) Assert.Equal(0.9f, c.Intensity.R, 4);

        cmd.Revert();
        foreach (var s in sec.Surfaces)
            foreach (var c in s.Corners)
                if (c.Vertex == v) Assert.Equal(0.4f, c.Intensity.R, 4);
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

public class CreateDeleteTests
{
    [Fact]
    public void CreateThing_Undo_Redo()
    {
        var level = new Level();
        var history = new EditHistory();
        var t = new Thing { Name = "x" };
        history.Do(new CreateThingCommand(level, t));
        Assert.Single(level.Things);
        history.Undo();
        Assert.Empty(level.Things);
        history.Redo();
        Assert.Single(level.Things);
    }

    [Fact]
    public void DeleteThing_RestoresAtSameIndex()
    {
        var level = new Level();
        var a = level.NewThing(); var b = level.NewThing(); var c = level.NewThing();
        var history = new EditHistory();
        history.Do(new DeleteThingCommand(level, b));
        Assert.Equal(2, level.Things.Count);
        Assert.DoesNotContain(b, level.Things);
        history.Undo();
        Assert.Equal(3, level.Things.Count);
        Assert.Same(b, level.Things[1]);
    }

    [Fact]
    public void CreateSector_BuildsBoxRoom_AndUndoes()
    {
        var level = new Level();
        var box = SectorFactory.CreateBox(level, Vec3.Zero, 1.0, "wall.mat", 0);
        var history = new EditHistory();
        history.Do(new CreateSectorCommand(level, box));
        Assert.Single(level.Sectors);
        Assert.Equal(8, level.Sectors[0].Vertices.Count);
        Assert.Equal(6, level.Sectors[0].Surfaces.Count);
        Assert.All(level.Sectors[0].Surfaces, s => Assert.Equal(4, s.Corners.Count));
        history.Undo();
        Assert.Empty(level.Sectors);
        history.Redo();
        Assert.Single(level.Sectors);
    }

    [Fact]
    public void DeleteSector_RestoresAtIndex()
    {
        var level = new Level();
        var a = level.NewSector(); var b = level.NewSector(); var c = level.NewSector();
        var history = new EditHistory();
        history.Do(new DeleteSectorCommand(level, b));
        Assert.Equal(2, level.Sectors.Count);
        history.Undo();
        Assert.Equal(3, level.Sectors.Count);
        Assert.Same(b, level.Sectors[1]);
    }

    [Fact]
    public void DeleteVertex_RemovesCorners_AndReverts()
    {
        var level = SampleScene.CreateCube();
        var sector = level.Sectors[0];
        var v = sector.Vertices[0];
        int touching = sector.Surfaces.Count(s => s.Corners.Any(c => c.Vertex == v));
        Assert.Equal(3, touching); // cube corner shared by 3 faces

        var history = new EditHistory();
        history.Do(new DeleteVertexCommand(sector, v));
        Assert.Equal(7, sector.Vertices.Count);
        Assert.DoesNotContain(sector.Surfaces.SelectMany(s => s.Corners), c => c.Vertex == v);

        history.Undo();
        Assert.Equal(8, sector.Vertices.Count);
        Assert.Equal(3, sector.Surfaces.Count(s => s.Corners.Any(c => c.Vertex == v)));
        Assert.All(sector.Surfaces, s => Assert.Equal(4, s.Corners.Count));
    }

    [Fact]
    public void InsertSurfaceVertex_AddsCornerAndVertex_AndReverts()
    {
        var level = SampleScene.CreateCube();
        var sector = level.Sectors[0];
        var surf = sector.Surfaces[0];
        int verts0 = sector.Vertices.Count, corners0 = surf.Corners.Count;

        var history = new EditHistory();
        history.Do(new InsertSurfaceVertexCommand(surf, 0));
        Assert.Equal(corners0 + 1, surf.Corners.Count);
        Assert.Equal(verts0 + 1, sector.Vertices.Count);

        history.Undo();
        Assert.Equal(corners0, surf.Corners.Count);
        Assert.Equal(verts0, sector.Vertices.Count);
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
