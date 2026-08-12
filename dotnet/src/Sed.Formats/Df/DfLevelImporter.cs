using System.Globalization;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Formats.Df;

/// <summary>
/// Options for <see cref="DfLevelImporter"/>, mirroring the original's DF import
/// dialog (scaling factor, texture handling) and the df2jk.lst logic table.
/// </summary>
public sealed record DfImportOptions
{
    /// <summary>World-scale factor (jkdffactor); the dialog defaults to 40, the file constant to 35.</summary>
    public double ScaleFactor = 35;

    /// <summary>False → every surface gets the default material; true → keep the .lev texture names.</summary>
    public bool KeepTextureNames;

    /// <summary>The default material used when texture handling is DFLT (or an index is out of range).</summary>
    public string DefaultMaterial = "DFLT.MAT";

    /// <summary>DF logic → JK template mapping (the original's data\df2jk.lst).</summary>
    public IReadOnlyDictionary<string, string>? LogicTable;
}

/// <summary>
/// Ports the original's DF import (<c>U_DFI.PAS</c> / <c>DF_IMPORT.INC</c>): reads a
/// Dark Forces <c>.lev</c> level text plus its optional <c>.O</c> object file and
/// produces a Jedi Knight <see cref="Level"/>. The conversion is faithful to the
/// original: the <c>x=x, y=z, z=-y</c> axis remap with the scale factor, cycle
/// extraction and ear-clip triangulation of concave DF sectors, the BOT/TOP/MID
/// wall splitting against neighbouring sector heights, adjoin matching on
/// reversed vertex order, per-sector ambient light from the DF wall lights, and
/// the df2jk.lst logic conversion for objects.
/// </summary>
public sealed class DfLevelImporter
{
    private sealed class DfVertex { public double X, Z; }

    private sealed class DfWall
    {
        public int V1, V2;
        public int IMid, ITop, IBot;
        public int Adjoin, Mirror;
        public long Flags1, Flags2;
        public int Light;
    }

    private sealed class DfSector
    {
        public int Ambient;
        public double FloorY, CeilingY, SecY;
        public int FloorTx, CeilingTx;
        public long Flags;
        public int Layer;
    }

    private readonly DfImportOptions _options;
    private readonly List<string> _warnings = new();

    // Parsed DF data.
    private readonly List<string> _textures = new();
    private readonly List<DfSector> _dfSectors = new();
    private readonly List<DfVertex> _vxList = new();
    private readonly List<DfWall> _wlList = new();
    private DfSector _sector = new();

    // The JK level being built, with parallel DF-index bookkeeping.
    private readonly Level _level = new();
    private readonly Dictionary<Sector, int> _sectorMarks = new();
    private readonly Dictionary<Surface, int> _surfaceMarks = new();
    private readonly Dictionary<Surface, int> _nmat = new();
    private int _nSector;

    private double _pixelPerUnit;

    private DfLevelImporter(DfImportOptions options)
    {
        _options = options;
        _pixelPerUnit = 320 * options.ScaleFactor / 40;
    }

    /// <summary>Imports a DF level from its .lev and optional .O texts.</summary>
    public static (Level Level, IReadOnlyList<string> Warnings) Import(
        string levText, string? objectText, DfImportOptions options)
    {
        var importer = new DfLevelImporter(options);
        return (importer.Run(levText, objectText), importer._warnings);
    }

    /// <summary>Imports a DF level file, reading the sibling <c>.O</c> if present.</summary>
    public static (Level Level, IReadOnlyList<string> Warnings) ImportFile(
        string levPath, DfImportOptions options)
    {
        var oPath = Path.ChangeExtension(levPath, ".O");
        var oText = File.Exists(oPath) ? File.ReadAllText(oPath) : null;
        return Import(File.ReadAllText(levPath), oText, options);
    }

    /// <summary>Loads the df2jk.lst mapping from <paramref name="baseDir"/>\data (or the dir itself).</summary>
    public static Dictionary<string, string> LoadLogicTable(string baseDir)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { Path.Combine(baseDir, "data"), baseDir })
        {
            var path = Path.Combine(dir, "df2jk.lst");
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw;
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line[..hash];
                var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2)
                    table[words[0]] = words[1];
            }
            break;
        }
        return table;
    }

    // ---- coordinate conversion ----

    private Vec3 DfToJk(double dfx, double dfy, double dfz)
    {
        double f = _options.ScaleFactor;
        return new Vec3(dfx / f, dfz / f, -dfy / f);
    }

    // ---- helpers (port of GetAngle / Isabove180 / Is0to180 / DoIntersect) ----

    private static bool IsAbove180(double x, double y, double xa, double ya, double xb, double yb)
        => (x - xb) * (y - ya) - (x - xa) * (y - yb) > 0;

    /// <summary>Angle at (x,y) between vectors (xa,ya)-(x,y) and (xb,yb)-(x,y), in radians.</summary>
    private static double GetAngle(double x, double y, double xa, double ya, double xb, double yb)
    {
        double ax = xa - x, ay = ya - y;
        double bx = xb - x, by = yb - y;
        double d = (ax * bx + ay * by) /
                   (Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by));
        double result;
        if (d > 1) result = 0;
        else if (d < -1) result = Math.PI;
        else result = Math.Acos(d);
        if (IsAbove180(x, y, xa, ya, xb, yb)) result = 2 * Math.PI - result;
        return result;
    }

    /// <summary>Angle at vertex 2 of (v1, v2, v3), in the DF XZ plane.</summary>
    private double CalcAngle(int nv1, int nv2, int nv3)
    {
        var v1 = _vxList[nv1]; var v2 = _vxList[nv2]; var v3 = _vxList[nv3];
        return GetAngle(v2.X, v2.Z, v1.X, v1.Z, v3.X, v3.Z);
    }

    /// <summary>Cross-product sign test: angle at v2 is in (0, 180).</summary>
    private bool Is0to180ex(int nv1, int nv2, int nv3)
    {
        var v1 = _vxList[nv1]; var v2 = _vxList[nv2]; var v3 = _vxList[nv3];
        double dx1 = v1.X - v2.X, dy1 = v1.Z - v2.Z;
        double dx2 = v3.X - v2.X, dy2 = v3.Z - v2.Z;
        return dx1 * dy2 - dx2 * dy1 > 0;
    }

    /// <summary>Cross-product sign test: angle at v2 is in [0, 180].</summary>
    private bool Is0to180inc(int nv1, int nv2, int nv3)
    {
        var v1 = _vxList[nv1]; var v2 = _vxList[nv2]; var v3 = _vxList[nv3];
        double dx1 = v1.X - v2.X, dy1 = v1.Z - v2.Z;
        double dx2 = v3.X - v2.X, dy2 = v3.Z - v2.Z;
        return dx1 * dy2 - dx2 * dy1 >= 0;
    }

    /// <summary>2D (XZ) segment intersection; port of DoIntersect.</summary>
    private static bool DoIntersect(double x11, double y11, double x12, double y12,
        double x21, double y21, double x22, double y22)
    {
        double dy1 = y12 - y11, dy2 = y22 - y21;
        double dx1 = x12 - x11, dx2 = x22 - x21;
        double d = dy1 * dx2 - dx1 * dy2;
        if (Math.Abs(d) < 0.0001) return false;
        double b = (dx1 * (y21 - y11) + dy1 * (x11 - x21)) / d;
        double y = y21 + dy2 * b;
        double x = x21 + dx2 * b;
        return y >= Math.Min(y11, y12) && y <= Math.Max(y11, y12) &&
               y >= Math.Min(y21, y22) && y <= Math.Max(y21, y22) &&
               x >= Math.Min(x11, x12) && x <= Math.Max(x11, x12) &&
               x >= Math.Min(x21, x22) && x <= Math.Max(x21, x22);
    }

    // ---- texture / material helpers ----

    private string GetTexture(int i)
    {
        if (i == -1) return string.Empty;
        if (!_options.KeepTextureNames) return _options.DefaultMaterial;
        if (i < 0 || i >= _textures.Count) return _options.DefaultMaterial;
        return Path.ChangeExtension(_textures[i], ".mat");
    }

    /// <summary>Sets a surface's UVs from its vertices (ArrangeTexture with un=(1,0,0), vn=(0,1,0)).</summary>
    private void ArrangeTexture(Surface surface, int orgVx)
    {
        var org = surface.Corners[orgVx];
        var refp = org.Vertex.Position;
        double refu = org.Uv.U, refv = org.Uv.V;
        foreach (var c in surface.Corners)
        {
            var p = c.Vertex.Position;
            double u = Math.Round(refu + (p.X - refp.X) * _pixelPerUnit, 3);
            double v = Math.Round(refv + (p.Y - refp.Y) * _pixelPerUnit, 3);
            c.Uv = new TexVertex(u, v);
        }
    }

    private static void AddCorner(Surface s, Vertex v) => s.Corners.Add(new Surface.Corner { Vertex = v, Intensity = ColorF.White });

    // ---- parsing (LoadTextures / LoadSectors) ----

    private void RunTextures(LineReader t, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var s = t.ReadLine();
            _textures.Add(TokenAt(s, 1));
        }
    }

    private void LoadSector(LineReader t)
    {
        // The original creates and registers the sector before reading its fields,
        // so the DF sector list gains one (unused) trailing empty entry — keep that
        // shape so the sector indices match the .lev's ADJOIN numbers exactly.
        _sector = new DfSector();
        _dfSectors.Add(_sector);

        // Key/value pairs until LAYER.
        while (!t.Eof)
        {
            var s = t.ReadLine();
            var (w1, w2) = Words(s);
            if (w1.Length == 0) break;
            if (w1 == "NAME") { }
            else if (w1 == "AMBIENT") _sector.Ambient = ParseInt(w2);
            else if (w1 == "FLOOR" && w2 == "ALTITUDE") _sector.FloorY = ParseDouble(TokenAt(s, 2));
            else if (w1 == "CEILING" && w2 == "ALTITUDE") _sector.CeilingY = ParseDouble(TokenAt(s, 2));
            else if (w1 == "SECOND" && w2 == "ALTITUDE") _sector.SecY = ParseDouble(TokenAt(s, 2));
            else if (w1 == "FLOOR" && w2 == "TEXTURE") _sector.FloorTx = ParseInt(TokenAt(s, 2));
            else if (w1 == "CEILING" && w2 == "TEXTURE") _sector.CeilingTx = ParseInt(TokenAt(s, 2));
            else if (w1 == "FLAGS") _sector.Flags = ParseHex(w2);
            else if (w1 == "LAYER") { _sector.Layer = ParseInt(w2); break; }
        }

        while (!t.Eof && FirstWord(t.Peek()) != "VERTICES") t.ReadLine();
        if (!t.Eof)
        {
            int n = ParseInt(TokenAt(t.ReadLine(), 1));
            _vxList.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = t.ReadLine();
                // "X: <x> Z: <z>"
                var v = new DfVertex { X = ParseDouble(TokenAt(s, 1)), Z = ParseDouble(TokenAt(s, 3)) };
                _vxList.Add(v);
            }
        }

        while (!t.Eof && FirstWord(t.Peek()) != "WALLS") t.ReadLine();
        if (!t.Eof)
        {
            int n = ParseInt(TokenAt(t.ReadLine(), 1));
            _wlList.Clear();
            int i = 0;
            while (!t.Eof && i < n)
            {
                var s = t.ReadLine();
                if (s.Trim().Length == 0) continue;
                i++;
                var wall = ParseWall(s);
                _wlList.Add(wall);
            }
        }

        ImportSector();
    }

    /// <summary>Parses one DF WALL line (FScanf 'WALL LEFT: %d RIGHT: %d ...').</summary>
    private DfWall ParseWall(string s)
    {
        var wall = new DfWall { Adjoin = -1, Mirror = -1, IMid = -1, ITop = -1, IBot = -1 };
        var tokens = SplitTokens(s);
        // WALL LEFT: v RIGHT: v MID: t f f TOP: t f f BOT: t f f SIGN: t f f ADJOIN: n MIRROR: n FLAGS: h1 0 h2 LIGHT: n
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case "LEFT:": wall.V1 = ParseInt(tokens[i + 1]); break;
                case "RIGHT:": wall.V2 = ParseInt(tokens[i + 1]); break;
                case "MID:": wall.IMid = ParseInt(tokens[i + 1]); break;
                case "TOP:": wall.ITop = ParseInt(tokens[i + 1]); break;
                case "BOT:": wall.IBot = ParseInt(tokens[i + 1]); break;
                case "ADJOIN:": wall.Adjoin = ParseInt(tokens[i + 1]); break;
                case "MIRROR:": wall.Mirror = ParseInt(tokens[i + 1]); break;
                case "FLAGS:":
                    wall.Flags1 = ParseHex(tokens[i + 1]);
                    // the original's format has an ignored literal "0" then the second dword
                    for (int j = i + 2; j < tokens.Count; j++)
                    {
                        if (tokens[j] == "LIGHT:") break;
                        if (tokens[j] != "0")
                        {
                            wall.Flags2 = ParseHex(tokens[j]);
                            break;
                        }
                    }
                    break;
                case "LIGHT:": wall.Light = ParseInt(tokens[i + 1]); break;
            }
        }
        return wall;
    }

    private static int ParseInt(string s) =>
        int.Parse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ParseDouble(string s) =>
        double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static long ParseHex(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return long.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string FirstWord(string s) => TokenAt(s, 0);

    private static string TokenAt(string s, int index)
    {
        var t = SplitTokens(s);
        return index < t.Count ? t[index] : string.Empty;
    }

    private static List<string> SplitTokens(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static (string, string) Words(string s)
    {
        var t = SplitTokens(s);
        return (t.Count > 0 ? t[0] : string.Empty, t.Count > 1 ? t[1] : string.Empty);
    }

    private sealed class LineReader
    {
        private readonly string[] _lines;
        private int _i;
        public LineReader(string text) => _lines = text.Replace("\r\n", "\n").Split('\n');
        public bool Eof => _i >= _lines.Length;
        public string ReadLine() => _i < _lines.Length ? _lines[_i++] : string.Empty;
        public string Peek() => _i < _lines.Length ? _lines[_i] : string.Empty;
    }

    // ---- cycle extraction + triangulation (ImportSector / ImportCycle) ----

    private void ImportSector()
    {
        int i;
        for (i = 0; i < _wlList.Count; i++) { /* marks are per-walk */ }

        var cycles = new List<List<DfWall>>();
        int ncycles = 0, nwl = 0;
        int fvx;
        var swls = _wlList;
        var marks = new bool[swls.Count];

        List<DfWall> wls = new();
        void TakeWall(int n)
        {
            wls.Add(swls[n]);
            marks[n] = true;
        }

        void TakeFirstWall(int n)
        {
            wls = new List<DfWall>();
            cycles.Add(wls);
            fvx = swls[n].V1;
            TakeWall(n);
        }

        for (i = 0; i < marks.Length; i++) marks[i] = false;
        TakeFirstWall(0);
        while (true)
        {
            int nvx = swls[nwl].V2;
            if (nvx == fvx)
            {
                ncycles++;
                nwl = -1;
                for (i = 0; i < swls.Count; i++)
                    if (!marks[i]) { nwl = i; break; }
                if (nwl == -1) break;
                TakeFirstWall(nwl);
                nvx = swls[nwl].V2;
            }
            nwl = -1;
            for (i = 0; i < swls.Count; i++)
            {
                if (marks[i]) continue;
                if (swls[i].V1 == nvx) { nwl = i; break; }
            }
            if (nwl == -1)
            {
                _warnings.Add($"Incomplete cycle in sector {_nSector}");
                break;
            }
            TakeWall(nwl);
        }
        if (ncycles == 0) return;

        // Find the outer cycle: angle sum ≈ (count-2)·π.
        int n = -1;
        for (i = 0; i < ncycles; i++)
        {
            double asum = 0;
            wls = cycles[i];
            for (int j = 0; j < wls.Count; j++)
            {
                var prev = j > 0 ? wls[j - 1] : wls[wls.Count - 1];
                asum += CalcAngle(prev.V1, wls[j].V1, wls[j].V2);
            }
            if (Math.Abs(asum - (cycles[i].Count - 2) * Math.PI) < 0.1)
            {
                n = i;
                break;
            }
        }
        if (n == -1)
        {
            _warnings.Add($"The sector is all inside out {_nSector}");
            return;
        }
        (cycles[0], cycles[n]) = (cycles[n], cycles[0]);

        // Merge sub-cycles into the main one.
        for (i = ncycles - 1; i >= 1; i--)
        {
            n = FindNonIntersectingWall(cycles, i, out int nwl0);
            if (n == -1)
            {
                _warnings.Add($"Sector {_nSector} too complex. Part ignored");
                continue;
            }
            MergeCycleAt(cycles, i, nwl0, n);
            cycles.RemoveAt(i);
        }

        ImportCycle(cycles[0]);
    }

    /// <summary>Finds a wall of the main cycle whose start vertex connects to the inner cycle without crossing.</summary>
    private int FindNonIntersectingWall(List<List<DfWall>> cycles, int cycle, out int nwl)
    {
        nwl = 0;
        var wls0 = cycles[0];
        var wls = cycles[cycle];

        for (int iwl0 = 0; iwl0 < wls.Count; iwl0++)
        {
            int v1 = wls[iwl0].V1;
            var vx = _vxList[v1];

            int nn = 0;
            double dist = 99999;
            for (int i = 0; i < wls0.Count; i++)
            {
                var vx1 = _vxList[wls0[i].V1];
                double cdist = Math.Pow(vx1.X - vx.X, 2) + Math.Pow(vx1.Z - vx.Z, 2);
                if (cdist < dist) { dist = cdist; nn = i; }
            }

            int ii = nn;
            for (int c = 0; c < wls0.Count; c++)
            {
                int v2 = wls0[ii].V1;
                if (DoesLineIntersect(cycles, v1, v2))
                {
                    ii = (ii + 1) % wls0.Count;
                    continue;
                }
                nwl = iwl0;
                return ii;
            }
        }
        return -1;
    }

    private bool DoesLineIntersect(List<List<DfWall>> cycles, int nv1, int nv2)
    {
        var vx1 = _vxList[nv1];
        var vx2 = _vxList[nv2];
        for (int i = 0; i < cycles.Count; i++)
        {
            var cyc = cycles[i];
            for (int j = 0; j < cyc.Count; j++)
            {
                var w = cyc[j];
                if (w.V1 == nv1 || w.V1 == nv2 || w.V2 == nv1 || w.V2 == nv2) continue;
                var vx3 = _vxList[w.V1];
                var vx4 = _vxList[w.V2];
                if (DoIntersect(vx1.X, vx1.Z, vx2.X, vx2.Z, vx3.X, vx3.Z, vx4.X, vx4.Z))
                    return true;
            }
        }
        return false;
    }

    private void MergeCycleAt(List<List<DfWall>> cycles, int ncyc, int nwl0, int nMain)
    {
        var wls0 = cycles[0];
        int vx0 = wls0[nwl0].V1;
        var wls = cycles[ncyc];
        int vxn = wls[0].V1; // merged via the wall found in FindNonIntersectingWall

        // The original connects via the walls whose first vertices are the
        // nearest pair; reconstruct the same pair of connector walls.
        var wall1 = new DfWall { V1 = vx0, V2 = vxn, Adjoin = _nSector };
        wls0.Insert(nwl0, wall1);
        var wall2 = new DfWall { V1 = vxn, V2 = vx0, Adjoin = _nSector };
        wls0.Insert(nwl0 + 1, wall2);
        int ins = nwl0 + 1;

        int nn = wls.Count > 0 ? (wls.Count - 1) : 0; // PrevWL(0)
        for (int w = 0; w < wls.Count; w++)
        {
            wls0.Insert(ins, wls[nn]);
            nn = nn > 0 ? nn - 1 : wls.Count - 1;
        }
    }

    /// <summary>Breaks a (possibly concave) wall cycle into convex polygons, then into sectors.</summary>
    private void ImportCycle(List<DfWall> cycle)
    {
        var polys = new List<List<DfWall>>();

        // Convex already?
        bool nonConvex = false;
        for (int i = 0; i < cycle.Count; i++)
        {
            var prev = i > 0 ? cycle[i - 1] : cycle[cycle.Count - 1];
            if (!Is0to180inc(prev.V1, cycle[i].V1, cycle[i].V2)) { nonConvex = true; break; }
        }
        if (!nonConvex && cycle.Count <= 24)
        {
            PolyToJKSector(cycle);
            return;
        }

        while (true)
        {
            if (cycle.Count == 3)
            {
                AddTriangle(polys, cycle[0], cycle[1], cycle[2]);
                break;
            }

            bool found = false;
            for (int i = 0; i < cycle.Count; i++)
            {
                var cwl = cycle[i];
                int iwl = (i + 1) % cycle.Count;
                var nwl = cycle[iwl];
                if (!Is0to180ex(cwl.V1, cwl.V2, nwl.V2)) continue;
                if (ArePointsInTri(cycle, cwl.V1, cwl.V2, nwl.V2)) continue;
                AddTriangle(polys, cwl, nwl, null);
                SubtractTri(cycle, i);
                found = true;
                break;
            }
            if (cycle.Count == 3)
            {
                AddTriangle(polys, cycle[0], cycle[1], cycle[2]);
                break;
            }

            for (int i = 0; i < cycle.Count; i++)
            {
                int iwl = i > 0 ? i - 1 : cycle.Count - 1;
                var nwl = cycle[i];
                var cwl = cycle[iwl];
                if (!Is0to180ex(cwl.V1, cwl.V2, nwl.V2)) continue;
                if (ArePointsInTri(cycle, cwl.V1, cwl.V2, nwl.V2)) continue;
                AddTriangle(polys, cwl, nwl, null);
                SubtractTri(cycle, iwl);
                found = true;
                break;
            }

            if (!found) break;
        }

        MergePolys(polys);
        foreach (var poly in polys)
            PolyToJKSector(poly);
    }

    /// <summary>True if any vertex of the cycle (besides the triangle's own) lies strictly inside.</summary>
    private bool ArePointsInTri(List<DfWall> cycle, int nv1, int nv2, int nv3)
    {
        foreach (var w in cycle)
        {
            int iv = w.V1;
            if (iv == nv1 || iv == nv2 || iv == nv3) continue;
            if (Is0to180inc(nv1, nv2, iv) && Is0to180inc(nv2, nv3, iv) && Is0to180inc(nv3, nv1, iv))
                return true;
        }
        return false;
    }

    private void AddTriangle(List<List<DfWall>> polys, DfWall wl1, DfWall wl2, DfWall? wl3)
    {
        var cpoly = new List<DfWall> { wl1, wl2 };
        if (wl3 is not null)
        {
            cpoly.Add(wl3);
        }
        else
        {
            var wl = new DfWall { V1 = wl2.V2, V2 = wl1.V1, Adjoin = _nSector };
            cpoly.Add(wl);
        }
        polys.Add(cpoly);
    }

    /// <summary>Removes wall <paramref name="swall"/> from the cycle, replacing it with the chord.</summary>
    private void SubtractTri(List<DfWall> cycle, int swall)
    {
        int ewall = (swall + 1) % cycle.Count;
        var wl = new DfWall { V1 = cycle[swall].V1, V2 = cycle[ewall].V2, Adjoin = _nSector };
        cycle.RemoveAt(swall);
        if (swall >= cycle.Count)
        {
            cycle.RemoveAt(0);
            cycle.Add(wl);
        }
        else
        {
            // The original removes the next wall too (it shifted into the
            // deleted slot) and inserts the chord, so a clip drops two walls
            // and adds one — the cycle shrinks by one per ear.
            cycle.RemoveAt(swall);
            cycle.Insert(swall, wl);
        }
    }

    /// <summary>Merges adjacent triangles while the result stays convex and under 24 walls.</summary>
    private void MergePolys(List<List<DfWall>> polys)
    {
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < polys.Count; i++)
            {
                var pl = polys[i];
                if (pl.Count >= 24) continue;
                for (int j = 0; j < pl.Count; j++)
                {
                    if (!FindBackWall(polys, i, pl[j], out int p, out int w)) continue;
                    if (!WouldBeConvex(polys, i, j, p, w)) continue;
                    if (polys[p].Count >= 24) continue;
                    DoMergePolys(polys, i, j, p, w);
                    merged = true;
                    break;
                }
                if (merged) break;
            }
        } while (merged);
    }

    private static bool FindBackWall(List<List<DfWall>> polys, int self, DfWall wl, out int np, out int nw)
    {
        for (int i = 0; i < polys.Count; i++)
        {
            var pl = polys[i];
            for (int j = 0; j < pl.Count; j++)
            {
                if (pl[j].V1 == wl.V2 && pl[j].V2 == wl.V1) { np = i; nw = j; return true; }
            }
        }
        np = -1; nw = -1;
        return false;
    }

    private bool WouldBeConvex(List<List<DfWall>> polys, int np1, int nw1, int np2, int nw2)
    {
        var wl1 = polys[np1][nw1 > 0 ? nw1 - 1 : polys[np1].Count - 1];
        var wl2 = polys[np2][(nw2 + 1) % polys[np2].Count];
        if (!Is0to180inc(wl1.V1, wl1.V2, wl2.V2)) return false;
        wl1 = polys[np2][nw2 > 0 ? nw2 - 1 : polys[np2].Count - 1];
        wl2 = polys[np1][(nw1 + 1) % polys[np1].Count];
        return Is0to180inc(wl1.V1, wl1.V2, wl2.V2);
    }

    private static void DoMergePolys(List<List<DfWall>> polys, int np1, int nw1, int np2, int nw2)
    {
        var p1 = polys[np1];
        var p2 = polys[np2];
        p1.RemoveAt(nw1);
        int nn = nw2 > 0 ? nw2 - 1 : p2.Count - 1;
        for (int i = 0; i < p2.Count - 1; i++)
        {
            p1.Insert(nw1, p2[nn]);
            nn = nn > 0 ? nn - 1 : p2.Count - 1;
        }
        polys.RemoveAt(np2);
    }

    // ---- JK sector construction (PolyToJKSector / CreateUnderWater) ----

    /// <summary>Builds the JK sector for one convex DF wall cycle.</summary>
    private void PolyToJKSector(List<DfWall> poly)
    {
        var jsec = _level.NewSector();
        jsec.ColorMap = "dflt.cmp";
        _sectorMarks[jsec] = _nSector;
        jsec.Layer = _sector.Layer;

        // Floor (reversed winding).
        var fsurf = jsec.NewSurface();
        _surfaceMarks[fsurf] = -1;
        for (int i = poly.Count - 1; i >= 0; i--)
        {
            var v = _vxList[poly[i].V1];
            var jv = jsec.AddVertex(DfToJk(v.X, _sector.FloorY, v.Z));
            AddCorner(fsurf, jv);
        }
        fsurf.SurfFlags |= SurfaceFlags.Floor;
        fsurf.Material = GetTexture(_sector.FloorTx);
        if ((_sector.Flags & 128) != 0) fsurf.SurfFlags |= SurfaceFlags.SkyHorizon;
        ArrangeTexture(fsurf, 0);

        // Ceiling (forward winding).
        var csurf = jsec.NewSurface();
        _surfaceMarks[csurf] = -1;
        for (int i = 0; i < poly.Count; i++)
        {
            var v = _vxList[poly[i].V1];
            var jv = jsec.AddVertex(DfToJk(v.X, _sector.CeilingY, v.Z));
            AddCorner(csurf, jv);
        }
        csurf.Material = GetTexture(_sector.CeilingTx);
        if ((_sector.Flags & 1) != 0) csurf.SurfFlags |= SurfaceFlags.SkyHorizon;
        ArrangeTexture(csurf, 0);

        // Side walls.
        int nv = poly.Count;
        for (int i = 0; i < poly.Count; i++)
        {
            var jsurf = jsec.NewSurface();
            var w = poly[i];
            _surfaceMarks[jsurf] = w.Adjoin;
            _nmat[jsurf] = w.IBot + (w.ITop << 16);
            jsurf.Material = GetTexture(w.IMid);
            int j = nv - i - 1;
            AddCorner(jsurf, fsurf.Corners[j].Vertex);
            AddCorner(jsurf, fsurf.Corners[j > 0 ? j - 1 : fsurf.Corners.Count - 1].Vertex);
            AddCorner(jsurf, csurf.Corners[(i + 1) % csurf.Corners.Count].Vertex);
            AddCorner(jsurf, csurf.Corners[i].Vertex);
            jsurf.RecalcNormal();
        }

        // Underwater second-floor volume.
        if (_sector.SecY > 0)
        {
            double az = -_sector.FloorY / _options.ScaleFactor;
            double awz = -(_sector.FloorY + _sector.SecY) / _options.ScaleFactor;
            CreateUnderWater(poly, az, awz);
        }
    }

    /// <summary>The water slab below the floor, from the floor level down to the second floor.</summary>
    private void CreateUnderWater(List<DfWall> poly, double fl, double cl)
    {
        var jsec = _level.NewSector();
        jsec.ColorMap = "dflt.cmp";
        _sectorMarks[jsec] = _nSector;
        jsec.Layer = _sector.Layer;
        jsec.Flags = 2;

        var fsurf = jsec.NewSurface();
        _surfaceMarks[fsurf] = -1;
        for (int i = poly.Count - 1; i >= 0; i--)
        {
            var v = _vxList[poly[i].V1];
            var jv = jsec.AddVertex(new Vec3(v.X / _options.ScaleFactor, v.Z / _options.ScaleFactor, fl));
            AddCorner(fsurf, jv);
        }
        fsurf.SurfFlags |= SurfaceFlags.Floor;
        ArrangeTexture(fsurf, 0);

        var csurf = jsec.NewSurface();
        _surfaceMarks[csurf] = _nSector;
        for (int i = 0; i < poly.Count; i++)
        {
            var v = _vxList[poly[i].V1];
            var jv = jsec.AddVertex(new Vec3(v.X / _options.ScaleFactor, v.Z / _options.ScaleFactor, cl));
            AddCorner(csurf, jv);
        }
        ArrangeTexture(csurf, 0);

        int nv = poly.Count;
        for (int i = 0; i < poly.Count; i++)
        {
            var jsurf = jsec.NewSurface();
            _surfaceMarks[jsurf] = -1;
            int j = nv - i - 1;
            AddCorner(jsurf, fsurf.Corners[j].Vertex);
            AddCorner(jsurf, fsurf.Corners[j > 0 ? j - 1 : fsurf.Corners.Count - 1].Vertex);
            AddCorner(jsurf, csurf.Corners[(i + 1) % csurf.Corners.Count].Vertex);
            AddCorner(jsurf, csurf.Corners[i].Vertex);
            jsurf.RecalcNormal();
        }
    }

    // ---- adjoins (ArrangeAdjoins) ----

    /// <summary>BOT/TOP/MID wall splitting against neighbour heights, then matching adjoins.</summary>
    private void ArrangeAdjoins()
    {
        for (int i = 0; i < _level.Sectors.Count; i++)
        {
            var sec = _level.Sectors[i];
            int secnum = _sectorMarks[sec];
            double csecFZ = -_dfSectors[secnum].FloorY / _options.ScaleFactor;
            double csecCZ = -_dfSectors[secnum].CeilingY / _options.ScaleFactor;

            for (int j = sec.Surfaces.Count - 1; j >= 0; j--)
            {
                var surf = sec.Surfaces[j];
                int mark = _surfaceMarks[surf];
                if (mark < 0 || mark == secnum) continue;
                int adjsc = mark;
                double sec2FZ = -_dfSectors[adjsc].FloorY / _options.ScaleFactor;
                double sec2CZ = -_dfSectors[adjsc].CeilingY / _options.ScaleFactor;

                if ((sec2FZ <= csecFZ && sec2CZ >= csecCZ) ||
                    sec2FZ >= csecCZ || sec2CZ <= csecFZ)
                    continue;

                bool bot = sec2FZ > csecFZ && sec2CZ >= csecCZ;
                bool top = sec2FZ <= csecFZ && sec2CZ < csecCZ;
                bool both = sec2FZ > csecFZ && sec2CZ < csecCZ;

                if (bot)
                {
                    // BOT cap: our floor down to the neighbour's floor.
                    var v1 = surf.Corners[0].Vertex; var v2 = surf.Corners[1].Vertex;
                    var botSurf = sec.NewSurface();
                    _surfaceMarks[botSurf] = -1;
                    var v3 = sec.AddVertex(new Vec3(v2.Position.X, v2.Position.Y, sec2FZ));
                    var v4 = sec.AddVertex(new Vec3(v1.Position.X, v1.Position.Y, sec2FZ));
                    AddCorner(botSurf, v1); AddCorner(botSurf, v2); AddCorner(botSurf, v3); AddCorner(botSurf, v4);
                    botSurf.Material = GetTexture(_nmat[surf] & 0xffff);
                    botSurf.RecalcNormal();

                    // MID: neighbour floor up to our ceiling.
                    var midSurf = sec.NewSurface();
                    _surfaceMarks[midSurf] = adjsc;
                    AddCorner(midSurf, v4); AddCorner(midSurf, v3);
                    AddCorner(midSurf, surf.Corners[2].Vertex); AddCorner(midSurf, surf.Corners[3].Vertex);
                    midSurf.Material = surf.Material;
                    midSurf.RecalcNormal();
                }

                if (top)
                {
                    // MID: our floor up to the neighbour's ceiling.
                    var v1 = surf.Corners[0].Vertex; var v2 = surf.Corners[1].Vertex;
                    var midSurf = sec.NewSurface();
                    _surfaceMarks[midSurf] = adjsc;
                    var v3 = sec.AddVertex(new Vec3(v2.Position.X, v2.Position.Y, sec2CZ));
                    var v4 = sec.AddVertex(new Vec3(v1.Position.X, v1.Position.Y, sec2CZ));
                    AddCorner(midSurf, v1); AddCorner(midSurf, v2); AddCorner(midSurf, v3); AddCorner(midSurf, v4);
                    midSurf.Material = surf.Material;
                    midSurf.RecalcNormal();

                    // TOP cap: neighbour ceiling up to our ceiling.
                    var topSurf = sec.NewSurface();
                    _surfaceMarks[topSurf] = -1;
                    AddCorner(topSurf, v4); AddCorner(topSurf, v3);
                    AddCorner(topSurf, surf.Corners[2].Vertex); AddCorner(topSurf, surf.Corners[3].Vertex);
                    topSurf.Material = GetTexture((_nmat[surf] >> 16) & 0xffff);
                    topSurf.RecalcNormal();
                }

                if (both)
                {
                    // BOT cap.
                    var v1 = surf.Corners[0].Vertex; var v2 = surf.Corners[1].Vertex;
                    var botSurf = sec.NewSurface();
                    _surfaceMarks[botSurf] = -1;
                    var v3 = sec.AddVertex(new Vec3(v2.Position.X, v2.Position.Y, sec2FZ));
                    var v4 = sec.AddVertex(new Vec3(v1.Position.X, v1.Position.Y, sec2FZ));
                    AddCorner(botSurf, v1); AddCorner(botSurf, v2); AddCorner(botSurf, v3); AddCorner(botSurf, v4);
                    botSurf.Material = GetTexture(_nmat[surf] & 0xffff);
                    botSurf.RecalcNormal();

                    // MID.
                    var midSurf = sec.NewSurface();
                    _surfaceMarks[midSurf] = adjsc;
                    var v5 = sec.AddVertex(new Vec3(v4.Position.X, v4.Position.Y, sec2CZ));
                    var v6 = sec.AddVertex(new Vec3(v3.Position.X, v3.Position.Y, sec2CZ));
                    AddCorner(midSurf, v4); AddCorner(midSurf, v3); AddCorner(midSurf, v6); AddCorner(midSurf, v5);
                    midSurf.Material = surf.Material;
                    midSurf.RecalcNormal();

                    // TOP cap.
                    var topSurf = sec.NewSurface();
                    _surfaceMarks[topSurf] = -1;
                    AddCorner(topSurf, v6); AddCorner(topSurf, v5);
                    AddCorner(topSurf, surf.Corners[2].Vertex); AddCorner(topSurf, surf.Corners[3].Vertex);
                    topSurf.Material = GetTexture((_nmat[surf] >> 16) & 0xffff);
                    topSurf.RecalcNormal();
                }

                sec.Surfaces.RemoveAt(j);
            }
        }

        // Adjoin matching on reversed vertex order.
        for (int i = 0; i < _level.Sectors.Count; i++)
        {
            var sec = _level.Sectors[i];
            for (int j = sec.Surfaces.Count - 1; j >= 0; j--)
            {
                var surf = sec.Surfaces[j];
                int mark = _surfaceMarks[surf];
                if (mark < 0 || surf.Adjoin is not null) continue;
                AdjoinSurf(surf);
            }
        }
    }

    private void AdjoinSurf(Surface surf)
    {
        int mark = _surfaceMarks[surf];
        foreach (var sec in _level.Sectors)
        {
            if (_sectorMarks[sec] != mark) continue;
            foreach (var sf1 in sec.Surfaces)
            {
                if (sf1 == surf) continue;
                if (DoSurfMatch(surf, sf1))
                {
                    surf.Adjoin = sf1;
                    sf1.Adjoin = surf;
                    surf.AdjoinFlags = AdjoinFlags.Visible | AdjoinFlags.Move | AdjoinFlags.AllowSoundPass;
                    sf1.AdjoinFlags = surf.AdjoinFlags;
                    surf.Geo = 0;
                    sf1.Geo = 0;
                    surf.SurfFlags = 0;
                    sf1.SurfFlags = 0;
                    surf.FaceFlags = 0;
                    sf1.FaceFlags = 0;
                    return;
                }
            }
        }
    }

    /// <summary>Same vertex count and the partner's corners match ours in reverse order (DO_Surf_Match).</summary>
    private bool DoSurfMatch(Surface a, Surface b)
    {
        if (b.Corners.Count != a.Corners.Count) return false;
        var v0 = a.Corners[0].Vertex.Position;
        int fv = -1;
        for (int v = 0; v < b.Corners.Count; v++)
        {
            if (IsClose(v0, b.Corners[v].Vertex.Position)) { fv = v; break; }
        }
        if (fv == -1) return false;
        fv = fv > 0 ? fv - 1 : b.Corners.Count - 1;
        for (int v = 1; v < a.Corners.Count; v++)
        {
            if (!IsClose(a.Corners[v].Vertex.Position, b.Corners[fv].Vertex.Position)) return false;
            fv = fv > 0 ? fv - 1 : b.Corners.Count - 1;
        }
        return true;
    }

    private static bool IsClose(Vec3 a, Vec3 b) =>
        Math.Abs(a.X - b.X) < 10e-5 && Math.Abs(a.Y - b.Y) < 10e-5 && Math.Abs(a.Z - b.Z) < 10e-5;

    // ---- objects (LoadObjects from the .O file) ----

    private void LoadObjects(string oText)
    {
        var table = _options.LogicTable;
        var t = new LineReader(oText);
        Thing? th = null;
        int dif = 0;

        while (!t.Eof)
        {
            var s = t.ReadLine();
            var (w1, _) = Words(s);
            if (w1.Length == 0) continue;

            if (w1 == "CLASS:")
            {
                th = _level.NewThing();
                dif = 0;
                var tokens = SplitTokens(s);
                // CLASS: <logic> DATA: n X: x Y: y Z: z PCH: p YAW: y ROL: r DIFF: d
                string logic = tokens.Count > 1 ? tokens[1] : string.Empty;
                double x = 0, y = 0, z = 0;
                for (int i = 0; i + 1 < tokens.Count; i++)
                {
                    switch (tokens[i])
                    {
                        case "X:": x = ParseDouble(tokens[i + 1]); break;
                        case "Y:": y = ParseDouble(tokens[i + 1]); break;
                        case "Z:": z = ParseDouble(tokens[i + 1]); break;
                        case "PCH:": th.Pitch = ParseDouble(tokens[i + 1]); break;
                        case "YAW:": th.Yaw = ParseDouble(tokens[i + 1]); break;
                        case "ROL:": th.Roll = ParseDouble(tokens[i + 1]); break;
                        case "DIFF:": dif = ParseInt(tokens[i + 1]); break;
                    }
                }
                th.Position = DfToJk(x, y, z);
                th.Name = logic == "SPIRIT" || logic == "SAFE" ? "walkplayer" : "ghost";
            }
            else if (w1 == "SEQ")
            {
                if (th is null) continue;

                // SEQ blocks attach to the most recent CLASS and may convert the
                // thing's logic name into a JK template via the df2jk table.
                while (!t.Eof)
                {
                    var inner = t.ReadLine();
                    var ws = SplitTokens(inner);
                    if (ws.Count == 0 || ws[0] == "SEQEND") break;
                    if (ws[0] == "LOGIC:" || ws[0] == "TYPE:")
                    {
                        string logic = ws.Count > 1 ? ws[1] : string.Empty;
                        if (logic == "ITEM" && ws.Count > 2) logic = ws[2];
                        if (table is not null && table.TryGetValue(logic, out var jk))
                            th.Name = jk;
                    }
                }

                ApplyDifficulty(th, dif);
            }
        }
    }

    private void ApplyDifficulty(Thing th, int dif)
    {
        if (dif is not (-2) and not (-1) and not 2 and not 3) return;

        long f = 0;
        var v = _level.GetTemplateValue(th.Name, "thingflags");
        if (v.Length > 0)
        {
            var hex = v.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                f = parsed;
        }
        f |= dif switch
        {
            -2 => (long)ThingFlags.NoHard,
            -1 => (long)(ThingFlags.NoHard | ThingFlags.NoMedium),
            2 => (long)ThingFlags.NoEasy,
            _ => (long)(ThingFlags.NoEasy | ThingFlags.NoMedium),
        };
        th.Values["thingflags"] = $"0x{f:x}";
    }

    // ---- layers ----

    private void ConvertLayers()
    {
        foreach (var sec in _level.Sectors)
        {
            var name = $"Layer{sec.Layer}";
            int n = _level.Layers.IndexOf(name);
            sec.Layer = n == -1 ? _level.AddLayer(name) : n;
        }
    }

    // ---- main flow ----

    private Level Run(string levText, string? objectText)
    {
        _level.Kind = ProjectType.JediKnight;
        _level.PixelsPerUnit = _pixelPerUnit;
        var t = new LineReader(levText);

        while (!t.Eof)
        {
            var s = t.ReadLine();
            var (w1, w2) = Words(s);
            if (w1.Length == 0) continue;

            if (w1 == "TEXTURES") RunTextures(t, ParseInt(w2));
            else if (w1 == "NUMSECTORS")
            {
                int n = ParseInt(w2);
                while (!t.Eof && _dfSectors.Count <= n)
                {
                    var ls = t.ReadLine();
                    if (FirstWord(ls) == "SECTOR")
                    {
                        LoadSector(t);
                        _nSector++;
                    }
                }
            }
        }

        ArrangeAdjoins();

        for (int i = 0; i < _level.Sectors.Count; i++)
        {
            SetSecLight(_level.Sectors[i], _dfSectors[_sectorMarks[_level.Sectors[i]]].Ambient);
        }

        _level.RenumberSectors();
        foreach (var sec in _level.Sectors)
        {
            sec.Renumber();
            foreach (var surf in sec.Surfaces)
                surf.RecalcNormal();
        }

        if (objectText is not null)
            LoadObjects(objectText);

        _level.RenumberThings();
        ConvertLayers();

        // Resolve each thing's sector from its position (parity ray-cast; the
        // writer recomputes sectors on save, but the views expect it set).
        foreach (var th in _level.Things)
            th.Sector = Sed.Core.Lighting.LightCalculator.FindSector(_level, th.Position);

        return _level;
    }

    private void SetSecLight(Sector s, int l)
    {
        double v = l / 31.0;
        s.Ambient = new ColorF((float)v, (float)v, (float)v);
        foreach (var surf in s.Surfaces)
            foreach (var c in surf.Corners)
                c.Intensity = new ColorF((float)v, (float)v, (float)v);
    }
}
