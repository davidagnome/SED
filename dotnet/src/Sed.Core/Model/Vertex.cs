using Sed.Core.Math;

namespace Sed.Core.Model;

/// <summary>A world-space vertex owned by a <see cref="Sector"/> (TJKVertex).</summary>
public sealed class Vertex
{
    public Vec3 Position;
    public Sector? Sector;

    /// <summary>Index into the file's global WORLD VERTICES list (-1 if not loaded from JKL).</summary>
    public int SourceIndex = -1;

    public Vertex() { }
    public Vertex(Vec3 position) => Position = position;
}

/// <summary>A texture-space UV coordinate (TTXVertex).</summary>
public struct TexVertex
{
    public double U;
    public double V;

    public TexVertex(double u, double v) { U = u; V = v; }
}
