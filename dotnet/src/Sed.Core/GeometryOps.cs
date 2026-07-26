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

    /// <summary>
    /// True when a point (assumed to lie on the surface's plane) falls inside the
    /// polygon. Tests in 2D after dropping the axis the normal is most aligned
    /// with — the projection that keeps the polygon largest and avoids degenerate
    /// edge-on cases.
    /// </summary>
    public static bool PointOnSurface(Surface surface, Vec3 point)
    {
        var n = surface.Normal;
        if (n.LengthSquared < 1e-12)
        {
            surface.RecalcNormal();
            n = surface.Normal;
        }

        double ax = System.Math.Abs(n.X), ay = System.Math.Abs(n.Y), az = System.Math.Abs(n.Z);
        int drop = az >= ax && az >= ay ? 2 : ay >= ax ? 1 : 0;

        var (px, py) = Flatten(point, drop);
        bool inside = false;

        var corners = surface.Corners;
        for (int i = 0, j = corners.Count - 1; i < corners.Count; j = i++)
        {
            var (ix, iy) = Flatten(corners[i].Vertex.Position, drop);
            var (jx, jy) = Flatten(corners[j].Vertex.Position, drop);

            if ((iy > py) != (jy > py) &&
                px < (jx - ix) * (py - iy) / (jy - iy + 1e-30) + ix)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>Drops one axis, projecting a point into the plane's dominant 2D basis.</summary>
    private static (double u, double v) Flatten(Vec3 p, int drop) => drop switch
    {
        0 => (p.Y, p.Z),
        1 => (p.X, p.Z),
        _ => (p.X, p.Y),
    };

    /// <summary>Centroid of a surface's corners.</summary>
    public static Vec3 Centroid(Surface surface)
    {
        if (surface.Corners.Count == 0) return Vec3.Zero;
        var sum = Vec3.Zero;
        foreach (var c in surface.Corners) sum += c.Vertex.Position;
        return sum * (1.0 / surface.Corners.Count);
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
