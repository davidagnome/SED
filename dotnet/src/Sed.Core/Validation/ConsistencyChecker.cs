using Sed.Core.Model;

namespace Sed.Core.Validation;

public enum IssueSeverity { Error, Warning }

public enum ItemType { Sector, Surface, Thing }

public sealed record ConsistencyIssue(ItemType Type, IssueSeverity Severity, int SectorIndex, int SurfaceIndex, string Message);

public static class ConsistencyChecker
{
    /// <summary>Validates the level and returns a list of issues found.</summary>
    public static List<ConsistencyIssue> Check(Level level)
    {
        var issues = new List<ConsistencyIssue>();
        int maxCorners = level.Kind == ProjectType.InfernalMachine ? 64 : 24;

        for (int si = 0; si < level.Sectors.Count; si++)
        {
            var sector = level.Sectors[si];

            if (sector.Surfaces.Count < 4)
                issues.Add(new ConsistencyIssue(ItemType.Sector, IssueSeverity.Error, si, -1, "Sector has fewer than 4 surfaces"));

            if (sector.Vertices.Count < 4)
                issues.Add(new ConsistencyIssue(ItemType.Sector, IssueSeverity.Error, si, -1, "Sector has fewer than 4 vertices"));

            for (int sfi = 0; sfi < sector.Surfaces.Count; sfi++)
            {
                var surf = sector.Surfaces[sfi];
                CheckSurface(issues, surf, si, sfi, maxCorners);
            }
        }

        for (int ti = 0; ti < level.Things.Count; ti++)
        {
            var thing = level.Things[ti];
            if (thing.Sector is null)
                issues.Add(new ConsistencyIssue(ItemType.Thing, IssueSeverity.Warning, -1, -1, "Thing is not in a sector"));
        }

        return issues;
    }

    private static void CheckSurface(List<ConsistencyIssue> issues, Surface surf, int si, int sfi, int maxCorners)
    {
        if (surf.Corners.Count < 3)
        {
            issues.Add(new ConsistencyIssue(ItemType.Surface, IssueSeverity.Error, si, sfi, "Surface has fewer than 3 vertices"));
            return;
        }

        // Normal validity: recalc and check unit length.
        surf.RecalcNormal();
        if (System.Math.Abs(surf.Normal.LengthSquared - 1.0) > 0.01)
            issues.Add(new ConsistencyIssue(ItemType.Surface, IssueSeverity.Warning, si, sfi, "Surface normal is invalid"));

        // Planarity: every corner must lie on the plane defined by first corner and normal.
        var p0 = surf.Corners[0].Vertex.Position;
        var n = surf.Normal;
        for (int ci = 0; ci < surf.Corners.Count; ci++)
        {
            double dist = n.Dot(surf.Corners[ci].Vertex.Position - p0);
            if (System.Math.Abs(dist) > 0.001)
            {
                issues.Add(new ConsistencyIssue(ItemType.Surface, IssueSeverity.Warning, si, sfi, "Surface is not planar"));
                break;
            }
        }

        // Adjoin validity: mirror pair.
        if (surf.Adjoin is not null && surf.Adjoin.Adjoin != surf)
            issues.Add(new ConsistencyIssue(ItemType.Surface, IssueSeverity.Error, si, sfi, "Invalid reverse adjoin"));

        // Max vertex count.
        if (surf.Corners.Count > maxCorners)
            issues.Add(new ConsistencyIssue(ItemType.Surface, IssueSeverity.Warning, si, sfi, "Surface exceeds max vertex count"));
    }
}
