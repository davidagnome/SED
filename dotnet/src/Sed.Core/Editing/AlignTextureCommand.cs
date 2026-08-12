using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Aligns a surface's UVs to a neighbour it shares an edge with, so the texture
/// runs continuously across the seam (the original's align-from-adjoin).
///
/// The shared edge is the common axis. The reference surface's UV mapping is
/// decomposed into a gradient *along* that edge and one *perpendicular* to it;
/// the target is then given the same two gradients, anchored at the shared edge.
/// The result matches exactly along the seam and continues at the same texture
/// scale onto the target — which is what "continuous" means for two faces meeting
/// at an angle, since they are not coplanar and cannot share one flat projection.
/// </summary>
public sealed class AlignTextureToNeighbourCommand : IEditCommand
{
    private readonly Surface _target;
    private readonly Surface _reference;
    private readonly List<TexVertex> _old = new();

    public AlignTextureToNeighbourCommand(Surface target, Surface reference)
    {
        _target = target;
        _reference = reference;
    }

    public string Name => "Align texture to neighbour";

    /// <summary>Null when the pair can be aligned, else why not.</summary>
    public static string? Validate(Surface target, Surface reference)
    {
        if (ReferenceEquals(target, reference)) return "Pick two different surfaces.";
        if (target.Corners.Count < 3 || reference.Corners.Count < 3)
            return "Both surfaces need at least three vertices.";
        if (FindSharedEdge(target, reference) is null)
            return "The surfaces do not share an edge.";
        return null;
    }

    public void Apply()
    {
        _old.Clear();
        foreach (var c in _target.Corners) _old.Add(c.Uv);

        if (FindSharedEdge(_target, _reference) is not { } edge) return;
        var (a, b) = edge;

        _reference.RecalcNormal();
        _target.RecalcNormal();

        var along = b - a;
        double length = along.Length;
        if (length < 1e-9) return;
        along *= 1.0 / length;

        // UV gradient along the shared edge, in UV units per world unit.
        var uvA = UvAt(_reference, a);
        var uvB = UvAt(_reference, b);
        var gradAlong = new TexVertex((uvB.U - uvA.U) / length, (uvB.V - uvA.V) / length);

        // UV gradient perpendicular to it, taken from a reference corner off the edge.
        var perpRef = InwardPerpendicular(_reference, along, a);
        if (!TryPerpendicularGradient(_reference, a, along, perpRef, uvA, gradAlong, out var gradPerp))
            return;

        var perpTarget = InwardPerpendicular(_target, along, a);

        foreach (var corner in _target.Corners)
        {
            var d = corner.Vertex.Position - a;
            double u = d.Dot(along);
            double v = d.Dot(perpTarget);

            corner.Uv = new TexVertex(
                uvA.U + gradAlong.U * u + gradPerp.U * v,
                uvA.V + gradAlong.V * u + gradPerp.V * v);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _target.Corners.Count && i < _old.Count; i++)
            _target.Corners[i].Uv = _old[i];
    }

    /// <summary>
    /// The two world positions of an edge both surfaces contain. Matched by
    /// position rather than vertex identity: adjoined sectors keep their own
    /// vertex objects at the same coordinates.
    /// </summary>
    private static (Vec3 a, Vec3 b)? FindSharedEdge(Surface x, Surface y)
    {
        const double tol = 1e-6;

        for (int i = 0; i < x.Corners.Count; i++)
        {
            var xa = x.Corners[i].Vertex.Position;
            var xb = x.Corners[(i + 1) % x.Corners.Count].Vertex.Position;

            for (int j = 0; j < y.Corners.Count; j++)
            {
                var ya = y.Corners[j].Vertex.Position;
                var yb = y.Corners[(j + 1) % y.Corners.Count].Vertex.Position;

                bool same = (Near(xa, ya, tol) && Near(xb, yb, tol))
                            || (Near(xa, yb, tol) && Near(xb, ya, tol));
                if (same) return (xa, xb);
            }
        }
        return null;
    }

    private static bool Near(Vec3 p, Vec3 q, double tol) => (p - q).LengthSquared < tol * tol;

    /// <summary>
    /// A unit vector in the surface's plane, perpendicular to the edge and
    /// pointing into the surface (so its corners have positive coordinates).
    /// </summary>
    private static Vec3 InwardPerpendicular(Surface surf, Vec3 along, Vec3 anchor)
    {
        var perp = surf.Normal.Cross(along);
        if (perp.LengthSquared < 1e-18) return perp;
        perp = perp.Normalized();

        double sum = 0;
        foreach (var c in surf.Corners) sum += (c.Vertex.Position - anchor).Dot(perp);
        return sum < 0 ? perp * -1.0 : perp;
    }

    /// <summary>
    /// Recovers the reference's UV change per world unit away from the edge, by
    /// looking at the corner furthest from it (the most numerically stable one).
    /// </summary>
    private static bool TryPerpendicularGradient(Surface reference, Vec3 anchor, Vec3 along,
        Vec3 perp, TexVertex uvAnchor, TexVertex gradAlong, out TexVertex gradPerp)
    {
        gradPerp = new TexVertex(0, 0);
        if (perp.LengthSquared < 1e-18) return false;

        Surface.Corner? best = null;
        double bestDistance = 0;

        foreach (var c in reference.Corners)
        {
            double distance = (c.Vertex.Position - anchor).Dot(perp);
            if (distance > bestDistance) { bestDistance = distance; best = c; }
        }

        if (best is null || bestDistance < 1e-9) return false;

        var d = best.Vertex.Position - anchor;
        double u = d.Dot(along);

        // Subtract the along-edge contribution; what remains is the perpendicular one.
        gradPerp = new TexVertex(
            (best.Uv.U - uvAnchor.U - gradAlong.U * u) / bestDistance,
            (best.Uv.V - uvAnchor.V - gradAlong.V * u) / bestDistance);
        return true;
    }

    private static TexVertex UvAt(Surface surf, Vec3 position)
    {
        const double tol = 1e-6;
        foreach (var c in surf.Corners)
            if (Near(c.Vertex.Position, position, tol)) return c.Uv;
        return new TexVertex(0, 0);
    }
}
