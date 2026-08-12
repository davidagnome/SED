using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Formats.ThreeDo;

/// <summary>
/// Builds a 3DO model from level sectors, mirroring the original's
/// "Export Sector as 3DO" (JED_MAIN.PAS): sectors are grouped by layer into
/// meshes named after the layer, vertices are centred on the active sector's
/// centroid and deduplicated by position, adjoined surfaces are skipped (they
/// are portals, not geometry), and each face carries the surface's engine
/// fields. The hierarchy is the default one — a single node, or a dummy root
/// with one child per mesh.
/// </summary>
public static class ThreeDoExport
{
    /// <summary>Builds the model for <paramref name="sectors"/> (the active one first).</summary>
    public static ThreeDoModel BuildModel(Level level, IReadOnlyList<Sector> sectors,
        Func<int, string>? layerName = null)
    {
        layerName ??= i => (uint)i < (uint)level.Layers.Count ? level.Layers[i] : $"Layer{i}";

        var model = new ThreeDoModel { Version = 2.1 };
        var materials = model.Materials;

        // Centre on the active sector's centroid (FindCenter).
        var center = Centroid(sectors[0]);

        // Group selected sectors by layer — one mesh per layer (nsel loop).
        foreach (var group in sectors.GroupBy(s => s.Layer).OrderBy(g => g.Key))
        {
            var mesh = new Mesh3do { Name = layerName(group.Key) };
            model.Meshes.Add(mesh);
            foreach (var sec in group)
                AddSectorToMesh(mesh, sec, center, materials);
        }

        BuildDefaultHierarchy(model);
        return model;
    }

    private static void AddSectorToMesh(Mesh3do mesh, Sector sec, Vec3 center, List<string> materials)
    {
        // Deduplicate sector vertices by position; the face corners reference
        // the dedup'd index (the original's mark field).
        var marks = new int[sec.Vertices.Count];
        for (int i = 0; i < sec.Vertices.Count; i++)
            marks[i] = AddVertex(mesh, sec.Vertices[i].Position - center);

        foreach (var surf in sec.Surfaces)
        {
            if (surf.Adjoin is not null) continue; // portals are not geometry

            var face = new Face3do
            {
                Material = AddMaterial(materials, surf.Material),
                FaceFlags = surf.FaceFlags,
                Geo = surf.Geo,
                Light = surf.Light,
                Tex = surf.Tex,
                ExtraLight = surf.ExtraLightIntensity,
            };

            foreach (var corner in surf.Corners)
            {
                face.VertexIndices.Add(marks[sec.Vertices.IndexOf(corner.Vertex)]);
                face.UvIndices.Add(AddUv(mesh, corner.Uv));
            }
            mesh.Faces.Add(face);
        }
    }

    private static int AddMaterial(List<string> materials, string material)
    {
        int i = materials.IndexOf(material);
        if (i == -1)
        {
            i = materials.Count;
            materials.Add(material);
        }
        return i;
    }

    /// <summary>Adds a vertex unless an identical one exists (AddVertex, duplicates=False).</summary>
    private static int AddVertex(Mesh3do mesh, Vec3 p)
    {
        for (int i = 0; i < mesh.Vertices.Count; i++)
            if (mesh.Vertices[i] == p)
                return i;
        mesh.Vertices.Add(p);
        return mesh.Vertices.Count - 1;
    }

    /// <summary>Adds a texture vertex unless an identical one exists (AddTXVX).</summary>
    private static int AddUv(Mesh3do mesh, TexVertex uv)
    {
        var v = new Vec2(uv.U, uv.V);
        for (int i = 0; i < mesh.Uvs.Count; i++)
            if (mesh.Uvs[i] == v)
                return i;
        mesh.Uvs.Add(v);
        return mesh.Uvs.Count - 1;
    }

    private static void BuildDefaultHierarchy(ThreeDoModel model)
    {
        if (model.Meshes.Count == 1)
        {
            model.Nodes.Add(new HierarchyNode { Mesh = 0, Parent = -1 });
            return;
        }

        model.Nodes.Add(new HierarchyNode { Mesh = -1, Parent = -1 }); // $$DUMMY
        for (int i = 0; i < model.Meshes.Count; i++)
            model.Nodes.Add(new HierarchyNode { Mesh = i, Parent = 0 });
    }

    /// <summary>The average of the sector's vertices (CalcSecCenter).</summary>
    private static Vec3 Centroid(Sector sec)
    {
        var sum = Vec3.Zero;
        foreach (var v in sec.Vertices) sum += v.Position;
        return sec.Vertices.Count == 0 ? Vec3.Zero : sum / sec.Vertices.Count;
    }
}
