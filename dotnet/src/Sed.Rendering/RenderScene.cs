namespace Sed.Rendering;

/// <summary>
/// An 8-bit palette-indexed texture (one byte per pixel). The renderer applies
/// the CMP palette + light table on the GPU, so lighting matches the engine.
/// </summary>
public readonly record struct IndexedTexture(int Width, int Height, byte[] Indices);

/// <summary>Resolves a material name (e.g. "wall01.mat") to indexed pixels, or null.</summary>
public delegate IndexedTexture? TextureLookup(string material);

/// <summary>A contiguous run of indices in a <see cref="Mesh"/> that share one material.</summary>
public sealed class Submesh
{
    public string Material { get; init; } = string.Empty;
    public int IndexOffset { get; init; }
    public int IndexCount { get; init; }
    /// <summary>Drawn in a separate alpha-blended pass after opaque geometry.</summary>
    public bool Translucent { get; init; }
}

/// <summary>
/// A level's geometry batched for drawing: a single vertex/index buffer plus a
/// list of per-material index ranges. The renderer binds each material's texture
/// and issues one indexed draw per submesh.
/// </summary>
public sealed class RenderScene
{
    public Mesh Mesh { get; } = new();
    public List<Submesh> Submeshes { get; } = new();
}
