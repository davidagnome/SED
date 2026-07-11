using Sed.Core.Math;

namespace Sed.Core;

/// <summary>Geometric utility helpers for surface/vertex manipulation.</summary>
public static class GeometryOps
{
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
