using System.Globalization;
using Sed.Core.Model;

namespace Sed.Formats.Jkl;

/// <summary>
/// Writes an edited <see cref="JklDocument"/> back to disk. The GEORESOURCE,
/// SECTORS, and THINGS sections are regenerated from the model (so geometry and
/// thing topology edits persist); every other section is kept verbatim.
/// </summary>
public static class JklWriter
{
    public static void Save(JklDocument doc, string path) =>
        File.WriteAllText(path, Build(doc));

    public static string Build(JklDocument doc)
    {
        var src = doc.SourceLines;
        var (geo, sectors) = GeoResourceWriter.Build(doc.Level);

        var ranges = new List<(int start, int end, List<string> replacement)>();
        AddSection(ranges, src, "GEORESOURCE", geo);
        AddSection(ranges, src, "SECTORS", sectors);
        AddSection(ranges, src, "THINGS", GenerateThings(doc.Level));
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
        return string.Join('\n', result);
    }

    private static void AddSection(List<(int, int, List<string>)> ranges, string[] lines, string name, List<string> replacement)
    {
        var (start, end) = FindSection(lines, name);
        if (start >= 0) ranges.Add((start, end, replacement));
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

    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
