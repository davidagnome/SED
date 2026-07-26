using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core;

/// <summary>Geometric utility helpers for surface/vertex manipulation.</summary>
public static class GeometryOps
{
    /// <summary>
    /// Picks the plane that splits a surface across its middle: it passes through
    /// the surface centroid with its normal along the surface's longest in-plane
    /// axis, so the cut crosses the long dimension (splitting a wall in two rather
    /// than shaving a strip off one end). Feed the result to
    /// <c>CleaveSurfaceCommand</c>.
    /// </summary>
    public static (Vec3 Normal, Vec3 Point) MidCleavePlane(Surface surface)
    {
        surface.RecalcNormal();
        var n = surface.Normal;

        var centroid = Vec3.Zero;
        foreach (var c in surface.Corners) centroid += c.Vertex.Position;
        if (surface.Corners.Count > 0) centroid *= 1.0 / surface.Corners.Count;

        // Orthonormal basis lying in the surface plane.
        var seed = System.Math.Abs(n.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        var axisU = n.Cross(seed).Normalized();
        var axisV = n.Cross(axisU).Normalized();

        var cutNormal = Spread(surface, centroid, axisU) >= Spread(surface, centroid, axisV) ? axisU : axisV;
        return (cutNormal, centroid);
    }

    /// <summary>Extent of a surface's corners along an axis, measured from an origin.</summary>
    private static double Spread(Surface surface, Vec3 origin, Vec3 axis)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var c in surface.Corners)
        {
            double d = axis.Dot(c.Vertex.Position - origin);
            min = System.Math.Min(min, d);
            max = System.Math.Max(max, d);
        }
        return max > min ? max - min : 0;
    }

    /// <summary>Classifies a point relative to a plane: -1 (behind), 0 (on), +1 (front).</summary>
    public static int ClassifyPoint(Vec3 point, Vec3 planeNormal, Vec3 planePoint, double epsilon = 1e-6)
    {
        double d = planeNormal.Dot(point - planePoint);
        if (d < -epsilon) return -1;
        if (d > epsilon) return 1;
        return 0;
    }

    /// <summary>Intersection of segment a-b with a plane. Returns null if parallel or out of range.</summary>
    public static Vec3? PlaneSegmentIntersection(Vec3 a, Vec3 b, Vec3 normal, Vec3 point)
    {
        var dir = b - a;
        double denom = normal.Dot(dir);
        if (System.Math.Abs(denom) < 1e-9) return null;
        double t = normal.Dot(point - a) / denom;
        if (t < -1e-9 || t > 1 + 1e-9) return null;
        return a + dir * t;
    }
}
