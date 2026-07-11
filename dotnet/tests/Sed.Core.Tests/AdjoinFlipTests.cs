using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class AdjoinFlipTests
{
    private static Surface MakeQuad(Level level)
    {
        var sector = level.NewSector();
        var v0 = sector.AddVertex(new Vec3(0, 0, 0));
        var v1 = sector.AddVertex(new Vec3(1, 0, 0));
        var v2 = sector.AddVertex(new Vec3(1, 1, 0));
        var v3 = sector.AddVertex(new Vec3(0, 1, 0));
        var surf = sector.NewSurface();
        surf.Corners.Add(new Surface.Corner { Vertex = v0 });
        surf.Corners.Add(new Surface.Corner { Vertex = v1 });
        surf.Corners.Add(new Surface.Corner { Vertex = v2 });
        surf.Corners.Add(new Surface.Corner { Vertex = v3 });
        surf.RecalcNormal();
        return surf;
    }

    [Fact]
    public void FlipSurface_ReversesCorners_AndNegatesNormal()
    {
        var level = new Level();
        var surf = MakeQuad(level);
        var originalNormal = surf.Normal;
        var originalVertices = surf.Corners.Select(c => c.Vertex).ToList();

        var cmd = new FlipSurfaceCommand(surf);
        cmd.Apply();

        Assert.Equal(originalVertices.AsEnumerable().Reverse(), surf.Corners.Select(c => c.Vertex));
        var negated = Vec3.Zero - originalNormal;
        Assert.Equal(negated.X, surf.Normal.X, 6);
        Assert.Equal(negated.Y, surf.Normal.Y, 6);
        Assert.Equal(negated.Z, surf.Normal.Z, 6);

        cmd.Revert();

        Assert.Equal(originalVertices, surf.Corners.Select(c => c.Vertex));
        Assert.Equal(originalNormal.X, surf.Normal.X, 6);
        Assert.Equal(originalNormal.Y, surf.Normal.Y, 6);
        Assert.Equal(originalNormal.Z, surf.Normal.Z, 6);
    }

    [Fact]
    public void MakeAdjoin_CreatesMirrorPair()
    {
        var level = new Level();
        var a = MakeQuad(level);
        var b = MakeQuad(level);

        var cmd = new MakeAdjoinCommand(a, b);
        cmd.Apply();

        Assert.Same(b, a.Adjoin);
        Assert.Same(a, b.Adjoin);

        cmd.Revert();

        Assert.Null(a.Adjoin);
        Assert.Null(b.Adjoin);
    }

    [Fact]
    public void RemoveAdjoin_ClearsBothSides()
    {
        var level = new Level();
        var a = MakeQuad(level);
        var b = MakeQuad(level);

        var make = new MakeAdjoinCommand(a, b);
        make.Apply();
        Assert.Same(b, a.Adjoin);

        var cmd = new RemoveAdjoinCommand(a);
        cmd.Apply();

        Assert.Null(a.Adjoin);
        Assert.Null(b.Adjoin);

        cmd.Revert();

        Assert.Same(b, a.Adjoin);
        Assert.Same(a, b.Adjoin);
    }

    [Fact]
    public void FlipSurface_ThroughEditHistory()
    {
        var level = new Level();
        var surf = MakeQuad(level);
        var originalNormal = surf.Normal;
        var originalVertices = surf.Corners.Select(c => c.Vertex).ToList();

        var history = new EditHistory();
        history.Do(new FlipSurfaceCommand(surf));

        Assert.Equal(originalVertices.AsEnumerable().Reverse(), surf.Corners.Select(c => c.Vertex));
        Assert.True(history.CanUndo);

        history.Undo();

        Assert.Equal(originalVertices, surf.Corners.Select(c => c.Vertex));
        Assert.Equal(originalNormal.X, surf.Normal.X, 6);
        Assert.Equal(originalNormal.Y, surf.Normal.Y, 6);
        Assert.Equal(originalNormal.Z, surf.Normal.Z, 6);
    }
}
