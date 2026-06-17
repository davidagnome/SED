using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Rendering;

/// <summary>
/// Triangulates the level model into a renderable <see cref="Mesh"/>. Each
/// surface is fanned from its first corner; the surface normal is used for
/// shading and a per-surface hue gives flat-shaded faces a distinct look.
/// </summary>
public static class SceneBuilder
{
    /// <summary>
    /// Preview lighting: bias the per-vertex intensity toward bright so geometry
    /// is clearly visible in the editor (JK bakes much of its lighting via the
    /// colormap light tables, so raw vertex intensities are often near-zero).
    /// </summary>
    private const float LightBias = 0.65f;
    private const float LightScale = 0.6f;

    /// <summary>
    /// Builds a material-batched scene: triangles are grouped by surface material,
    /// vertices carry the surface's UVs and per-vertex light intensity (used as a
    /// Gouraud multiplier on the sampled texture).
    /// </summary>
    public static RenderScene BuildScene(Level level)
    {
        var byMaterial = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        foreach (var sector in level.Sectors)
        {
            foreach (var surface in sector.Surfaces)
            {
                if (surface.Corners.Count < 3) continue;
                surface.RecalcNormal();
                var normal = surface.Normal;

                if (!byMaterial.TryGetValue(surface.Material, out var mesh))
                    byMaterial[surface.Material] = mesh = new Mesh();

                var c0 = surface.Corners[0];
                for (int i = 1; i + 1 < surface.Corners.Count; i++)
                    mesh.AddTriangle(
                        Vertex(c0, normal),
                        Vertex(surface.Corners[i], normal),
                        Vertex(surface.Corners[i + 1], normal));
            }
        }

        var scene = new RenderScene();
        foreach (var (material, mesh) in byMaterial)
        {
            if (mesh.IsEmpty) continue;
            uint baseVertex = (uint)scene.Mesh.Vertices.Count;
            int indexOffset = scene.Mesh.Indices.Count;

            scene.Mesh.Vertices.AddRange(mesh.Vertices);
            foreach (var idx in mesh.Indices)
                scene.Mesh.Indices.Add(idx + baseVertex);

            scene.Submeshes.Add(new Submesh
            {
                Material = material,
                IndexOffset = indexOffset,
                IndexCount = mesh.Indices.Count,
            });
        }
        return scene;
    }

    private static MeshVertex Vertex(Surface.Corner c, Vec3 normal)
    {
        var lit = new ColorF(Light(c.Intensity.R), Light(c.Intensity.G), Light(c.Intensity.B));
        return new MeshVertex(c.Vertex.Position, normal, lit, c.Uv.U, c.Uv.V);
    }

    private static float Light(float intensity) =>
        System.Math.Clamp(LightBias + intensity * LightScale, 0f, 1.4f);

    public static Mesh FromLevel(Level level)
    {
        var mesh = new Mesh();
        int surfaceCounter = 0;

        foreach (var sector in level.Sectors)
        {
            foreach (var surface in sector.Surfaces)
            {
                if (surface.Corners.Count < 3)
                    continue;

                surface.RecalcNormal();
                var normal = surface.Normal;
                var color = HueColor(surfaceCounter++);

                var c0 = surface.Corners[0].Vertex.Position;
                for (int i = 1; i + 1 < surface.Corners.Count; i++)
                {
                    var ci = surface.Corners[i].Vertex.Position;
                    var cj = surface.Corners[i + 1].Vertex.Position;
                    mesh.AddTriangle(
                        new MeshVertex(c0, normal, color),
                        new MeshVertex(ci, normal, color),
                        new MeshVertex(cj, normal, color));
                }
            }
        }

        return mesh;
    }

    /// <summary>Spreads surfaces across the hue wheel for visual separation.</summary>
    private static ColorF HueColor(int index)
    {
        double h = (index * 0.618033988749895) % 1.0; // golden-ratio hue stepping
        return HsvToRgb(h, 0.55, 0.95);
    }

    private static ColorF HsvToRgb(double h, double s, double v)
    {
        double i = System.Math.Floor(h * 6);
        double f = h * 6 - i;
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        return ((int)i % 6) switch
        {
            0 => new ColorF((float)v, (float)t, (float)p),
            1 => new ColorF((float)q, (float)v, (float)p),
            2 => new ColorF((float)p, (float)v, (float)t),
            3 => new ColorF((float)p, (float)q, (float)v),
            4 => new ColorF((float)t, (float)p, (float)v),
            _ => new ColorF((float)v, (float)p, (float)q),
        };
    }
}
