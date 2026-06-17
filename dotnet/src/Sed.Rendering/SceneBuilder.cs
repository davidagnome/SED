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
