using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Sets a vertex's absolute position (reversible).</summary>
public sealed class SetVertexPositionCommand : IEditCommand
{
    private readonly Vertex _vertex;
    private readonly Vec3 _newPos;
    private Vec3 _oldPos;

    public SetVertexPositionCommand(Vertex vertex, Vec3 newPos)
    {
        _vertex = vertex;
        _newPos = newPos;
    }

    public string Name => "Set vertex position";
    public void Apply() { _oldPos = _vertex.Position; _vertex.Position = _newPos; }
    public void Revert() { _vertex.Position = _oldPos; }
}
