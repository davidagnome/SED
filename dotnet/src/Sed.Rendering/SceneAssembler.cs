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
    /// <summary>Constant light for 3DO model surfaces (they aren't sector-lit here).</summary>
    private const float ModelLight = 0.8f;

    private readonly record struct Batch(string Material, bool Translucent, int SkyMode);
    private readonly Dictionary<Batch, Mesh> _byBatch = new();

    private Mesh For(string material, bool translucent, int skyMode = 0)
    {
        var key = new Batch(material, translucent, skyMode);
        if (!_byBatch.TryGetValue(key, out var mesh))
            _byBatch[key] = mesh = new Mesh();
        return mesh;
    }

    /// <summary>Adds the level's textured surfaces (skipping no-material adjoin portals).</summary>
    public void AddLevel(Level level)
    {
        foreach (var sector in level.Sectors)
        foreach (var surface in sector.Surfaces)
        {
            if (surface.Corners.Count < 3 || string.IsNullOrEmpty(surface.Material)) continue;
            surface.RecalcNormal();
            var n = surface.Normal;
            int skyMode = (surface.SurfFlags & Surface.SfSkyCeiling) != 0 ? 1
                        : (surface.SurfFlags & Surface.SfSkyHorizon) != 0 ? 2 : 0;
            var mesh = For(surface.Material, surface.IsTranslucent, skyMode);
            float sky = surface.IsSky ? 1f : -1f;     // -1 => use per-vertex intensity
            float ambient = sector.Ambient.R;          // sector ambient acts as a light floor
            var c0 = surface.Corners[0];
            for (int i = 1; i + 1 < surface.Corners.Count; i++)
                mesh.AddTriangle(SurfVertex(c0, n, sky, ambient), SurfVertex(surface.Corners[i], n, sky, ambient), SurfVertex(surface.Corners[i + 1], n, sky, ambient));
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
            var target = For(model.GetMaterial(face.Material), false);
            var light = new ColorF(ModelLight, ModelLight, ModelLight);

            var p0 = world.TransformPoint(Vert(mesh, face, 0));
            for (int k = 1; k + 1 < face.VertexIndices.Count; k++)
            {
                var pi = world.TransformPoint(Vert(mesh, face, k));
                var pj = world.TransformPoint(Vert(mesh, face, k + 1));
                var n = (pi - p0).Cross(pj - p0).Normalized();
                target.AddTriangle(
                    new MeshVertex(p0, n, light, U(mesh, face, 0), V(mesh, face, 0)),
                    new MeshVertex(pi, n, light, U(mesh, face, k), V(mesh, face, k)),
                    new MeshVertex(pj, n, light, U(mesh, face, k + 1), V(mesh, face, k + 1)));
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
        // Opaque first, then translucent, so the renderer can order passes by list order.
        foreach (var (batch, mesh) in _byBatch.OrderBy(kv => kv.Key.Translucent))
        {
            if (mesh.IsEmpty) continue;
            uint baseVertex = (uint)scene.Mesh.Vertices.Count;
            int indexOffset = scene.Mesh.Indices.Count;
            scene.Mesh.Vertices.AddRange(mesh.Vertices);
            foreach (var idx in mesh.Indices) scene.Mesh.Indices.Add(idx + baseVertex);
            scene.Submeshes.Add(new Submesh
            {
                Material = batch.Material,
                IndexOffset = indexOffset,
                IndexCount = mesh.Indices.Count,
                Translucent = batch.Translucent,
                SkyMode = batch.SkyMode,
            });
        }
        return scene;
    }

    /// <summary>Per-vertex light intensity is carried in the color channel (0..1); sky is full bright.</summary>
    private static MeshVertex SurfVertex(Surface.Corner c, Vec3 normal, float skyOverride, float ambient)
    {
        float i = skyOverride >= 0 ? skyOverride : System.Math.Max(c.Intensity.R, ambient);
        return new MeshVertex(c.Vertex.Position, normal, new ColorF(i, i, i), c.Uv.U, c.Uv.V);
    }
}
