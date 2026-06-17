using Sed.Core.Math;

namespace Sed.Formats.ThreeDo;

/// <summary>A polygon face within a 3DO mesh: material index + per-corner vertex/UV indices.</summary>
public sealed class Face3do
{
    public int Material = -1;
    public List<int> VertexIndices { get; } = new();
    public List<int> UvIndices { get; } = new();
}

/// <summary>One mesh of a 3DO geoset: local-space vertices, texture vertices, and faces.</summary>
public sealed class Mesh3do
{
    public string Name = string.Empty;
    public List<Vec3> Vertices { get; } = new();
    public List<Vec2> Uvs { get; } = new();
    public List<Face3do> Faces { get; } = new();
}

/// <summary>A hierarchy node: which mesh it draws and its rest transform relative to its parent.</summary>
public sealed class HierarchyNode
{
    public int Mesh = -1;
    public int Parent = -1;
    public Vec3 Offset;
    public double Pitch, Yaw, Roll;
}

/// <summary>
/// A parsed Sith-engine 3DO model (highest-detail geoset). Materials are MAT
/// names resolved against the resource GOBs; the hierarchy positions meshes.
/// </summary>
public sealed class ThreeDoModel
{
    public double Version { get; set; }
    public Vec3 InsertOffset { get; set; }
    public List<string> Materials { get; } = new();
    public List<Mesh3do> Meshes { get; } = new();
    public List<HierarchyNode> Nodes { get; } = new();

    public string GetMaterial(int index) =>
        (uint)index < (uint)Materials.Count ? Materials[index] : string.Empty;
}
