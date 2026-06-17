using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Rendering;

/// <summary>A world-space ray (origin + unit direction).</summary>
public readonly record struct Ray(Vec3 Origin, Vec3 Direction);

/// <summary>The surface hit by a pick ray, with its sector and hit distance/point.</summary>
public sealed record PickResult(Sector Sector, Surface Surface, double Distance, Vec3 Point);

/// <summary>A thing hit by a pick ray (sphere test around the thing's position).</summary>
public sealed record ThingHit(Thing Thing, double Distance);

/// <summary>
/// Ray casting for viewport selection: builds a pick ray from a screen pixel and
/// the camera, and finds the nearest surface a ray strikes (Möller–Trumbore over
/// each surface's fan triangulation).
/// </summary>
public static class Picker
{
    private const double Epsilon = 1e-9;

    /// <summary>Builds a world-space ray through a viewport pixel (origin at the camera).</summary>
    public static Ray ScreenPointToRay(Camera camera, double px, double py, double width, double height)
    {
        double aspect = width / height;
        double tanY = System.Math.Tan(camera.FieldOfViewDegrees * System.Math.PI / 180.0 / 2.0);
        double tanX = tanY * aspect;

        double ndcX = 2.0 * px / width - 1.0;
        double ndcY = 2.0 * py / height - 1.0; // screen y is top-down

        var dir = camera.Forward
                  + camera.Right * (ndcX * tanX)
                  + camera.CameraUp * (-ndcY * tanY);
        return new Ray(camera.Position, dir.Normalized());
    }

    /// <summary>Finds the nearest surface the ray hits, or null.</summary>
    public static PickResult? Pick(Level level, Ray ray)
    {
        PickResult? best = null;
        foreach (var sector in level.Sectors)
        {
            foreach (var surface in sector.Surfaces)
            {
                if (surface.Corners.Count < 3) continue;
                var a = surface.Corners[0].Vertex.Position;
                for (int i = 1; i + 1 < surface.Corners.Count; i++)
                {
                    var b = surface.Corners[i].Vertex.Position;
                    var c = surface.Corners[i + 1].Vertex.Position;
                    if (RayTriangle(ray, a, b, c, out double t) && t > Epsilon &&
                        (best is null || t < best.Distance))
                    {
                        best = new PickResult(sector, surface, t, ray.Origin + ray.Direction * t);
                    }
                }
            }
        }
        return best;
    }

    /// <summary>Finds the nearest thing whose marker sphere (radius) the ray hits, or null.</summary>
    public static ThingHit? PickThing(Level level, Ray ray, double radius)
    {
        ThingHit? best = null;
        foreach (var thing in level.Things)
        {
            if (RaySphere(ray, thing.Position, radius, out double t) &&
                (best is null || t < best.Distance))
            {
                best = new ThingHit(thing, t);
            }
        }
        return best;
    }

    /// <summary>Ray/sphere intersection; returns the nearest positive hit distance.</summary>
    public static bool RaySphere(Ray ray, Vec3 center, double radius, out double t)
    {
        t = 0;
        var oc = ray.Origin - center;
        double b = oc.Dot(ray.Direction);
        double c = oc.Dot(oc) - radius * radius;
        double disc = b * b - c;
        if (disc < 0) return false;

        double sqrt = System.Math.Sqrt(disc);
        double t0 = -b - sqrt;
        double t1 = -b + sqrt;
        t = t0 > Epsilon ? t0 : t1;
        return t > Epsilon;
    }

    /// <summary>Möller–Trumbore ray/triangle intersection (double-sided).</summary>
    public static bool RayTriangle(Ray ray, Vec3 a, Vec3 b, Vec3 c, out double t)
    {
        t = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = ray.Direction.Cross(e2);
        double det = e1.Dot(p);
        if (System.Math.Abs(det) < Epsilon) return false; // parallel

        double inv = 1.0 / det;
        var tv = ray.Origin - a;
        double u = tv.Dot(p) * inv;
        if (u < 0 || u > 1) return false;

        var q = tv.Cross(e1);
        double v = ray.Direction.Dot(q) * inv;
        if (v < 0 || u + v > 1) return false;

        t = e2.Dot(q) * inv;
        return t > Epsilon;
    }
}
