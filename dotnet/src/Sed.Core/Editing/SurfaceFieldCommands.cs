using Sed.Core.Model;

namespace Sed.Core.Editing;

public sealed class SetSurfaceFlagsCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly long _newFlags, _oldFlags;

    public SetSurfaceFlagsCommand(Surface surface, long flags) { _surface = surface; _newFlags = flags; _oldFlags = surface.SurfFlags; }

    public string Name => $"Set surf flags 0x{_newFlags:x}";
    public void Apply() => _surface.SurfFlags = _newFlags;
    public void Revert() => _surface.SurfFlags = _oldFlags;
}

public sealed class SetFaceFlagsCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly long _newFlags, _oldFlags;

    public SetFaceFlagsCommand(Surface surface, long flags) { _surface = surface; _newFlags = flags; _oldFlags = surface.FaceFlags; }

    public string Name => $"Set face flags 0x{_newFlags:x}";
    public void Apply() => _surface.FaceFlags = _newFlags;
    public void Revert() => _surface.FaceFlags = _oldFlags;
}

public sealed class SetSurfaceGeoCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly int _newGeo, _oldGeo;

    public SetSurfaceGeoCommand(Surface surface, int geo) { _surface = surface; _newGeo = geo; _oldGeo = surface.Geo; }

    public string Name => $"Set geo {_newGeo}";
    public void Apply() => _surface.Geo = _newGeo;
    public void Revert() => _surface.Geo = _oldGeo;
}

public sealed class SetSurfaceLightModeCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly int _newMode, _oldMode;

    public SetSurfaceLightModeCommand(Surface surface, int mode) { _surface = surface; _newMode = mode; _oldMode = surface.Light; }

    public string Name => $"Set light mode {_newMode}";
    public void Apply() => _surface.Light = _newMode;
    public void Revert() => _surface.Light = _oldMode;
}

public sealed class SetSurfaceTexModeCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly int _newMode, _oldMode;

    public SetSurfaceTexModeCommand(Surface surface, int mode) { _surface = surface; _newMode = mode; _oldMode = surface.Tex; }

    public string Name => $"Set tex mode {_newMode}";
    public void Apply() => _surface.Tex = _newMode;
    public void Revert() => _surface.Tex = _oldMode;
}

public sealed class SetSurfaceExtraLightCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly float _newValue, _oldValue;

    public SetSurfaceExtraLightCommand(Surface surface, float value) { _surface = surface; _newValue = value; _oldValue = surface.ExtraLightIntensity; }

    public string Name => $"Set extra light {_newValue:0.###}";
    public void Apply() => _surface.ExtraLightIntensity = _newValue;
    public void Revert() => _surface.ExtraLightIntensity = _oldValue;
}

public sealed class SetSurfaceScaleCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly float _newU, _oldU, _newV, _oldV;

    public SetSurfaceScaleCommand(Surface surface, float uScale, float vScale)
    {
        _surface = surface;
        _newU = uScale; _oldU = surface.UScale;
        _newV = vScale; _oldV = surface.VScale;
    }

    public string Name => $"Set scale {_newU:0.###}, {_newV:0.###}";
    public void Apply() { _surface.UScale = _newU; _surface.VScale = _newV; }
    public void Revert() { _surface.UScale = _oldU; _surface.VScale = _oldV; }
}

public sealed class SetSurfaceAdjoinFlagsCommand : IEditCommand
{
    private readonly Surface _surface;
    private readonly long _newFlags, _oldFlags;

    public SetSurfaceAdjoinFlagsCommand(Surface surface, long flags) { _surface = surface; _newFlags = flags; _oldFlags = surface.AdjoinFlags; }

    public string Name => $"Set adjoin flags 0x{_newFlags:x}";
    public void Apply() => _surface.AdjoinFlags = _newFlags;
    public void Revert() => _surface.AdjoinFlags = _oldFlags;
}
