using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Formats.Jkl;

/// <summary>
/// Parses a Sith-engine JKL level file into the editor <see cref="Level"/> model.
/// Faithful to the original SED loader (LEVEL_IO.INC): the GEORESOURCE section
/// builds global vertex/texture-vertex/surface tables which the SECTORS section
/// then references, de-duplicating vertices per sector.
///
/// Geometry, sectors, and things are parsed; materials are tracked by name.
/// COGs, lights, layers, and adjoin resolution are not required to render and
/// are skipped for now.
/// </summary>
public static class JklParser
{
    public static Level Parse(string text) => Parse(new StringReader(text));

    public static Level ParseFile(string path)
    {
        using var sr = new StreamReader(path);
        return Parse(sr);
    }

    public static Level Parse(TextReader reader)
    {
        var level = new Level();
        var r = new JklReader(reader);
        var geo = new GeoData();

        while (true)
        {
            r.SeekSection();
            if (r.Eof) break;
            var section = r.Section;
            r.EnterSection();

            switch (section)
            {
                case "HEADER": LoadHeader(r, level); break;
                case "MATERIALS": LoadMaterials(r, geo); break;
                case "GEORESOURCE": LoadGeometry(r, level, geo); break;
                case "SECTORS": LoadSectors(r, level, geo); break;
                case "TEMPLATES": LoadTemplates(r, level); break;
                case "THINGS": LoadThings(r, level); break;
                default: r.SkipToNextSection(); break;
            }
        }

        PostProcess(level);
        return level;
    }

    // ---- HEADER ----

    private static void LoadHeader(JklReader r, Level level)
    {
        while (r.Next() && !r.EndOfSection)
        {
            var t = JklReader.Tokens(r.Current);
            if (t.Length < 2) continue;
            if (t[0] == "VERSION")
            {
                level.Header.Version = JklReader.ParseInt(t[1]);
                level.Kind = level.Header.Version is 2 or 3
                    ? ProjectType.InfernalMachine
                    : ProjectType.JediKnight;
            }
            else if (t[0] == "WORLD" && t[1] == "GRAVITY" && t.Length >= 3)
            {
                level.Header.Gravity = (float)JklReader.ParseFloat(t[2]);
            }
        }
    }

    // ---- MATERIALS ----

    private static void LoadMaterials(JklReader r, GeoData geo)
    {
        if (!r.Next()) return;
        int n = LastInt(r.Current);
        for (int i = 0; i < n; i++)
        {
            if (!r.Next(upper: false) || r.EndOfSection) break;
            var body = JklReader.StripIndex(r.Current).Trim();
            int dot = body.IndexOf(".mat", StringComparison.OrdinalIgnoreCase);
            geo.Materials.Add(dot >= 0 ? body[..(dot + 4)].Trim() : JklReader.FirstWord(body));
        }
    }

    // ---- GEORESOURCE ----

    private static void LoadGeometry(JklReader r, Level level, GeoData geo)
    {
        bool ijim = level.Kind == ProjectType.InfernalMachine;

        // Colormaps (absent for IJIM): "i: name.cmp"
        if (!ijim)
        {
            r.Next();
            int nc = LastInt(r.Current);
            for (int i = 0; i < nc; i++)
            {
                r.Next();
                var name = JklReader.WordAt(JklReader.StripIndex(r.Current), 0);
                if (name.Length > 0) level.ColorMaps.Add(name);
            }
        }

        // World vertices: "i: x y z"
        r.Next();
        int nv = LastInt(r.Current);
        for (int i = 0; i < nv; i++)
        {
            r.Next();
            var t = JklReader.Tokens(JklReader.StripIndex(r.Current));
            geo.Vertices.Add(new Vec3(
                JklReader.ParseFloat(t.ElementAtOrDefault(0) ?? "0"),
                JklReader.ParseFloat(t.ElementAtOrDefault(1) ?? "0"),
                JklReader.ParseFloat(t.ElementAtOrDefault(2) ?? "0")));
        }

        // Texture vertices: "i: u v"
        r.Next();
        int nt = LastInt(r.Current);
        for (int i = 0; i < nt; i++)
        {
            r.Next();
            var t = JklReader.Tokens(JklReader.StripIndex(r.Current));
            geo.TexVertices.Add(new Vec2(
                JklReader.ParseFloat(t.ElementAtOrDefault(0) ?? "0"),
                JklReader.ParseFloat(t.ElementAtOrDefault(1) ?? "0")));
        }

        // Adjoins: "i: flags mirror" — not needed to render; skip.
        r.Next();
        int na = LastInt(r.Current);
        for (int i = 0; i < na; i++) r.Next();

        // Surfaces.
        r.Next();
        int ns = LastInt(r.Current);
        for (int i = 0; i < ns; i++)
        {
            r.Next();
            geo.Surfaces.Add(ParseSurface(JklReader.StripIndex(r.Current), level.Header.Version, ijim, level));
        }

        // Normals: one line per surface — skip.
        for (int i = 0; i < ns; i++) r.Next();
    }

    private static TempSurface ParseSurface(string line, int version, bool ijim, Level level)
    {
        // Vertex refs use "vx,tvx"; turning commas into spaces lets us read pairs.
        var t = JklReader.Tokens(line.Replace(',', ' '));
        var s = new TempSurface();
        int k = 0;
        int Int() => JklReader.ParseInt(t.ElementAtOrDefault(k++) ?? "0");
        long Hex() => JklReader.ParseHex(t.ElementAtOrDefault(k++) ?? "0");
        double Flt() => JklReader.ParseFloat(t.ElementAtOrDefault(k++) ?? "0");

        s.Material = Int();
        s.SurfFlags = Hex();
        s.FaceFlags = Hex();
        s.Geo = Int();
        s.Light = Int();
        s.Tex = Int();
        s.Adjoin = Int();

        float extraA;
        if (ijim)
        {
            float er = (float)Flt(), eg = (float)Flt(), eb = (float)Flt();
            extraA = (float)Flt();
            s.ExtraLight = new ColorF(er, eg, eb);
        }
        else
        {
            extraA = (float)Flt(); // single intensity
            s.ExtraLight = new ColorF(extraA, extraA, extraA);
        }

        int nvx = Int();
        for (int j = 0; j < nvx; j++)
        {
            s.Vxs.Add(Int());
            s.Tvxs.Add(Int());
        }

        // Remaining tokens are per-vertex light intensities.
        var il = new List<double>();
        while (k < t.Length) il.Add(Flt());

        if (il.Count > nvx)
        {
            // MotS (version 1 with extra data) or IJIM: RGB(A) per vertex.
            if (version == 1) level.Kind = ProjectType.MysteriesOfTheSith;
            int off = 0;
            for (int j = 0; j < nvx; j++)
            {
                if (version == 1) off++; // MotS leading alpha
                float cr = (float)il.ElementAtOrDefault(off++);
                float cg = (float)il.ElementAtOrDefault(off++);
                float cb = (float)il.ElementAtOrDefault(off++);
                s.Intensities.Add(new ColorF(cr, cg, cb));
            }
        }
        else
        {
            for (int j = 0; j < nvx; j++)
            {
                float a = (float)il.ElementAtOrDefault(j);
                s.Intensities.Add(new ColorF(a, a, a));
            }
        }

        return s;
    }

    // ---- SECTORS ----

    private static void LoadSectors(JklReader r, Level level, GeoData geo)
    {
        if (!r.Next()) return;
        int numSecs = LastInt(r.Current);

        for (int i = 0; i < numSecs; i++)
        {
            if (!r.Next() || r.EndOfSection) break; // "SECTOR i"
            var sector = level.NewSector();
            var globalToLocal = new Dictionary<int, Vertex>();

            while (true)
            {
                if (!r.Next() || r.EndOfSection) break;
                var t = JklReader.Tokens(r.Current);
                var w = t[0];

                if (w == "SURFACES")
                {
                    int bsurf = JklReader.ParseInt(t.ElementAtOrDefault(1) ?? "0");
                    int nsurfs = JklReader.ParseInt(t.ElementAtOrDefault(2) ?? "0");
                    BuildSectorSurfaces(sector, geo, bsurf, nsurfs, globalToLocal);
                    break;
                }
                if (w == "VERTICES")
                {
                    int nvx = JklReader.ParseInt(t.ElementAtOrDefault(1) ?? "0");
                    for (int j = 0; j < nvx; j++) r.Next();
                    continue;
                }
                if (w == "FLAGS") sector.Flags = JklReader.ParseHex(t.ElementAtOrDefault(1) ?? "0");
                else if (w == "TINT" && t.Length >= 4)
                    sector.Tint = new ColorF((float)JklReader.ParseFloat(t[1]),
                        (float)JklReader.ParseFloat(t[2]), (float)JklReader.ParseFloat(t[3]));
                // AMBIENT / EXTRA / SOUND / COLORMAP / etc. are not needed to render.
            }
        }
    }

    private static void BuildSectorSurfaces(Sector sector, GeoData geo, int bsurf, int nsurfs,
        Dictionary<int, Vertex> globalToLocal)
    {
        for (int isurf = bsurf; isurf < bsurf + nsurfs; isurf++)
        {
            if ((uint)isurf >= (uint)geo.Surfaces.Count) continue;
            var ts = geo.Surfaces[isurf];
            var surf = sector.NewSurface();
            surf.Material = geo.GetMaterial(ts.Material);
            surf.SurfFlags = ts.SurfFlags;

            for (int j = 0; j < ts.Vxs.Count; j++)
            {
                int gidx = ts.Vxs[j];
                if (!globalToLocal.TryGetValue(gidx, out var vertex))
                {
                    var pos = (uint)gidx < (uint)geo.Vertices.Count ? geo.Vertices[gidx] : Vec3.Zero;
                    vertex = sector.AddVertex(pos);
                    globalToLocal[gidx] = vertex;
                }

                int tvx = ts.Tvxs[j];
                var uv = (uint)tvx < (uint)geo.TexVertices.Count ? geo.TexVertices[tvx] : Vec2.Zero;
                surf.Corners.Add(new Surface.Corner
                {
                    Vertex = vertex,
                    Uv = new TexVertex(uv.X, uv.Y),
                    Intensity = j < ts.Intensities.Count ? ts.Intensities[j] : ColorF.White,
                });
            }
        }
    }

    // ---- TEMPLATES ----

    private static void LoadTemplates(JklReader r, Level level)
    {
        if (!r.Next()) return; // "World templates n"
        int n = LastInt(r.Current);
        for (int i = 0; i < n; i++)
        {
            if (!r.Next(upper: false) || r.EndOfSection) break;
            var t = JklReader.Tokens(r.Current);
            if (t.Length < 2 || t[0] is "#" or "//") continue;

            var tpl = new Template { Name = t[0], Parent = t[1] };
            for (int p = 2; p < t.Length; p++)
            {
                int eq = t[p].IndexOf('=');
                if (eq > 0) tpl.Values[t[p][..eq]] = t[p][(eq + 1)..];
            }
            level.Templates[tpl.Name] = tpl;
        }
    }

    // ---- THINGS ----

    private static void LoadThings(JklReader r, Level level)
    {
        if (!r.Next()) return;
        int n = LastInt(r.Current);
        for (int i = 0; i < n; i++)
        {
            if (!r.Next(upper: false) || r.EndOfSection) break;
            var t = JklReader.Tokens(r.Current);
            if (t.Length < 10) continue;

            var thing = level.NewThing();
            // t[0]=index, t[1]=template, t[2]=name, t[3..5]=xyz, t[6..8]=pyr, t[9]=sector
            thing.Template = t[1];
            thing.Name = t[2];
            thing.Position = new Vec3(
                JklReader.ParseFloat(t[3]), JklReader.ParseFloat(t[4]), JklReader.ParseFloat(t[5]));
            thing.Pitch = JklReader.ParseFloat(t[6]);
            thing.Yaw = JklReader.ParseFloat(t[7]);
            thing.Roll = JklReader.ParseFloat(t[8]);
            int sec = JklReader.ParseInt(t[9]);
            if ((uint)sec < (uint)level.Sectors.Count) thing.Sector = level.Sectors[sec];

            for (int p = 10; p < t.Length; p++)
            {
                int eq = t[p].IndexOf('=');
                if (eq <= 0) continue;
                thing.Values[t[p][..eq]] = t[p][(eq + 1)..];
            }
        }
    }

    // ---- post ----

    private static void PostProcess(Level level)
    {
        level.RenumberSectors();
        level.RenumberThings();
        foreach (var sector in level.Sectors)
        {
            sector.Renumber();
            foreach (var surf in sector.Surfaces)
                surf.RecalcNormal();
        }
    }

    private static int LastInt(string line)
    {
        var t = JklReader.Tokens(line);
        return t.Length == 0 ? 0 : JklReader.ParseInt(t[^1]);
    }
}
