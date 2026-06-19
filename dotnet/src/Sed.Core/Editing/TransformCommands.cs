using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Rotates a thing about its up axis (yaw) by a delta; reversible.</summary>
public sealed class RotateThingCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly double _deltaYaw;

    public RotateThingCommand(Thing thing, double deltaYawDegrees) { _thing = thing; _deltaYaw = deltaYawDegrees; }

    public string Name => "Rotate thing";
    public void Apply() => _thing.Yaw += _deltaYaw;
    public void Revert() => _thing.Yaw -= _deltaYaw;
}

/// <summary>
/// Applies an arbitrary positional transform to a set of vertices (rotate/scale
/// about a pivot), capturing originals so it is exactly reversible.
/// </summary>
public sealed class TransformVerticesCommand : IEditCommand
{
    private readonly Vertex[] _verts;
    private readonly Vec3[] _orig;
    private readonly Vec3[] _new;

    public TransformVerticesCommand(IEnumerable<Vertex> vertices, Func<Vec3, Vec3> transform, string name)
    {
        _verts = vertices.Distinct().ToArray();
        _orig = new Vec3[_verts.Length];
        _new = new Vec3[_verts.Length];
        for (int i = 0; i < _verts.Length; i++)
        {
            _orig[i] = _verts[i].Position;
            _new[i] = transform(_orig[i]);
        }
        Name = name;
    }

    public string Name { get; }
    public void Apply() { for (int i = 0; i < _verts.Length; i++) _verts[i].Position = _new[i]; }
    public void Revert() { for (int i = 0; i < _verts.Length; i++) _verts[i].Position = _orig[i]; }

    /// <summary>Rotation about <paramref name="pivot"/> around +Z (yaw).</summary>
    public static Func<Vec3, Vec3> RotateZ(Vec3 pivot, double radians)
    {
        double c = System.Math.Cos(radians), s = System.Math.Sin(radians);
        return p =>
        {
            var d = p - pivot;
            return new Vec3(pivot.X + d.X * c - d.Y * s, pivot.Y + d.X * s + d.Y * c, p.Z);
        };
    }

    /// <summary>Uniform scale about <paramref name="pivot"/>.</summary>
    public static Func<Vec3, Vec3> Scale(Vec3 pivot, double factor) =>
        p => pivot + (p - pivot) * factor;
}

/// <summary>Changes a surface's material; reversible.</summary>
public sealed class SetMaterialCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly string _newMaterial, _oldMaterial;
    private readonly int _newIndex, _oldIndex;

    public SetMaterialCommand(Surface surface, string material, int materialIndex)
    {
        _surface = surface;
        _newMaterial = material; _newIndex = materialIndex;
        _oldMaterial = surface.Material; _oldIndex = surface.MaterialIndex;
    }

    public string Name => $"Set material {_newMaterial}";
    public void Apply() { _surface.Material = _newMaterial; _surface.MaterialIndex = _newIndex; }
    public void Revert() { _surface.Material = _oldMaterial; _surface.MaterialIndex = _oldIndex; }
}
