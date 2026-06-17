using Sed.Core.Math;

namespace Sed.Rendering;

/// <summary>A GPU-ready vertex: position, normal, and RGB color (interleaved, all float32).</summary>
public struct MeshVertex
{
    public float Px, Py, Pz;
    public float Nx, Ny, Nz;
    public float R, G, B;

    public MeshVertex(Vec3 p, Vec3 n, ColorF c)
    {
        Px = (float)p.X; Py = (float)p.Y; Pz = (float)p.Z;
        Nx = (float)n.X; Ny = (float)n.Y; Nz = (float)n.Z;
        R = c.R; G = c.G; B = c.B;
    }

    public const uint Stride = 9 * sizeof(float);
}

/// <summary>An indexed triangle mesh produced from the level model.</summary>
public sealed class Mesh
{
    public List<MeshVertex> Vertices { get; } = new();
    public List<uint> Indices { get; } = new();

    public int TriangleCount => Indices.Count / 3;
    public bool IsEmpty => Indices.Count == 0;

    public void AddTriangle(MeshVertex a, MeshVertex b, MeshVertex c)
    {
        uint baseIndex = (uint)Vertices.Count;
        Vertices.Add(a);
        Vertices.Add(b);
        Vertices.Add(c);
        Indices.Add(baseIndex);
        Indices.Add(baseIndex + 1);
        Indices.Add(baseIndex + 2);
    }
}
