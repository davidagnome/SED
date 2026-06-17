using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.ThreeDo;

namespace Sed.Rendering;

/// <summary>
/// Accumulates renderable geometry from level surfaces and instanced 3DO models
/// into per-material meshes, then emits a single material-batched
/// <see cref="RenderScene"/>. UVs are kept in texel units (the renderer
/// normalizes per material), matching the original engine.
/// </summary>
public sealed class SceneAssembler
{
    private const float LightBias = 0.65f, LightScale = 0.6f;
    private static readonly ColorF ModelLight = new(0.85f, 0.85f, 0.85f);

    private readonly Dictionary<string, Mesh> _byMaterial = new(StringComparer.OrdinalIgnoreCase);

    private Mesh For(string material)
    {
        if (!_byMaterial.TryGetValue(material, out var mesh))
            _byMaterial[material] = mesh = new Mesh();
        return mesh;
    }

    /// <summary>Adds the level's textured surfaces (skipping no-material adjoin/sky portals).</summary>
    public void AddLevel(Level level)
    {
        foreach (var sector in level.Sectors)
        foreach (var surface in sector.Surfaces)
        {
            if (surface.Corners.Count < 3 || string.IsNullOrEmpty(surface.Material)) continue;
            surface.RecalcNormal();
            var n = surface.Normal;
            var mesh = For(surface.Material);
            var c0 = surface.Corners[0];
            for (int i = 1; i + 1 < surface.Corners.Count; i++)
                mesh.AddTriangle(SurfVertex(c0, n), SurfVertex(surface.Corners[i], n), SurfVertex(surface.Corners[i + 1], n));
        }
    }

    /// <summary>
    /// Instances each thing's 3DO model (resolved via templates) at its position
    /// and orientation. Returns the things that had no model (caller can mark them).
    /// </summary>
    public List<Thing> AddThings(Level level, Func<string, ThreeDoModel?> modelProvider)
    {
        var unmodeled = new List<Thing>();
        foreach (var thing in level.Things)
        {
            var modelName = level.GetThingModel(thing);
            var model = string.IsNullOrEmpty(modelName) ? null : modelProvider(modelName);
            if (model is null || model.Meshes.Count == 0) { unmodeled.Add(thing); continue; }

            var world = Mat4.Translate(thing.Position) * Mat4.FromPyr(thing.Pitch, thing.Yaw, thing.Roll);
            AddModel(model, world);
        }
        return unmodeled;
    }

    /// <summary>Instances a 3DO model under a world transform, applying its node hierarchy.</summary>
    public void AddModel(ThreeDoModel model, Mat4 world)
    {
        if (model.Nodes.Count == 0)
        {
            foreach (var mesh in model.Meshes) AddMesh(mesh, world, model);
            return;
        }

        var worlds = new Mat4?[model.Nodes.Count];
        Mat4 WorldOf(int i)
        {
            if (worlds[i] is { } cached) return cached;
            var nd = model.Nodes[i];
            var local = Mat4.Translate(nd.Offset) * Mat4.FromPyr(nd.Pitch, nd.Yaw, nd.Roll);
            var w = nd.Parent >= 0 && nd.Parent < model.Nodes.Count ? WorldOf(nd.Parent) * local : world * local;
            worlds[i] = w;
            return w;
        }

        for (int i = 0; i < model.Nodes.Count; i++)
        {
            var node = model.Nodes[i];
            if ((uint)node.Mesh < (uint)model.Meshes.Count)
                AddMesh(model.Meshes[node.Mesh], WorldOf(i), model);
        }
    }

    private void AddMesh(Mesh3do mesh, Mat4 world, ThreeDoModel model)
    {
        foreach (var face in mesh.Faces)
        {
            if (face.VertexIndices.Count < 3) continue;
            var target = For(model.GetMaterial(face.Material));

            var p0 = world.TransformPoint(Vert(mesh, face, 0));
            for (int k = 1; k + 1 < face.VertexIndices.Count; k++)
            {
                var pi = world.TransformPoint(Vert(mesh, face, k));
                var pj = world.TransformPoint(Vert(mesh, face, k + 1));
                var n = (pi - p0).Cross(pj - p0).Normalized();
                target.AddTriangle(
                    new MeshVertex(p0, n, ModelLight, U(mesh, face, 0), V(mesh, face, 0)),
                    new MeshVertex(pi, n, ModelLight, U(mesh, face, k), V(mesh, face, k)),
                    new MeshVertex(pj, n, ModelLight, U(mesh, face, k + 1), V(mesh, face, k + 1)));
            }
        }
    }

    private static Vec3 Vert(Mesh3do mesh, Face3do face, int corner)
    {
        int vi = face.VertexIndices[corner];
        return (uint)vi < (uint)mesh.Vertices.Count ? mesh.Vertices[vi] : Vec3.Zero;
    }

    private static double U(Mesh3do mesh, Face3do face, int corner) => Uv(mesh, face, corner).X;
    private static double V(Mesh3do mesh, Face3do face, int corner) => Uv(mesh, face, corner).Y;

    private static Vec2 Uv(Mesh3do mesh, Face3do face, int corner)
    {
        if (corner >= face.UvIndices.Count) return Vec2.Zero;
        int ti = face.UvIndices[corner];
        return (uint)ti < (uint)mesh.Uvs.Count ? mesh.Uvs[ti] : Vec2.Zero;
    }

    public RenderScene Build()
    {
        var scene = new RenderScene();
        foreach (var (material, mesh) in _byMaterial)
        {
            if (mesh.IsEmpty) continue;
            uint baseVertex = (uint)scene.Mesh.Vertices.Count;
            int indexOffset = scene.Mesh.Indices.Count;
            scene.Mesh.Vertices.AddRange(mesh.Vertices);
            foreach (var idx in mesh.Indices) scene.Mesh.Indices.Add(idx + baseVertex);
            scene.Submeshes.Add(new Submesh { Material = material, IndexOffset = indexOffset, IndexCount = mesh.Indices.Count });
        }
        return scene;
    }

    private static MeshVertex SurfVertex(Surface.Corner c, Vec3 normal)
    {
        var lit = new ColorF(Light(c.Intensity.R), Light(c.Intensity.G), Light(c.Intensity.B));
        return new MeshVertex(c.Vertex.Position, normal, lit, c.Uv.U, c.Uv.V);
    }

    private static float Light(float intensity) => System.Math.Clamp(LightBias + intensity * LightScale, 0f, 1.4f);
}
