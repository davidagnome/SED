using System.Globalization;
using System.Text;
using Sed.Core.Model;

namespace Sed.Formats.Jkl;

/// <summary>
/// Writes an edited <see cref="JklDocument"/> back to disk. The GEORESOURCE,
/// SECTORS, THINGS, LIGHTS, COGS, TEMPLATES, HEADER, and LAYERS sections are
/// regenerated from the model; any unrecognized section is kept verbatim.
/// </summary>
public static class JklWriter
{
    public static void Save(JklDocument doc, string path) =>
        File.WriteAllText(path, Build(doc));

    public static string Build(JklDocument doc)
    {
        var src = doc.SourceLines;
        var level = doc.Level;
        var (geo, sectors) = GeoResourceWriter.Build(level);

        var ranges = new List<(int start, int end, List<string> replacement)>();
        var appended = new List<List<string>>();

        AddSection(ranges, appended, src, "HEADER", GenerateHeader(level), true);
        AddSection(ranges, appended, src, "GEORESOURCE", geo, true);
        AddSection(ranges, appended, src, "SECTORS", sectors, true);
        AddSection(ranges, appended, src, "TEMPLATES", GenerateTemplates(level), level.Templates.Count > 0);
        AddSection(ranges, appended, src, "THINGS", GenerateThings(level), level.Things.Count > 0);
        AddSection(ranges, appended, src, "LIGHTS", GenerateLights(level), level.Lights.Count > 0);
        AddSection(ranges, appended, src, "COGS", GenerateCogs(level), level.Cogs.Count > 0);
        AddSection(ranges, appended, src, "LAYERS", GenerateLayers(level), level.Layers.Count > 0);
        ranges.Sort((a, b) => a.start.CompareTo(b.start));

        var result = new List<string>(src.Length + 64);
        int i = 0, r = 0;
        while (i < src.Length)
        {
            if (r < ranges.Count && i == ranges[r].start)
            {
                result.AddRange(ranges[r].replacement);
                i = ranges[r].end + 1;
                r++;
                continue;
            }
            result.Add(src[i]);
            i++;
        }

        // Sections the source never had. LIGHTS and LAYERS are editor-authored and
        // absent from every retail level, so without this a light placed in the
        // editor would vanish on save.
        foreach (var section in appended)
        {
            result.Add(string.Empty);
            result.AddRange(section);
        }

        return string.Join('\n', result);
    }

    /// <summary>
    /// Registers a section for rewriting. When the source already has the section
    /// its lines are replaced in place; when it does not and
    /// <paramref name="hasContent"/> is true, the generated section is appended at
    /// the end of the file instead of being dropped. Sections with no content are
    /// skipped entirely so untouched levels do not sprout empty ones.
    /// </summary>
    private static void AddSection(List<(int, int, List<string>)> ranges, List<List<string>> appended,
        string[] lines, string name, List<string> replacement, bool hasContent)
    {
        var (start, end) = FindSection(lines, name);
        if (start >= 0) ranges.Add((start, end, replacement));
        else if (hasContent) appended.Add(replacement);
    }

    /// <summary>
    /// Finds a "SECTION: name" line and the line that closes it: the section's own
    /// "END" (inclusive) or the line before the next "SECTION:" (exclusive) — JK
    /// sections don't all use an explicit END.
    /// </summary>
    private static (int start, int end) FindSection(string[] lines, string name)
    {
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (start < 0)
            {
                if (IsSectionHeader(t, out var hdr) && hdr.Equals(name, StringComparison.OrdinalIgnoreCase))
                    start = i;
            }
            else if (t.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                return (start, i);            // explicit END (inclusive)
            }
            else if (IsSectionHeader(t, out _))
            {
                return (start, i - 1);        // next section begins (exclusive)
            }
        }
        return (start, start >= 0 ? lines.Length - 1 : -1);
    }

    private static bool IsSectionHeader(string trimmed, out string name)
    {
        name = string.Empty;
        if (!trimmed.StartsWith("SECTION:", StringComparison.OrdinalIgnoreCase)) return false;
        name = trimmed[8..].Trim();
        return true;
    }

    private static List<string> GenerateThings(Level level)
    {
        var lines = new List<string> { "SECTION: THINGS", "", $"World things {level.Things.Count}" };
        for (int i = 0; i < level.Things.Count; i++)
        {
            var t = level.Things[i];
            var template = t.Template.Length > 0 ? t.Template : "none";
            var name = t.Name.Length > 0 ? t.Name : "thing";
            int sector = t.Sector?.Num ?? 0;
            var line = $"{i}: {template} {name} {F(t.Position.X)} {F(t.Position.Y)} {F(t.Position.Z)} " +
                       $"{F(t.Pitch)} {F(t.Yaw)} {F(t.Roll)} {sector}";
            foreach (var (k, v) in t.Values) line += $" {k}={v}";
            lines.Add(line);
        }
        lines.Add("end");
        lines.Add("");
        return lines;
    }

    private static List<string> GenerateHeader(Level level)
    {
        var h = level.Header;
        var ijim = level.Kind == ProjectType.InfernalMachine;
        var lines = new List<string> { "SECTION: HEADER" };
        lines.Add($"VERSION {h.Version}");
        lines.Add($"WORLD GRAVITY {F8(h.Gravity)}");
        lines.Add($"CEILING SKY Z {F8(h.CeilingSky.Height)}");
        lines.Add($"HORIZON DISTANCE {F8(h.HorizonSky.Distance)}");
        lines.Add($"HORIZON PIXELS PER REV {F6(h.HorizonSky.PixelsPerRev)}");
        lines.Add($"HORIZON SKY OFFSET {F8(h.HorizonSky.Offset.X)} {F8(h.HorizonSky.Offset.Y)}");
        lines.Add($"CEILING SKY OFFSET {F8(h.CeilingSky.Offset.X)} {F8(h.CeilingSky.Offset.Y)}");
        if (!ijim)
            lines.Add($"MIPMAP DISTANCES {F6(h.MipmapDistances[0])} {F6(h.MipmapDistances[1])} {F6(h.MipmapDistances[2])} {F6(h.MipmapDistances[3])}");
        lines.Add($"LOD DISTANCES {F6(h.LodDistances[0])} {F6(h.LodDistances[1])} {F6(h.LodDistances[2])} {F6(h.LodDistances[3])}");
        if (ijim)
            lines.Add($"FOG {(h.Fog.Enabled ? 1 : 0)} {F8(h.Fog.Color.R)} {F8(h.Fog.Color.G)} {F8(h.Fog.Color.B)} {F8(1.0)} {F8(h.Fog.Start)} {F8(h.Fog.End)}");
        if (!ijim)
            lines.Add($"PERSPECTIVE DISTANCE {F6(h.PerspectiveDistance)}");
        if (!ijim)
            lines.Add($"GOURAUD DISTANCE {F6(h.GouraudDistance)}");
        lines.Add("END");
        lines.Add("");
        return lines;
    }

    private static List<string> GenerateTemplates(Level level)
    {
        var lines = new List<string> { "SECTION: TEMPLATES", "", $"World templates {level.Templates.Count}" };
        foreach (var tpl in level.Templates.Values.OrderBy(t => t.Order))
        {
            // The parent occupies a fixed second token, so an empty one must be
            // written as "none" (the same sentinel THINGS uses). Emitting nothing
            // would shift the first parameter into the parent slot and lose it.
            var parent = string.IsNullOrWhiteSpace(tpl.Parent) ? "none" : tpl.Parent;

            var sb = new StringBuilder();
            sb.Append(tpl.Name).Append(' ').Append(parent);
            foreach (var (k, v) in tpl.Values)
                sb.Append(' ').Append(k).Append('=').Append(v);
            lines.Add(sb.ToString());
        }
        lines.Add("end");
        lines.Add("");
        return lines;
    }

    private static List<string> GenerateLights(Level level)
    {
        var motsOrIjim = level.Kind is ProjectType.MysteriesOfTheSith or ProjectType.InfernalMachine;
        var lines = new List<string> { "SECTION: LIGHTS", "", $"Editor lights {level.Lights.Count}" };
        for (int i = 0; i < level.Lights.Count; i++)
        {
            var lt = level.Lights[i];
            var line = $"{i}: 0x{lt.Flags:x} {lt.Layer} {F8(lt.Position.X)} {F8(lt.Position.Y)} {F8(lt.Position.Z)} {F8(lt.Range)} {F8(lt.Intensity)}";
            if (motsOrIjim)
                line += $" {F8(lt.Color.R)} {F8(lt.Color.G)} {F8(lt.Color.B)}";
            lines.Add(line);
        }
        lines.Add("end");
        lines.Add("");
        return lines;
    }

    private static List<string> GenerateCogs(Level level)
    {
        var lines = new List<string> { "SECTION: COGS", "", $"World cogs\t{level.Cogs.Count}" };
        for (int i = 0; i < level.Cogs.Count; i++)
        {
            var cog = level.Cogs[i];
            var sb = new StringBuilder();
            sb.Append(i).Append(":\t").Append(cog.Name);
            foreach (var v in cog.Values) sb.Append('\t').Append(v);
            lines.Add(sb.ToString());
        }
        lines.Add("end");
        lines.Add("");
        return lines;
    }

    private static List<string> GenerateLayers(Level level)
    {
        var lines = new List<string> { "SECTION: LAYERS", "", $"Editor layers {level.Layers.Count}" };

        for (int li = 0; li < level.Layers.Count; li++)
        {
            lines.Add($"#{level.Layers[li]}");

            var secs = new List<int>();
            for (int si = 0; si < level.Sectors.Count; si++)
                if (level.Sectors[si].Layer == li) secs.Add(si);

            var sbSec = new StringBuilder().Append(secs.Count).Append(':');
            foreach (var s in secs) sbSec.Append('\t').Append(s);
            lines.Add(sbSec.ToString());

            var ths = new List<int>();
            for (int ti = 0; ti < level.Things.Count; ti++)
                if (level.Things[ti].Layer == li) ths.Add(ti);

            var sbTh = new StringBuilder().Append(ths.Count).Append(':');
            foreach (var t in ths) sbTh.Append('\t').Append(t);
            lines.Add(sbTh.ToString());
        }

        lines.Add("end");
        lines.Add("");
        return lines;
    }

    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
    private static string F6(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
    private static string F8(double v) => v.ToString("0.00000000", CultureInfo.InvariantCulture);
}
