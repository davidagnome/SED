namespace Sed.Rendering;

/// <summary>RGBA8 texture pixels supplied by a <see cref="TextureLookup"/>.</summary>
public readonly record struct TextureData(int Width, int Height, byte[] Rgba);

/// <summary>Resolves a material name (e.g. "wall01.mat") to pixels, or null if unavailable.</summary>
public delegate TextureData? TextureLookup(string material);

/// <summary>A contiguous run of indices in a <see cref="Mesh"/> that share one material.</summary>
public sealed class Submesh
{
    public string Material { get; init; } = string.Empty;
    public int IndexOffset { get; init; }
    public int IndexCount { get; init; }
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
