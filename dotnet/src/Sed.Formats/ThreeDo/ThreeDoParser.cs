using Sed.Core.Math;
using Sed.Formats.Jkl;

namespace Sed.Formats.ThreeDo;

/// <summary>
/// Parses the Sith-engine 3DO model text format (per PJ3DO_IO.INC). Reads the
/// MODELRESOURCE materials, the highest-detail geoset's meshes (vertices,
/// texture vertices, faces) and the HIERARCHYDEF node transforms. Vertex normals
/// are skipped. Reuses <see cref="JklReader"/> for the shared tokenizer.
/// </summary>
public static class ThreeDoParser
{
    public static ThreeDoModel Parse(string text) => Parse(new StringReader(text));

    public static ThreeDoModel Parse(TextReader reader)
    {
        var model = new ThreeDoModel();
        var r = new JklReader(reader);

        while (true)
        {
            r.SeekSection();
            if (r.Eof) break;
            var section = r.Section;
            r.EnterSection();
            switch (section)
            {
                case "HEADER": LoadHeader(r, model); break;
                case "MODELRESOURCE": LoadMaterials(r, model); break;
                case "GEOMETRYDEF": LoadGeometry(r, model); break;
                case "HIERARCHYDEF": LoadHierarchy(r, model); break;
                default: r.SkipToNextSection(); break;
            }
        }
        return model;
    }

    private static void LoadHeader(JklReader r, ThreeDoModel model)
    {
        while (r.Next() && !r.EndOfSection)
        {
            var t = JklReader.Tokens(r.Current);
            if (t.Length >= 2 && t[0] == "3DO")
                model.Version = JklReader.ParseFloat(t[1]);
        }
    }

    private static void LoadMaterials(JklReader r, ThreeDoModel model)
    {
        if (!r.Next()) return;
        int n = LastInt(r.Current);
        for (int i = 0; i < n; i++)
        {
            if (!r.Next(upper: false) || r.EndOfSection) break;
            // "i: name.mat"
            var name = JklReader.WordAt(JklReader.StripIndex(r.Current), 0);
            if (name.Length > 0) model.Materials.Add(name);
        }
    }

    private static void LoadGeometry(JklReader r, ThreeDoModel model)
    {
        // RADIUS (skip), INSERT OFFSET x y z, GEOSETS n
        r.Next(); // radius
        r.Next();
        var ins = JklReader.Tokens(r.Current);
        if (ins.Length >= 5 && ins[0] == "INSERT")
            model.InsertOffset = new Vec3(JklReader.ParseFloat(ins[2]), JklReader.ParseFloat(ins[3]), JklReader.ParseFloat(ins[4]));

        r.Next(); // GEOSETS n  (we use geoset 0)
        // advance to "GEOSET 0"
        while (r.Next() && !r.EndOfSection)
            if (JklReader.FirstWord(r.Current) == "GEOSET") break;
        if (r.EndOfSection) return;

        if (!r.Next()) return; // MESHES n
        int nmeshes = LastInt(r.Current);

        for (int i = 0; i < nmeshes; i++)
        {
            // advance to "MESH"
            while (r.Next() && !r.EndOfSection)
                if (JklReader.FirstWord(r.Current) == "MESH") break;
            if (r.EndOfSection) return;

            var mesh = new Mesh3do();
            model.Meshes.Add(mesh);

            if (r.Next()) mesh.Name = JklReader.WordAt(r.Current, 1); // NAME x

            // skip mode lines until VERTICES n
            int nverts = 0;
            while (r.Next() && !r.EndOfSection)
            {
                var t = JklReader.Tokens(r.Current);
                if (t[0] == "VERTICES") { nverts = LastInt(r.Current); break; }
            }

            for (int j = 0; j < nverts; j++)
            {
                r.Next();
                var t = JklReader.Tokens(JklReader.StripIndex(r.Current));
                mesh.Vertices.Add(new Vec3(
                    JklReader.ParseFloat(t.ElementAtOrDefault(0) ?? "0"),
                    JklReader.ParseFloat(t.ElementAtOrDefault(1) ?? "0"),
                    JklReader.ParseFloat(t.ElementAtOrDefault(2) ?? "0")));
            }

            r.Next(); // TEXTURE VERTICES n
            int ntex = LastInt(r.Current);
            for (int j = 0; j < ntex; j++)
            {
                r.Next();
                var t = JklReader.Tokens(JklReader.StripIndex(r.Current));
                mesh.Uvs.Add(new Vec2(
                    JklReader.ParseFloat(t.ElementAtOrDefault(0) ?? "0"),
                    JklReader.ParseFloat(t.ElementAtOrDefault(1) ?? "0")));
            }

            // advance to FACES n
            int nfaces = 0;
            while (r.Next() && !r.EndOfSection)
            {
                var t = JklReader.Tokens(r.Current);
                if (t[0] == "FACES") { nfaces = LastInt(r.Current); break; }
            }
            for (int j = 0; j < nfaces; j++)
            {
                r.Next();
                mesh.Faces.Add(ParseFace(r.Current));
            }
            // VERTEX NORMALS follow — skipped by the next mesh's "MESH" scan.
        }
    }

    private static Face3do ParseFace(string line)
    {
        // num: material type geo light tex extralight nverts  v,uv v,uv ...
        var t = JklReader.Tokens(line);
        var face = new Face3do();
        if (t.Length < 8) return face;
        face.Material = JklReader.ParseInt(t[1]);
        int nverts = JklReader.ParseInt(t[7]);

        // Pairs start at token 8; rejoin and split commas to tolerate "v,uv" or "v, uv".
        var rest = string.Join(' ', t.Skip(8)).Replace(',', ' ');
        var pair = JklReader.Tokens(rest);
        for (int k = 0; k < nverts && k * 2 + 1 < pair.Length; k++)
        {
            face.VertexIndices.Add(JklReader.ParseInt(pair[k * 2]));
            face.UvIndices.Add(JklReader.ParseInt(pair[k * 2 + 1]));
        }
        return face;
    }

    private static void LoadHierarchy(JklReader r, ThreeDoModel model)
    {
        if (!r.Next()) return;
        int n = LastInt(r.Current);
        for (int i = 0; i < n; i++)
        {
            if (!r.Next() || r.EndOfSection) break;
            var t = JklReader.Tokens(JklReader.StripIndex(r.Current));
            // After stripping "i:": flags type mesh parent child sibling numChildren x y z pitch yaw roll ...
            if (t.Length < 13) { model.Nodes.Add(new HierarchyNode()); continue; }
            model.Nodes.Add(new HierarchyNode
            {
                Mesh = JklReader.ParseInt(t[2]),
                Parent = JklReader.ParseInt(t[3]),
                Offset = new Vec3(JklReader.ParseFloat(t[7]), JklReader.ParseFloat(t[8]), JklReader.ParseFloat(t[9])),
                Pitch = JklReader.ParseFloat(t[10]),
                Yaw = JklReader.ParseFloat(t[11]),
                Roll = JklReader.ParseFloat(t[12]),
            });
        }
    }

    private static int LastInt(string line)
    {
        var t = JklReader.Tokens(line);
        return t.Length == 0 ? 0 : JklReader.ParseInt(t[^1]);
    }
}
