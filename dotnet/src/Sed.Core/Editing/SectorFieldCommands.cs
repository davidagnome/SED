using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

public sealed class SetSectorFlagsCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly long _old, _new;

    public SetSectorFlagsCommand(Sector sector, long flags)
    {
        _sector = sector; _old = sector.Flags; _new = flags;
    }

    public string Name => "Set sector flags";
    public void Apply() => _sector.Flags = _new;
    public void Revert() => _sector.Flags = _old;
}

public sealed class SetSectorExtraLightCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly ColorF _old, _new;

    public SetSectorExtraLightCommand(Sector sector, ColorF extraLight)
    {
        _sector = sector; _old = sector.ExtraLight; _new = extraLight;
    }

    public string Name => "Set sector extra light";
    public void Apply() => _sector.ExtraLight = _new;
    public void Revert() => _sector.ExtraLight = _old;
}

public sealed class SetSectorTintCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly ColorF _old, _new;

    public SetSectorTintCommand(Sector sector, ColorF tint)
    {
        _sector = sector; _old = sector.Tint; _new = tint;
    }

    public string Name => "Set sector tint";
    public void Apply() => _sector.Tint = _new;
    public void Revert() => _sector.Tint = _old;
}

public sealed class SetSectorColormapCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly string _old, _new;

    public SetSectorColormapCommand(Sector sector, string colormap)
    {
        _sector = sector; _old = sector.ColorMap; _new = colormap;
    }

    public string Name => "Set sector colormap";
    public void Apply() => _sector.ColorMap = _new;
    public void Revert() => _sector.ColorMap = _old;
}

public sealed class SetSectorSoundCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly string _oldSound;
    private readonly double _oldVolume;
    private readonly string _newSound;
    private readonly double _newVolume;

    public SetSectorSoundCommand(Sector sector, string sound, double volume)
    {
        _sector = sector;
        _oldSound = sector.Sound;
        _oldVolume = sector.SoundVolume;
        _newSound = sound;
        _newVolume = volume;
    }

    public string Name => "Set sector sound";
    public void Apply() { _sector.Sound = _newSound; _sector.SoundVolume = _newVolume; }
    public void Revert() { _sector.Sound = _oldSound; _sector.SoundVolume = _oldVolume; }
}

public sealed class SetSectorLayerCommand : IEditCommand
{
    private readonly Sector _sector;
    private readonly int _old, _new;

    public SetSectorLayerCommand(Sector sector, int layer)
    {
        _sector = sector; _old = sector.Layer; _new = layer;
    }

    public string Name => "Set sector layer";
    public void Apply() => _sector.Layer = _new;
    public void Revert() => _sector.Layer = _old;
}
