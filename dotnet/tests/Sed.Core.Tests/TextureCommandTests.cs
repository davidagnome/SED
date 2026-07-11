using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

public class TextureCommandTests
{
    private static Surface MakeSquareSurface()
    {
        var level = new Level();
        var sector = level.NewSector();
        var surf = sector.NewSurface();
        var v0 = sector.AddVertex(new Vec3(0, 0, 0));
        var v1 = sector.AddVertex(new Vec3(1, 0, 0));
        var v2 = sector.AddVertex(new Vec3(1, 1, 0));
        var v3 = sector.AddVertex(new Vec3(0, 1, 0));
        surf.Corners.Add(new Surface.Corner { Vertex = v0, Uv = new TexVertex(0, 0) });
        surf.Corners.Add(new Surface.Corner { Vertex = v1, Uv = new TexVertex(64, 0) });
        surf.Corners.Add(new Surface.Corner { Vertex = v2, Uv = new TexVertex(64, 64) });
        surf.Corners.Add(new Surface.Corner { Vertex = v3, Uv = new TexVertex(0, 64) });
        surf.RecalcNormal();
        return surf;
    }

    [Fact]
    public void ShiftTexture_AddsDeltaToAllCorners()
    {
        var surf = MakeSquareSurface();
        var before = surf.Corners.Select(c => c.Uv).ToList();

        var cmd = new ShiftTextureCommand(surf, 10, 5);
        cmd.Apply();
        for (int i = 0; i < surf.Corners.Count; i++)
        {
            Assert.Equal(before[i].U + 10, surf.Corners[i].Uv.U);
            Assert.Equal(before[i].V + 5, surf.Corners[i].Uv.V);
        }

        cmd.Revert();
        for (int i = 0; i < surf.Corners.Count; i++)
        {
            Assert.Equal(before[i].U, surf.Corners[i].Uv.U);
            Assert.Equal(before[i].V, surf.Corners[i].Uv.V);
        }
    }

    [Fact]
    public void ScaleTexture_ScalesAboutPivot()
    {
        var surf = MakeSquareSurface();
        // pivot = corner[0].Uv = (0,0); set corner[1] to a known offset
        surf.Corners[0].Uv = new TexVertex(0, 0);
        surf.Corners[1].Uv = new TexVertex(2, 3);

        var cmd = new ScaleTextureCommand(surf, 2, 2);
        cmd.Apply();
        // (0,0) pivot stays; (2,3) → (4,6)
        Assert.Equal(0, surf.Corners[0].Uv.U, 6);
        Assert.Equal(0, surf.Corners[0].Uv.V, 6);
        Assert.Equal(4, surf.Corners[1].Uv.U, 6);
        Assert.Equal(6, surf.Corners[1].Uv.V, 6);

        cmd.Revert();
        Assert.Equal(2, surf.Corners[1].Uv.U, 6);
        Assert.Equal(3, surf.Corners[1].Uv.V, 6);
    }

    [Fact]
    public void RotateTexture_90Degrees()
    {
        var surf = MakeSquareSurface();
        // pivot = corner[0].Uv = (0,0); corner[1] at (1,0) relative to pivot
        surf.Corners[0].Uv = new TexVertex(0, 0);
        surf.Corners[1].Uv = new TexVertex(1, 0);

        var cmd = new RotateTextureCommand(surf, 90);
        cmd.Apply();
        // (1,0) relative to pivot → (0,1)
        Assert.Equal(0, surf.Corners[1].Uv.U, 6);
        Assert.Equal(1, surf.Corners[1].Uv.V, 6);

        cmd.Revert();
        Assert.Equal(1, surf.Corners[1].Uv.U, 6);
        Assert.Equal(0, surf.Corners[1].Uv.V, 6);
    }

    [Fact]
    public void AutoTexture_FitsToBox()
    {
        var surf = MakeSquareSurface();
        // world positions span 0..1 on X and Y; normal is +Z → projects to X/Y

        var cmd = new AutoTextureCommand(surf, 64, 64);
        cmd.Apply();

        double minU = surf.Corners.Min(c => c.Uv.U);
        double maxU = surf.Corners.Max(c => c.Uv.U);
        double minV = surf.Corners.Min(c => c.Uv.V);
        double maxV = surf.Corners.Max(c => c.Uv.V);

        Assert.Equal(0, minU, 6);
        Assert.Equal(64, maxU, 6);
        Assert.Equal(0, minV, 6);
        Assert.Equal(64, maxV, 6);

        cmd.Revert();
        // original UVs restored: (0,0), (64,0), (64,64), (0,64)
        Assert.Equal(0, surf.Corners[0].Uv.U, 6);
        Assert.Equal(0, surf.Corners[0].Uv.V, 6);
        Assert.Equal(64, surf.Corners[2].Uv.U, 6);
        Assert.Equal(64, surf.Corners[2].Uv.V, 6);
    }
}
