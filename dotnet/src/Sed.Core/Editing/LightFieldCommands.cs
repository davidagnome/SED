using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

public sealed class SetLightFlagsCommand : IEditCommand
{
    private readonly Light _light;
    private readonly long _old, _new;

    public SetLightFlagsCommand(Light light, long flags)
    {
        _light = light; _old = light.Flags; _new = flags;
    }

    public string Name => "Set light flags";
    public void Apply() => _light.Flags = _new;
    public void Revert() => _light.Flags = _old;
}

public sealed class SetLightRangeCommand : IEditCommand
{
    private readonly Light _light;
    private readonly double _old, _new;

    public SetLightRangeCommand(Light light, double range)
    {
        _light = light; _old = light.Range; _new = range;
    }

    public string Name => "Set light range";
    public void Apply() => _light.Range = _new;
    public void Revert() => _light.Range = _old;
}

public sealed class SetLightIntensityCommand : IEditCommand
{
    private readonly Light _light;
    private readonly double _old, _new;

    public SetLightIntensityCommand(Light light, double intensity)
    {
        _light = light; _old = light.Intensity; _new = intensity;
    }

    public string Name => "Set light intensity";
    public void Apply() => _light.Intensity = _new;
    public void Revert() => _light.Intensity = _old;
}

public sealed class SetLightColorCommand : IEditCommand
{
    private readonly Light _light;
    private readonly ColorF _old, _new;

    public SetLightColorCommand(Light light, ColorF color)
    {
        _light = light; _old = light.Color; _new = color;
    }

    public string Name => "Set light color";
    public void Apply() => _light.Color = _new;
    public void Revert() => _light.Color = _old;
}

public sealed class SetLightPositionCommand : IEditCommand
{
    private readonly Light _light;
    private readonly Vec3 _old, _new;

    public SetLightPositionCommand(Light light, Vec3 position)
    {
        _light = light; _old = light.Position; _new = position;
    }

    public string Name => "Set light position";
    public void Apply() => _light.Position = _new;
    public void Revert() => _light.Position = _old;
}

public sealed class SetLightLayerCommand : IEditCommand
{
    private readonly Light _light;
    private readonly int _old, _new;

    public SetLightLayerCommand(Light light, int layer)
    {
        _light = light; _old = light.Layer; _new = layer;
    }

    public string Name => "Set light layer";
    public void Apply() => _light.Layer = _new;
    public void Revert() => _light.Layer = _old;
}
