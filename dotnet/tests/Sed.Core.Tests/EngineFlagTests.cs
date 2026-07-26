using Sed.Core.Model;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// The flag bits are engine ABI — they are written verbatim into the JKL surface
/// line — so they are pinned here against the original's declarations. Several
/// were wrong before: SF_DoubleRes/HalfRes were off by a bit, SF_Water was
/// 0x1000 instead of 0x20000, and FF_TexClampX/Y were shifted one bit with a
/// bogus "TexFlip = 0x04" sitting on top of the real FF_TexClampX.
/// </summary>
public class EngineFlagTests
{
    [Fact]
    public void SurfaceFlagsMatchJLevelPas()
    {
        Assert.Equal(0x1, SurfaceFlags.Floor);
        Assert.Equal(0x2, SurfaceFlags.CogLinked);
        Assert.Equal(0x4, SurfaceFlags.Collision);
        Assert.Equal(0x10, SurfaceFlags.DoubleRes);
        Assert.Equal(0x20, SurfaceFlags.HalfRes);
        Assert.Equal(0x40, SurfaceFlags.EighthRes);
        Assert.Equal(0x200, SurfaceFlags.SkyHorizon);
        Assert.Equal(0x400, SurfaceFlags.SkyCeiling);
        Assert.Equal(0x20000, SurfaceFlags.Water);
        Assert.Equal(0x4000000, SurfaceFlags.QuarterRes);
        Assert.Equal(0x8000000, SurfaceFlags.QuadrupleRes);
    }

    [Fact]
    public void FaceFlagsMatchGeometryPas()
    {
        Assert.Equal(0x01, FaceFlags.DoubleSided);
        Assert.Equal(0x02, FaceFlags.Translucent);
        Assert.Equal(0x04, FaceFlags.TexClampX);
        Assert.Equal(0x08, FaceFlags.TexClampY);
        Assert.Equal(0x10, FaceFlags.TexNoFiltering);
        Assert.Equal(0x20, FaceFlags.ZWriteDisabled);
    }

    [Fact]
    public void SkyAndTranslucentHelpersAgreeWithTheConstants()
    {
        var level = new Level();
        var sector = level.NewSector();
        var surf = sector.NewSurface();

        surf.SurfFlags = SurfaceFlags.SkyCeiling;
        Assert.True(surf.IsSky);

        surf.SurfFlags = SurfaceFlags.SkyHorizon;
        Assert.True(surf.IsSky);

        surf.SurfFlags = SurfaceFlags.Collision;
        Assert.False(surf.IsSky);

        surf.FaceFlags = FaceFlags.Translucent;
        Assert.True(surf.IsTranslucent);
    }

    [Fact]
    public void NoTwoFlagsInAGroupShareABit()
    {
        long[] surface =
        {
            SurfaceFlags.Floor, SurfaceFlags.CogLinked, SurfaceFlags.Collision,
            SurfaceFlags.DoubleRes, SurfaceFlags.HalfRes, SurfaceFlags.EighthRes,
            SurfaceFlags.SkyHorizon, SurfaceFlags.SkyCeiling, SurfaceFlags.Water,
            SurfaceFlags.QuarterRes, SurfaceFlags.QuadrupleRes,
        };
        Assert.Equal(surface.Length, surface.Distinct().Count());

        long[] face =
        {
            FaceFlags.DoubleSided, FaceFlags.Translucent, FaceFlags.TexClampX,
            FaceFlags.TexClampY, FaceFlags.TexNoFiltering, FaceFlags.ZWriteDisabled,
        };
        Assert.Equal(face.Length, face.Distinct().Count());
    }

    [Fact]
    public void AdjoinAndSectorFlagsAreUnchanged()
    {
        Assert.Equal(0x01, AdjoinFlags.Visible);
        Assert.Equal(0x02, AdjoinFlags.Move);
        Assert.Equal(0x04, AdjoinFlags.AllowSoundPass);
        Assert.Equal(0x80000000, AdjoinFlags.BlockLight);
        Assert.Equal(0x40000000, SectorFlags.NoAmbientLight);
    }
}
