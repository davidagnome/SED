using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Connects two back-to-back surfaces in different sectors into a portal
/// (`ConnectSurfaces` in `LEV_UTILS.PAS`).
///
/// A plain adjoin only works when the two faces are already the same shape. This
/// trims them to their shared region first: each surface is cleaved by every edge
/// plane of the other — the plane through that edge whose normal is
/// <c>edge × normal</c>, which points out of the polygon, so the cleave keeps the
/// inside — and the trimmed results are then adjoined. Offcuts remain in their
/// sectors as separate surfaces, exactly as the original leaves them.
///
/// Built on the first <see cref="Apply"/> because each cleave plane depends on
/// the geometry the previous cleave produced; redo replays the recorded steps.
/// </summary>
public sealed class BridgeSurfacesCommand : IEditCommand
{
    private const double CoplanarDot = -0.9999;
    private const double PlaneTolerance = 1e-4;

    private readonly Surface _a;
    private readonly Surface _b;
    private List<IEditCommand>? _steps;

    public BridgeSurfacesCommand(Surface a, Surface b)
    {
        _a = a;
        _b = b;
    }

    public string Name => "Bridge surfaces";

    /// <summary>Null when the bridge succeeded; otherwise why it could not be made.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// Checks the preconditions the original enforces, without changing anything.
    /// Returns null when the pair can be bridged, else a message for the user.
    /// </summary>
    public static string? Validate(Surface a, Surface b)
    {
        if (ReferenceEquals(a, b)) return "Pick two different surfaces.";
        if (a.Corners.Count < 3 || b.Corners.Count < 3) return "Both surfaces need at least three vertices.";
        if (ReferenceEquals(a.Sector, b.Sector)) return "Both surfaces belong to the same sector.";
        if (a.Adjoin is not null || b.Adjoin is not null) return "One or both surfaces are already adjoined.";

        a.RecalcNormal();
        b.RecalcNormal();

        if (a.Normal.Dot(b.Normal) > CoplanarDot)
            return "The surfaces do not face each other (their normals are not opposed).";

        var pa = a.Corners[0].Vertex.Position;
        var pb = b.Corners[0].Vertex.Position;
        if (System.Math.Abs(a.Normal.Dot(pb - pa)) > PlaneTolerance)
            return "The surfaces are not back to back — they lie in different planes.";

        return null;
    }

    public void Apply()
    {
        if (_steps is not null)
        {
            foreach (var step in _steps) step.Apply();
            return;
        }

        _steps = new List<IEditCommand>();

        Failure = Validate(_a, _b);
        if (Failure is not null) return;

        TrimBy(_b, _a);
        TrimBy(_a, _b);

        if (!Overlaps(_a, _b))
        {
            // Undo the trimming: leaving half-cleaved surfaces behind after a
            // failed bridge would be worse than doing nothing.
            for (int i = _steps.Count - 1; i >= 0; i--) _steps[i].Revert();
            _steps.Clear();
            Failure = "The surfaces do not overlap, so there is nothing to connect.";
            return;
        }

        var adjoin = new MakeAdjoinCommand(_a, _b);
        adjoin.Apply();
        _steps.Add(adjoin);
    }

    public void Revert()
    {
        if (_steps is null) return;
        for (int i = _steps.Count - 1; i >= 0; i--) _steps[i].Revert();
    }

    /// <summary>Cleaves <paramref name="target"/> by every edge plane of <paramref name="cutter"/>.</summary>
    private void TrimBy(Surface target, Surface cutter)
    {
        cutter.RecalcNormal();
        var normal = cutter.Normal;
        int n = cutter.Corners.Count;

        for (int i = 0; i < n; i++)
        {
            var from = cutter.Corners[i].Vertex.Position;
            var to = cutter.Corners[(i + 1) % n].Vertex.Position;

            var edge = to - from;
            if (edge.LengthSquared < 1e-18) continue;

            // edge × normal points out of the polygon, so the cleave's "back"
            // side — which stays in the original surface — is the inside.
            var planeNormal = edge.Normalized().Cross(normal);
            if (planeNormal.LengthSquared < 1e-18) continue;

            var cleave = new CleaveSurfaceCommand(target, planeNormal, from);
            cleave.Apply();
            _steps!.Add(cleave);
        }
    }

    /// <summary>
    /// True when the trimmed surfaces cover the same region: each one's centroid
    /// falls inside the other. A pair that failed to trim to a common area will
    /// fail this.
    /// </summary>
    private static bool Overlaps(Surface a, Surface b)
    {
        if (a.Corners.Count < 3 || b.Corners.Count < 3) return false;
        a.RecalcNormal();
        b.RecalcNormal();

        return GeometryOps.PointOnSurface(b, GeometryOps.Centroid(a))
            && GeometryOps.PointOnSurface(a, GeometryOps.Centroid(b));
    }
}
