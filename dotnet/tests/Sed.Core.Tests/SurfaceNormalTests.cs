using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Core.Validation;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// Regression cover for <see cref="Surface.RecalcNormal"/>. It used to take the
/// cross product of only corners 0/1/2, which collapses to a zero vector when
/// those three are colinear — a shape that occurs throughout retail levels and
/// that corrupted shading, extrude direction, texture-axis choice, and made the
/// consistency checker report false "invalid normal" warnings.
/// </summary>
public class SurfaceNormalTests
{
    private static Surface MakePolygon(params Vec3[] positions)
    {
        var level = new Level();
        var sector = level.NewSector();
        var surf = sector.NewSurface();
        surf.Material = "dflt.mat";
        foreach (var p in positions)
            surf.Corners.Add(new Surface.Corner
            {
                Vertex = sector.AddVertex(p),
                Uv = new TexVertex(0, 0),
                Intensity = ColorF.White,
            });
        surf.RecalcNormal();
        return surf;
    }

    [Fact]
    public void ColinearLeadingCorners_StillProduceAUnitNormal()
    {
        // Corners 0,1,2 lie on the same line along X; the polygon is still a
        // perfectly good planar pentagon in the XY plane.
        var surf = MakePolygon(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(2, 0, 0),
            new Vec3(2, 2, 0),
            new Vec3(0, 2, 0));

        Assert.Equal(1.0, surf.Normal.Length, 6);
        Assert.Equal(1.0, System.Math.Abs(surf.Normal.Z), 6);
    }

    [Fact]
    public void ColinearLeadingCorners_DoNotTripTheConsistencyChecker()
    {
        var surf = MakePolygon(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(2, 0, 0),
            new Vec3(2, 2, 0),
            new Vec3(0, 2, 0));

        var level = new Level();
        level.Sectors.Add(surf.Sector);
        level.RenumberSectors();

        Assert.DoesNotContain(ConsistencyChecker.Check(level),
            i => i.Message.Contains("normal is invalid"));
    }

    [Fact]
    public void WindingConvention_MatchesCrossProductOrientation()
    {
        // Counter-clockwise viewed from +Z ⇒ normal points along +Z, the same
        // orientation the previous cross-product implementation produced.
        var ccw = MakePolygon(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(1, 1, 0),
            new Vec3(0, 1, 0));
        Assert.Equal(1.0, ccw.Normal.Z, 6);

        var cw = MakePolygon(
            new Vec3(0, 1, 0),
            new Vec3(1, 1, 0),
            new Vec3(1, 0, 0),
            new Vec3(0, 0, 0));
        Assert.Equal(-1.0, cw.Normal.Z, 6);
    }

    [Fact]
    public void DegenerateSurface_StillYieldsZeroNormal()
    {
        // Every corner on one line — genuinely zero-area, and the checker
        // should still be able to flag it.
        var surf = MakePolygon(
            new Vec3(0, 0, 0),
            new Vec3(1, 0, 0),
            new Vec3(2, 0, 0),
            new Vec3(3, 0, 0));

        Assert.Equal(0.0, surf.Normal.Length, 6);

        var level = new Level();
        level.Sectors.Add(surf.Sector);
        level.RenumberSectors();

        Assert.Contains(ConsistencyChecker.Check(level),
            i => i.Message.Contains("normal is invalid"));
    }

    [Fact]
    public void BoxSectorFaces_AllPointInward()
    {
        // The engine's convention: sector surface normals face into the sector.
        // Verified against retail data — 20,250 surfaces across four levels are
        // inward with no mixed sector — so new geometry must match, or the
        // lighting pass, extrude direction and auto-texture axis all misbehave.
        var level = new Level();
        var box = SectorFactory.CreateBox(level, new Vec3(3, -2, 5), 1.5, "dflt.mat", 0);

        var centre = Vec3.Zero;
        foreach (var v in box.Vertices) centre += v.Position;
        centre *= 1.0 / box.Vertices.Count;

        Assert.Equal(6, box.Surfaces.Count);
        foreach (var surf in box.Surfaces)
        {
            surf.RecalcNormal();
            double towardCentre = surf.Normal.Dot(centre - surf.Corners[0].Vertex.Position);
            Assert.True(towardCentre > 0,
                $"surface {surf.Num} faces outward (dot {towardCentre:0.###})");
        }
    }

    [Fact]
    public void NonAxisAlignedPolygon_GetsCorrectNormal()
    {
        // Triangle on the plane x + y + z = 1 ⇒ normal ∝ (1,1,1).
        var surf = MakePolygon(
            new Vec3(1, 0, 0),
            new Vec3(0, 1, 0),
            new Vec3(0, 0, 1));

        double k = 1.0 / System.Math.Sqrt(3);
        Assert.Equal(1.0, surf.Normal.Length, 6);
        Assert.Equal(k, System.Math.Abs(surf.Normal.X), 6);
        Assert.Equal(k, System.Math.Abs(surf.Normal.Y), 6);
        Assert.Equal(k, System.Math.Abs(surf.Normal.Z), 6);
    }
}
