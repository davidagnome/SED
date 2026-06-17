using Sed.Core.Math;
using Xunit;

namespace Sed.Core.Tests;

public class MathTests
{
    [Fact]
    public void Cross3_MatchesOriginalFormula()
    {
        // VectorCross3 from math.pas: cx=y1*z2-z1*y2, cy=z1*x2-x1*z2, cz=x1*y2-x2*y1
        var a = new Vec3(1, 0, 0);
        var b = new Vec3(0, 1, 0);
        Assert.Equal(new Vec3(0, 0, 1), a.Cross(b));
    }

    [Fact]
    public void Normalize_ReturnsLength_AndUnitVector()
    {
        var v = new Vec3(0, 3, 4);
        var n = v.Normalized(out var len);
        Assert.Equal(5.0, len, 9);
        Assert.Equal(1.0, n.Length, 9);
    }

    [Fact]
    public void Normalize_Zero_IsNoOp()
    {
        var n = Vec3.Zero.Normalized(out var len);
        Assert.Equal(0.0, len);
        Assert.Equal(Vec3.Zero, n);
    }

    [Fact]
    public void ProjectOntoNormal_GivesScaledNormal()
    {
        var a = new Vec3(2, 3, 0);
        var n = new Vec3(1, 0, 0);
        Assert.Equal(new Vec3(2, 0, 0), a.ProjectOntoNormal(n));
    }
}

public class Mat4Tests
{
    [Fact]
    public void Multiply_ByIdentity_IsUnchanged()
    {
        var t = Mat4.Translate(new Vec3(2, 3, 4));
        var r = t * Mat4.Identity;
        for (int i = 0; i < 16; i++)
            Assert.Equal(t.M[i], r.M[i], 6);
    }

    [Fact]
    public void Translate_StoresInColumnMajorTranslationColumn()
    {
        var t = Mat4.Translate(new Vec3(5, 6, 7));
        Assert.Equal(5f, t.M[12]);
        Assert.Equal(6f, t.M[13]);
        Assert.Equal(7f, t.M[14]);
    }

    [Fact]
    public void Perspective_PutsNegativeWInClipForwardPoint()
    {
        // A right-handed perspective should map a point in front of the camera
        // (negative Z in view space) to positive clip-space w (= -z).
        var p = Mat4.Perspective(System.Math.PI / 2, 1.0, 0.1, 100.0);
        // w row is the third column's .w via M[11] = -1
        Assert.Equal(-1f, p.M[11]);
    }
}

public class ModelTests
{
    [Fact]
    public void Surface_RecalcNormal_FacesUpForCcwFloor()
    {
        var level = new Sed.Core.Model.Level();
        var sec = level.NewSector();
        var s = sec.NewSurface();
        foreach (var p in new[] { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0) })
            s.Corners.Add(new Sed.Core.Model.Surface.Corner { Vertex = sec.AddVertex(p) });

        s.RecalcNormal();
        Assert.Equal(0.0, s.Normal.X, 9);
        Assert.Equal(0.0, s.Normal.Y, 9);
        Assert.Equal(1.0, s.Normal.Z, 9);
    }

    [Fact]
    public void Level_AssignsUniqueIds()
    {
        var level = new Sed.Core.Model.Level();
        var a = level.NewSector();
        var b = level.NewSector();
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(0, a.Num);
        Assert.Equal(1, b.Num);
    }
}
