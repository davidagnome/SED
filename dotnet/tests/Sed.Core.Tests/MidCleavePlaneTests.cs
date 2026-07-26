using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Xunit;

namespace Sed.Core.Tests;

/// <summary>
/// Covers <see cref="GeometryOps.MidCleavePlane"/> — the plane the editor's
/// Geometry ▸ Cleave Surface command uses when the user has not drawn one.
/// </summary>
public class MidCleavePlaneTests
{
    /// <summary>An axis-aligned rectangle in the XY plane, `width` × `height`, cornered at the origin.</summary>
    private static (Sector sector, Surface surf) MakeRect(double width, double height)
    {
        var level = new Level();
        var sector = level.NewSector();
        var v0 = sector.AddVertex(new Vec3(0, 0, 0));
        var v1 = sector.AddVertex(new Vec3(width, 0, 0));
        var v2 = sector.AddVertex(new Vec3(width, height, 0));
        var v3 = sector.AddVertex(new Vec3(0, height, 0));

        var surf = sector.NewSurface();
        surf.Material = "dflt.mat";
        foreach (var v in new[] { v0, v1, v2, v3 })
            surf.Corners.Add(new Surface.Corner { Vertex = v, Uv = new TexVertex(0, 0), Intensity = ColorF.White });
        surf.RecalcNormal();
        return (sector, surf);
    }

    [Fact]
    public void Plane_PassesThroughCentroid()
    {
        var (_, surf) = MakeRect(4, 2);
        var (_, point) = GeometryOps.MidCleavePlane(surf);

        Assert.Equal(2.0, point.X, 6);
        Assert.Equal(1.0, point.Y, 6);
        Assert.Equal(0.0, point.Z, 6);
    }

    [Fact]
    public void Normal_LiesInSurfacePlane_AndIsUnitLength()
    {
        var (_, surf) = MakeRect(4, 2);
        var (normal, _) = GeometryOps.MidCleavePlane(surf);

        Assert.Equal(1.0, normal.Length, 6);
        // Perpendicular to the surface normal ⇒ the cut plane is not parallel to the face.
        Assert.Equal(0.0, normal.Dot(surf.Normal), 6);
    }

    [Fact]
    public void CutsAcrossTheLongAxis_WhenWiderThanTall()
    {
        var (_, surf) = MakeRect(8, 1);
        var (normal, _) = GeometryOps.MidCleavePlane(surf);

        // Long axis is X, so the plane normal must point along X (either sign).
        Assert.Equal(1.0, System.Math.Abs(normal.X), 6);
        Assert.Equal(0.0, System.Math.Abs(normal.Y), 6);
    }

    [Fact]
    public void CutsAcrossTheLongAxis_WhenTallerThanWide()
    {
        var (_, surf) = MakeRect(1, 8);
        var (normal, _) = GeometryOps.MidCleavePlane(surf);

        Assert.Equal(1.0, System.Math.Abs(normal.Y), 6);
        Assert.Equal(0.0, System.Math.Abs(normal.X), 6);
    }

    [Fact]
    public void FeedingItToCleave_SplitsTheSurfaceInTwo()
    {
        var (sector, surf) = MakeRect(8, 1);
        var (normal, point) = GeometryOps.MidCleavePlane(surf);

        var history = new EditHistory();
        history.Do(new CleaveSurfaceCommand(surf, normal, point));

        Assert.Equal(2, sector.Surfaces.Count);
        Assert.Equal(6, sector.Vertices.Count);          // 4 original + 2 edge crossings
        Assert.Equal(4, surf.Corners.Count);             // each half stays a quad
        Assert.Equal(4, sector.Surfaces[^1].Corners.Count);

        history.Undo();
        Assert.Single(sector.Surfaces);
        Assert.Equal(4, sector.Vertices.Count);
        Assert.Equal(4, surf.Corners.Count);
    }

    [Fact]
    public void HalvesHaveEqualArea_ForARectangle()
    {
        var (sector, surf) = MakeRect(8, 2);
        var (normal, point) = GeometryOps.MidCleavePlane(surf);

        new CleaveSurfaceCommand(surf, normal, point).Apply();

        // A mid-plane cut of a rectangle yields two 4×2 halves.
        Assert.Equal(4.0, BoundingExtent(surf, new Vec3(1, 0, 0)), 6);
        Assert.Equal(4.0, BoundingExtent(sector.Surfaces[^1], new Vec3(1, 0, 0)), 6);
    }

    private static double BoundingExtent(Surface surf, Vec3 axis)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var c in surf.Corners)
        {
            double d = axis.Dot(c.Vertex.Position);
            min = System.Math.Min(min, d);
            max = System.Math.Max(max, d);
        }
        return max - min;
    }
}
