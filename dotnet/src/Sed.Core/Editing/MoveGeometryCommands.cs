using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Translates a single sector vertex by a delta; reversible. Affects every
/// surface in the sector that shares the vertex.</summary>
public sealed class MoveVertexCommand : IEditCommand
{
    private readonly Vertex _vertex;
    private readonly Vec3 _delta;

    public MoveVertexCommand(Vertex vertex, Vec3 delta)
    {
        _vertex = vertex;
        _delta = delta;
    }

    public string Name => "Move vertex";
    public void Apply() => _vertex.Position += _delta;
    public void Revert() => _vertex.Position -= _delta;
}

/// <summary>Translates all distinct vertices of a surface by a delta; reversible.</summary>
public sealed class MoveSurfaceCommand : IEditCommand
{
    private readonly List<Vertex> _vertices;
    private readonly Vec3 _delta;

    public MoveSurfaceCommand(Surface surface, Vec3 delta)
    {
        _vertices = surface.Corners.Select(c => c.Vertex).Distinct().ToList();
        _delta = delta;
    }

    public string Name => "Move surface";

    public void Apply()
    {
        foreach (var v in _vertices) v.Position += _delta;
    }

    public void Revert()
    {
        foreach (var v in _vertices) v.Position -= _delta;
    }
}
