using System.Globalization;
using System.Text;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Formats.Jkl;

/// <summary>
/// Writes an edited <see cref="JklDocument"/> back to disk by patching only the
/// changed lines (moved world vertices and things) in the original source,
/// leaving every other section byte-for-byte intact.
/// </summary>
public static class JklWriter
{
    public static void Save(JklDocument doc, string path) =>
        File.WriteAllText(path, Build(doc));

    public static string Build(JklDocument doc)
    {
        var lines = (string[])doc.SourceLines.Clone();
        var level = doc.Level;

        // World vertices: rewrite only the lines whose vertex actually moved.
        // (JK shares world vertices across sectors, so unmoved copies must not
        // overwrite a moved one — compare against the originally-parsed position.)
        foreach (var sector in level.Sectors)
            foreach (var v in sector.Vertices)
            {
                if (v.SourceIndex < 0) continue;
                if (!doc.VertexOriginal.TryGetValue(v.SourceIndex, out var orig)) continue;
                if (v.Position.DistanceSquaredTo(orig) < 1e-12) continue; // unmoved
                if (doc.VertexLine.TryGetValue(v.SourceIndex, out int ln) && ln < lines.Length)
                    lines[ln] = PatchVertexLine(lines[ln], v.Position);
            }

        // Things: rewrite position + orientation, preserving template/name/sector/params.
        foreach (var thing in level.Things)
            if (doc.ThingLine.TryGetValue(thing.Num, out int ln) && ln < lines.Length)
                lines[ln] = PatchThingLine(lines[ln], thing);

        return string.Join('\n', lines);
    }

    private static string PatchVertexLine(string original, Vec3 pos)
    {
        // "  12:\t x y z [# comment]"  ->  keep the "12:" prefix, replace coords.
        int colon = original.IndexOf(':');
        string prefix = colon >= 0 ? original[..(colon + 1)] : "0:";
        return $"{prefix}\t{F(pos.X)} {F(pos.Y)} {F(pos.Z)}";
    }

    private static string PatchThingLine(string original, Thing thing)
    {
        int hash = original.IndexOf('#');
        var body = hash >= 0 ? original[..hash] : original;
        var t = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 10) return original; // not a thing line we can patch

        var sb = new StringBuilder();
        sb.Append(t[0]).Append(' ')          // index "i:"
          .Append(t[1]).Append(' ')          // template
          .Append(t[2]).Append(' ')          // name
          .Append(F(thing.Position.X)).Append(' ')
          .Append(F(thing.Position.Y)).Append(' ')
          .Append(F(thing.Position.Z)).Append(' ')
          .Append(F(thing.Pitch)).Append(' ')
          .Append(F(thing.Yaw)).Append(' ')
          .Append(F(thing.Roll)).Append(' ')
          .Append(t[9]);                       // sector
        for (int i = 10; i < t.Length; i++)   // remaining params verbatim
            sb.Append(' ').Append(t[i]);
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
