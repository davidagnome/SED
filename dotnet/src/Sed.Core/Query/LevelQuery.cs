using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Query;

/// <summary>What kind of object a query searches for.</summary>
public enum FindKind { Sector, Surface, Thing, Light }

/// <summary>
/// One search hit: a display label, a world position to jump the camera to, and
/// whichever model object it refers to (exactly one of the four is non-null).
/// </summary>
public sealed record FindResult(
    FindKind Kind,
    int Index,
    string Label,
    Vec3 Position,
    Sector? Sector = null,
    Surface? Surface = null,
    Thing? Thing = null,
    Light? Light = null);

/// <summary>
/// Search criteria. <see cref="Text"/> matches the object's identifying strings
/// (material, name, template, colormap, sound) case-insensitively, or its index
/// when the text is a number. <see cref="FlagMask"/> of 0 means "any".
/// </summary>
public sealed class FindQuery
{
    public FindKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;

    /// <summary>Bits that must be set; 0 disables the filter.</summary>
    public long FlagMask { get; init; }

    /// <summary>Caps the result list so a broad query on a big level stays responsive.</summary>
    public int Limit { get; init; } = 2000;
}

/// <summary>
/// Finds sectors, surfaces, things and lights by identity, name and flags —
/// the `Q_SECTORS` / `Q_SURFS` / `Q_THINGS` dialogs. The original builds a
/// per-field query (material, adjoin sector, each flag word, with a comparison
/// operator per field); this covers the common cases — free text over the
/// identifying strings plus a flag mask — with the matching logic kept out of
/// the view so it can be tested.
/// </summary>
public static class LevelQuery
{
    public static List<FindResult> Run(Level level, FindQuery query)
    {
        var results = new List<FindResult>();
        string text = query.Text.Trim();
        bool numeric = int.TryParse(text, out int wantedIndex);

        bool TextMatches(int num, params string?[] fields)
        {
            if (text.Length == 0) return true;
            if (numeric && num == wantedIndex) return true;
            foreach (var f in fields)
                if (!string.IsNullOrEmpty(f) && f.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        bool FlagsMatch(long flags) => query.FlagMask == 0 || (flags & query.FlagMask) != 0;

        switch (query.Kind)
        {
            case FindKind.Sector:
                for (int i = 0; i < level.Sectors.Count && results.Count < query.Limit; i++)
                {
                    var s = level.Sectors[i];
                    if (!FlagsMatch(s.Flags)) continue;
                    if (!TextMatches(s.Num, s.ColorMap, s.Sound)) continue;

                    results.Add(new FindResult(FindKind.Sector, s.Num,
                        $"Sector {s.Num} — {s.Surfaces.Count} surfaces, {s.Vertices.Count} vertices" +
                        (s.Flags != 0 ? $", flags 0x{s.Flags:X}" : string.Empty),
                        Centroid(s), Sector: s));
                }
                break;

            case FindKind.Surface:
                foreach (var sector in level.Sectors)
                {
                    foreach (var surf in sector.Surfaces)
                    {
                        if (results.Count >= query.Limit) break;
                        // A surface carries two flag words; a mask hit on either counts.
                        if (!(FlagsMatch(surf.SurfFlags) || FlagsMatch(surf.FaceFlags))) continue;
                        if (!TextMatches(surf.Num, surf.Material)) continue;

                        var material = string.IsNullOrEmpty(surf.Material) ? "(no material)" : surf.Material;
                        results.Add(new FindResult(FindKind.Surface, surf.Num,
                            $"Sector {sector.Num} · surface {surf.Num} — {material}" +
                            (surf.Adjoin is not null ? " (adjoin)" : string.Empty),
                            Centroid(surf), Sector: sector, Surface: surf));
                    }
                    if (results.Count >= query.Limit) break;
                }
                break;

            case FindKind.Thing:
                for (int i = 0; i < level.Things.Count && results.Count < query.Limit; i++)
                {
                    var t = level.Things[i];
                    if (!FlagsMatch((long)t.Flags)) continue;
                    if (!TextMatches(t.Num, t.Name, t.Template)) continue;

                    var name = string.IsNullOrEmpty(t.Name) ? "(unnamed)" : t.Name;
                    var template = string.IsNullOrEmpty(t.Template) ? "(no template)" : t.Template;
                    results.Add(new FindResult(FindKind.Thing, t.Num,
                        $"Thing {t.Num} '{name}' — {template}" +
                        (t.Sector is { } sec ? $", sector {sec.Num}" : ", no sector"),
                        t.Position, Sector: t.Sector, Thing: t));
                }
                break;

            case FindKind.Light:
                for (int i = 0; i < level.Lights.Count && results.Count < query.Limit; i++)
                {
                    var l = level.Lights[i];
                    if (!FlagsMatch(l.Flags)) continue;
                    if (!TextMatches(l.Num)) continue;

                    results.Add(new FindResult(FindKind.Light, l.Num,
                        $"Light {l.Num} — range {l.Range:0.##}, intensity {l.Intensity:0.##}",
                        l.Position, Light: l));
                }
                break;
        }

        return results;
    }

    private static Vec3 Centroid(Sector sector)
    {
        if (sector.Vertices.Count == 0) return Vec3.Zero;
        var sum = Vec3.Zero;
        foreach (var v in sector.Vertices) sum += v.Position;
        return sum * (1.0 / sector.Vertices.Count);
    }

    private static Vec3 Centroid(Surface surface)
    {
        if (surface.Corners.Count == 0) return Vec3.Zero;
        var sum = Vec3.Zero;
        foreach (var c in surface.Corners) sum += c.Vertex.Position;
        return sum * (1.0 / surface.Corners.Count);
    }
}
