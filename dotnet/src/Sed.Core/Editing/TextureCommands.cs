using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Shifts (offsets) every corner UV by a constant delta. Reversible.</summary>
public sealed class ShiftTextureCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly double _du, _dv;
    private readonly List<TexVertex> _old = new();

    public ShiftTextureCommand(Surface surface, double du, double dv) { _surface = surface; _du = du; _dv = dv; }

    public string Name => "Shift texture";

    public void Apply()
    {
        _old.Clear();
        foreach (var c in _surface.Corners)
        {
            _old.Add(c.Uv);
            c.Uv = new TexVertex(c.Uv.U + _du, c.Uv.V + _dv);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _surface.Corners.Count; i++)
            _surface.Corners[i].Uv = _old[i];
    }
}

/// <summary>Scales UVs about a pivot UV point (first corner). Reversible.</summary>
public sealed class ScaleTextureCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly double _su, _sv;
    private readonly List<TexVertex> _old = new();

    public ScaleTextureCommand(Surface surface, double su, double sv) { _surface = surface; _su = su; _sv = sv; }

    public string Name => "Scale texture";

    public void Apply()
    {
        _old.Clear();
        double pivotU = _surface.Corners[0].Uv.U;
        double pivotV = _surface.Corners[0].Uv.V;
        foreach (var c in _surface.Corners)
        {
            _old.Add(c.Uv);
            c.Uv = new TexVertex(
                pivotU + (c.Uv.U - pivotU) * _su,
                pivotV + (c.Uv.V - pivotV) * _sv);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _surface.Corners.Count; i++)
            _surface.Corners[i].Uv = _old[i];
    }
}

/// <summary>Rotates UVs by an angle (degrees) about a pivot UV point (first corner). Reversible.</summary>
public sealed class RotateTextureCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly double _angleDegrees;
    private readonly List<TexVertex> _old = new();

    public RotateTextureCommand(Surface surface, double angleDegrees) { _surface = surface; _angleDegrees = angleDegrees; }

    public string Name => "Rotate texture";

    public void Apply()
    {
        _old.Clear();
        double rad = _angleDegrees * System.Math.PI / 180.0;
        double cos = System.Math.Cos(rad), sin = System.Math.Sin(rad);
        double pivotU = _surface.Corners[0].Uv.U;
        double pivotV = _surface.Corners[0].Uv.V;
        foreach (var c in _surface.Corners)
        {
            _old.Add(c.Uv);
            double du = c.Uv.U - pivotU;
            double dv = c.Uv.V - pivotV;
            c.Uv = new TexVertex(
                pivotU + du * cos - dv * sin,
                pivotV + du * sin + dv * cos);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _surface.Corners.Count; i++)
            _surface.Corners[i].Uv = _old[i];
    }
}

/// <summary>Auto-fits UVs to the surface's bounding box (maps to 0..width, 0..height texels).</summary>
public sealed class AutoTextureCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly int _textureWidth, _textureHeight;
    private readonly List<TexVertex> _old = new();

    public AutoTextureCommand(Surface surface, int textureWidth = 64, int textureHeight = 64)
    {
        _surface = surface; _textureWidth = textureWidth; _textureHeight = textureHeight;
    }

    public string Name => "Auto-fit texture";

    public void Apply()
    {
        _old.Clear();
        foreach (var c in _surface.Corners)
            _old.Add(c.Uv);

        // Project onto the two world axes least aligned with the surface normal.
        var n = _surface.Normal;
        double ax = System.Math.Abs(n.X), ay = System.Math.Abs(n.Y), az = System.Math.Abs(n.Z);
        Func<Vec3, double> projU, projV;
        if (az >= ay && az >= ax)       // normal mostly Z → use X/Y
        {
            projU = p => p.X; projV = p => p.Y;
        }
        else if (ay >= ax)              // normal mostly Y → use X/Z
        {
            projU = p => p.X; projV = p => p.Z;
        }
        else                            // normal mostly X → use Y/Z
        {
            projU = p => p.Y; projV = p => p.Z;
        }

        double minU = double.PositiveInfinity, minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity, maxV = double.NegativeInfinity;
        foreach (var c in _surface.Corners)
        {
            double pu = projU(c.Vertex.Position);
            double pv = projV(c.Vertex.Position);
            minU = System.Math.Min(minU, pu);
            maxU = System.Math.Max(maxU, pu);
            minV = System.Math.Min(minV, pv);
            maxV = System.Math.Max(maxV, pv);
        }

        double rangeU = maxU - minU;
        double rangeV = maxV - minV;
        if (rangeU == 0) rangeU = 1;
        if (rangeV == 0) rangeV = 1;

        for (int i = 0; i < _surface.Corners.Count; i++)
        {
            double pu = projU(_surface.Corners[i].Vertex.Position);
            double pv = projV(_surface.Corners[i].Vertex.Position);
            _surface.Corners[i].Uv = new TexVertex(
                (pu - minU) / rangeU * _textureWidth,
                (pv - minV) / rangeV * _textureHeight);
        }
    }

    public void Revert()
    {
        for (int i = 0; i < _surface.Corners.Count; i++)
            _surface.Corners[i].Uv = _old[i];
    }
}
