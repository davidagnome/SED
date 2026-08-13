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
/// <see cref="Fields"/> holds per-field criteria (the original's per-field query
/// builder); all active criteria must match.
/// </summary>
public sealed class FindQuery
{
    public FindKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;

    /// <summary>Bits that must be set; 0 disables the filter.</summary>
    public long FlagMask { get; init; }

    /// <summary>Per-field criteria, all ANDed. Empty when unused.</summary>
    public IReadOnlyList<FieldCriterion> Fields { get; init; } = Array.Empty<FieldCriterion>();

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

        bool FieldsMatch(FindResult result) =>
            query.Fields.All(c => FieldMatches(result, c));

        switch (query.Kind)
        {
            case FindKind.Sector:
                for (int i = 0; i < level.Sectors.Count && results.Count < query.Limit; i++)
                {
                    var s = level.Sectors[i];
                    if (!FlagsMatch(s.Flags)) continue;
                    if (!TextMatches(s.Num, s.ColorMap, s.Sound)) continue;

                    var hit = new FindResult(FindKind.Sector, s.Num,
                        $"Sector {s.Num} — {s.Surfaces.Count} surfaces, {s.Vertices.Count} vertices" +
                        (s.Flags != 0 ? $", flags 0x{s.Flags:X}" : string.Empty),
                        Centroid(s), Sector: s);
                    if (!FieldsMatch(hit)) continue;
                    results.Add(hit);
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
                        var hit = new FindResult(FindKind.Surface, surf.Num,
                            $"Sector {sector.Num} · surface {surf.Num} — {material}" +
                            (surf.Adjoin is not null ? " (adjoin)" : string.Empty),
                            Centroid(surf), Sector: sector, Surface: surf);
                        if (!FieldsMatch(hit)) continue;
                        results.Add(hit);
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
                    var hit = new FindResult(FindKind.Thing, t.Num,
                        $"Thing {t.Num} '{name}' — {template}" +
                        (t.Sector is { } sec ? $", sector {sec.Num}" : ", no sector"),
                        t.Position, Sector: t.Sector, Thing: t);
                    if (!FieldsMatch(hit)) continue;
                    results.Add(hit);
                }
                break;

            case FindKind.Light:
                for (int i = 0; i < level.Lights.Count && results.Count < query.Limit; i++)
                {
                    var l = level.Lights[i];
                    if (!FlagsMatch(l.Flags)) continue;
                    if (!TextMatches(l.Num)) continue;

                    var hit = new FindResult(FindKind.Light, l.Num,
                        $"Light {l.Num} — range {l.Range:0.##}, intensity {l.Intensity:0.##}",
                        l.Position, Light: l);
                    if (!FieldsMatch(hit)) continue;
                    results.Add(hit);
                }
                break;
        }

        return results;
    }

    /// <summary>Tests one per-field criterion against a hit (port of QTestInt/Str/Flags/Double/Color).</summary>
    private static bool FieldMatches(FindResult hit, FieldCriterion c)
    {
        if (c.Op == CompareOp.None) return true;

        switch (c.Field)
        {
            case FindField.Num:
                return TestInt(hit.Index, c);
            case FindField.Layer:
                return TestLayer(hit, c);

            case FindField.Sector:
                if (hit.Sector is not { } sec) return c.Op == CompareOp.NotEqual;
                return TestInt(sec.Num, c);

            case FindField.ColorMap:
                return hit.Sector is { } sc && TestString(sc.ColorMap, c);
            case FindField.Sound:
                return hit.Sector is { } ss && TestString(ss.Sound, c);
            case FindField.SoundVolume:
                return hit.Sector is { } sv && TestDouble(sv.SoundVolume, c);
            case FindField.ExtraLight:
                if (hit.Sector is { } se) return TestColor(se.ExtraLight, c);
                if (hit.Surface is { } sf) return TestDouble(sf.ExtraLightIntensity, c);
                if (hit.Light is { } sl) return TestColor(sl.Color, c);
                return false;
            case FindField.Tint:
                return hit.Sector is { } st && TestColor(st.Tint, c);
            case FindField.Flags:
                if (hit.Sector is { } sf2) return TestInt(sf2.Flags, c);
                if (hit.Thing is { } th) return TestInt((long)th.Flags, c);
                if (hit.Light is { } sl2) return TestInt(sl2.Flags, c);
                return false;
            case FindField.NSurfs:
                return hit.Sector is { } sn && TestInt(sn.Surfaces.Count, c);

            case FindField.Material:
                return hit.Surface is { } sm && TestString(sm.Material, c);
            case FindField.AdjoinSector:
                return hit.Surface is { } sa && TestInt(sa.Adjoin?.Sector.Num ?? -1, c);
            case FindField.AdjoinSurface:
                return hit.Surface is { } san && TestInt(san.Adjoin?.Num ?? -1, c);
            case FindField.AdjoinFlags:
                return hit.Surface is { } saf && TestInt(saf.AdjoinFlags, c);
            case FindField.SurfFlags:
                return hit.Surface is { } ss2 && TestInt(ss2.SurfFlags, c);
            case FindField.FaceFlags:
                return hit.Surface is { } sf3 && TestInt(sf3.FaceFlags, c);
            case FindField.Geo:
                return hit.Surface is { } sg && TestInt(sg.Geo, c);
            case FindField.Light:
                return hit.Surface is { } sl3 && TestInt(sl3.Light, c);
            case FindField.Tex:
                return hit.Surface is { } st2 && TestInt(st2.Tex, c);

            case FindField.Name:
                return hit.Thing is { } tn && TestString(tn.Name, c);
            case FindField.Template:
                return hit.Thing is { } tt && TestString(tt.Template, c);
            case FindField.Pitch:
                return hit.Thing is { } tp && TestDouble(tp.Pitch, c);
            case FindField.Yaw:
                return hit.Thing is { } ty && TestDouble(ty.Yaw, c);
            case FindField.Roll:
                return hit.Thing is { } tr && TestDouble(tr.Roll, c);
            case FindField.X:
                return hit.Thing is { } tx && TestDouble(tx.Position.X, c);
            case FindField.Y:
                return hit.Thing is { } ty2 && TestDouble(ty2.Position.Y, c);
            case FindField.Z:
                return hit.Thing is { } tz && TestDouble(tz.Position.Z, c);

            case FindField.Range:
                return hit.Light is { } lr && TestDouble(lr.Range, c);
            case FindField.Intensity:
                return hit.Light is { } li && TestDouble(li.Intensity, c);
            case FindField.Color:
                return hit.Light is { } lc && TestColor(lc.Color, c);
        }
        return true;
    }

    private static bool TestLayer(FindResult hit, FieldCriterion c)
    {
        // The original's Layer fields compare the layer NAME. Lights carry no
        // level reference, so their names fall back to the synthetic form.
        string? layer = hit.Sector is { } s
            ? FieldCriteria.LayerName(s.Level, s.Layer)
            : hit.Thing is { } t && t.Level is { } tl
                ? FieldCriteria.LayerName(tl, t.Layer)
                : hit.Light is { } l
                    ? FieldCriteria.LayerName(new Level(), l.Layer)
                    : null;
        return layer is not null && TestString(layer, c);
    }

    // ---- ports of TestInt / TestStr / TestFlags / TestDouble / TestColor (Q_UTILS.PAS) ----

    private static bool TestInt(long actual, FieldCriterion c) => c.Op switch
    {
        CompareOp.Equal => actual == c.Long,
        CompareOp.NotEqual => actual != c.Long,
        CompareOp.Above => actual > c.Long,
        CompareOp.Below => actual < c.Long,
        CompareOp.Contains => (actual & c.Long) != 0,
        CompareOp.NotContains => (actual & c.Long) == 0,
        _ => true,
    };

    private static bool TestDouble(double actual, FieldCriterion c) => c.Op switch
    {
        CompareOp.Equal => actual == c.Number,
        CompareOp.NotEqual => actual != c.Number,
        CompareOp.Above => actual > c.Number,
        CompareOp.Below => actual < c.Number,
        _ => true,
    };

    private static bool TestString(string actual, FieldCriterion c)
    {
        if (c.Op == CompareOp.Equal) return actual.Equals(c.Text, StringComparison.OrdinalIgnoreCase);
        if (c.Op == CompareOp.NotEqual) return !actual.Equals(c.Text, StringComparison.OrdinalIgnoreCase);
        if (c.Op == CompareOp.Contains) return actual.Contains(c.Text, StringComparison.OrdinalIgnoreCase);
        if (c.Op == CompareOp.NotContains) return !actual.Contains(c.Text, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool TestColor(Sed.Core.Math.ColorF actual, FieldCriterion c)
    {
        // Componentwise comparison against the criterion's color value.
        bool C(double a, double b) => c.Op switch
        {
            CompareOp.Equal => a == b,
            CompareOp.NotEqual => a != b,
            CompareOp.Above => a > b,
            CompareOp.Below => a < b,
            _ => true,
        };
        return C(actual.R, c.Color.R) && C(actual.G, c.Color.G) && C(actual.B, c.Color.B);
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
