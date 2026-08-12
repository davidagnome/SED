using System.Globalization;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Formats.Asc;

/// <summary>
/// Imports a 3D Studio ASCII (.asc) file as level geometry, porting
/// <c>ASC_IMPORT.INC</c>: every <c>Tri-mesh</c> becomes one sector whose faces
/// are its triangles, sharing the mesh's vertices. The original expected the
/// mesh counts on the <c>Tri-mesh,</c> line (<c>vertices: N faces: M</c>); the
/// standard 3DS layout (bare <c>Vertex list:</c> / <c>Face list:</c> lines) is
/// also accepted, which is the form most .asc files in the wild use.
/// </summary>
public static class AscImporter
{
    private sealed class Reader
    {
        private readonly string[] _lines;
        private int _i;
        private string? _pending;

        public Reader(string text) => _lines = text.Replace("\r\n", "\n").Split('\n');

        public string? Peek()
        {
            _pending ??= NextRaw();
            return _pending;
        }

        public string? Next()
        {
            if (_pending is not null)
            {
                var p = _pending;
                _pending = null;
                return p;
            }
            return NextRaw();
        }

        private string? NextRaw()
        {
            while (_i < _lines.Length)
            {
                var s = StripComment(_lines[_i++]).Trim().ToLowerInvariant();
                if (s.Length > 0) return s;
            }
            return null;
        }
    }

    /// <summary>Imports the ASC text, returning the level (plus a default thing, as the original adds).</summary>
    public static Level Import(string text)
    {
        var level = new Level { Kind = ProjectType.JediKnight };
        var r = new Reader(text);

        while (true)
        {
            var s = r.Next();
            if (s is null) break;
            if (!s.StartsWith("tri-mesh", StringComparison.Ordinal))
                continue;

            LoadTriMesh(s);
        }

        // FinishUp: renumber + a default thing.
        level.RenumberSectors();
        level.NewThing();
        level.RenumberThings();
        return level;

        void LoadTriMesh(string firstLine)
        {
            int nvxs = -1, nsfs = -1;
            TryCounts(firstLine, ref nvxs, ref nsfs);

            var sec = level.NewSector();

            // "Vertex list:"
            var v = r.Next();
            if (v is null || !FirstWord(v).StartsWith("vertex", StringComparison.Ordinal))
            {
                level.Sectors.Remove(sec);
                return;
            }

            // Vertices.
            if (nvxs >= 0)
            {
                for (int k = 0; k < nvxs; k++)
                {
                    var tmp = r.Next();
                    if (tmp is null || !ParseVertex(tmp, sec)) break;
                }
            }
            else
            {
                while (r.Peek() is { } tmp && ParseVertex(tmp, sec))
                    r.Next();
            }

            // "Face list:"
            var f = r.Next();
            while (f is not null && !FirstWord(f).StartsWith("face", StringComparison.Ordinal))
                f = r.Next();
            if (f is null)
            {
                level.Sectors.Remove(sec);
                return;
            }
            // f is the "Face list:" line itself — faces start after it.

            if (nsfs >= 0)
            {
                for (int k = 0; k < nsfs; k++)
                {
                    if (r.Next() is { } tmp) ParseFace(tmp, sec);
                }
            }
            else
            {
                while (r.Peek() is { } tmp)
                {
                    if (FirstWord(tmp).StartsWith("tri-mesh", StringComparison.Ordinal)) break;
                    r.Next();
                    ParseFace(tmp, sec);
                }
            }

            sec.Renumber();
        }
    }

    private static void TryCounts(string line, ref int nvxs, ref int nsfs)
    {
        // "tri-mesh, "name": vertices: 8 faces: 12"
        var t = Split(line);
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (t[i] == "vertices:")
                nvxs = ParseInt(t[i + 1]);
            else if (t[i] == "faces:")
                nsfs = ParseInt(t[i + 1]);
        }
    }

    private static bool ParseVertex(string tmp, Sector sec)
    {
        // The original replaces every ':' with a space before scanning.
        var t = Split(tmp.Replace(':', ' '));
        if (t.Count < 4 || t[0] != "vertex") return false;

        // "vertex 0 x 1 y 2 z 3" — labelled form.
        double? x = null, y = null, z = null;
        for (int i = 2; i + 1 < t.Count; i++)
        {
            if (t[i] == "x") x = ParseDouble(t[i + 1]);
            else if (t[i] == "y") y = ParseDouble(t[i + 1]);
            else if (t[i] == "z") z = ParseDouble(t[i + 1]);
        }
        if (x is not null && y is not null && z is not null)
        {
            sec.AddVertex(new Vec3(x.Value, y.Value, z.Value));
            return true;
        }

        // "vertex 0 1 2 3" — plain coordinates.
        sec.AddVertex(new Vec3(ParseDouble(t[2]), ParseDouble(t[3]), ParseDouble(t[4])));
        return true;
    }

    private static bool ParseFace(string tmp, Sector sec)
    {
        var t = Split(tmp.Replace(':', ' '));
        if (t.Count < 8 || t[0] != "face") return false;

        // "face 0 a 0 b 1 c 2"
        int a = -1, b = -1, c = -1;
        for (int i = 2; i + 1 < t.Count; i++)
        {
            if (t[i] == "a") a = ParseInt(t[i + 1]);
            else if (t[i] == "b") b = ParseInt(t[i + 1]);
            else if (t[i] == "c") c = ParseInt(t[i + 1]);
        }
        if (a < 0 || b < 0 || c < 0) return false;
        if ((uint)a >= (uint)sec.Vertices.Count || (uint)b >= (uint)sec.Vertices.Count ||
            (uint)c >= (uint)sec.Vertices.Count)
            return false;

        var surf = sec.NewSurface();
        surf.Corners.Add(new Surface.Corner { Vertex = sec.Vertices[a], Intensity = ColorF.White });
        surf.Corners.Add(new Surface.Corner { Vertex = sec.Vertices[b], Intensity = ColorF.White });
        surf.Corners.Add(new Surface.Corner { Vertex = sec.Vertices[c], Intensity = ColorF.White });
        surf.RecalcNormal();
        return true;
    }

    private static string StripComment(string s)
    {
        int p = s.IndexOf('#');
        return p >= 0 ? s[..p] : s;
    }

    private static string FirstWord(string s)
    {
        var t = Split(s);
        return t.Count == 0 ? string.Empty : t[0];
    }

    private static List<string> Split(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static int ParseInt(string s) =>
        int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ParseDouble(string s) =>
        double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
