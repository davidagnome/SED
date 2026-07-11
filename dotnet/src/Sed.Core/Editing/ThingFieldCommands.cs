using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

public sealed class SetThingNameCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly string _newName;
    private readonly string _oldName;

    public SetThingNameCommand(Thing thing, string name)
    {
        _thing = thing;
        _newName = name;
        _oldName = thing.Name;
    }

    public string Name => $"Rename {(_oldName.Length > 0 ? _oldName : "thing")}";
    public void Apply() => _thing.Name = _newName;
    public void Revert() => _thing.Name = _oldName;
}

public sealed class SetThingTemplateCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly string _newTemplate;
    private readonly string _oldTemplate;

    public SetThingTemplateCommand(Thing thing, string template)
    {
        _thing = thing;
        _newTemplate = template;
        _oldTemplate = thing.Template;
    }

    public string Name => "Set thing template";
    public void Apply() => _thing.Template = _newTemplate;
    public void Revert() => _thing.Template = _oldTemplate;
}

public sealed class SetThingPositionCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly Vec3 _newPos;
    private readonly Vec3 _oldPos;

    public SetThingPositionCommand(Thing thing, Vec3 position)
    {
        _thing = thing;
        _newPos = position;
        _oldPos = thing.Position;
    }

    public string Name => "Move thing";
    public void Apply() => _thing.Position = _newPos;
    public void Revert() => _thing.Position = _oldPos;
}

public sealed class SetThingOrientationCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly double _newPitch, _newYaw, _newRoll;
    private readonly double _oldPitch, _oldYaw, _oldRoll;

    public SetThingOrientationCommand(Thing thing, double pitch, double yaw, double roll)
    {
        _thing = thing;
        _newPitch = pitch; _newYaw = yaw; _newRoll = roll;
        _oldPitch = thing.Pitch; _oldYaw = thing.Yaw; _oldRoll = thing.Roll;
    }

    public string Name => "Rotate thing";
    public void Apply()
    {
        _thing.Pitch = _newPitch;
        _thing.Yaw = _newYaw;
        _thing.Roll = _newRoll;
    }
    public void Revert()
    {
        _thing.Pitch = _oldPitch;
        _thing.Yaw = _oldYaw;
        _thing.Roll = _oldRoll;
    }
}

public sealed class SetThingLayerCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly int _newLayer;
    private readonly int _oldLayer;

    public SetThingLayerCommand(Thing thing, int layer)
    {
        _thing = thing;
        _newLayer = layer;
        _oldLayer = thing.Layer;
    }

    public string Name => "Set thing layer";
    public void Apply() => _thing.Layer = _newLayer;
    public void Revert() => _thing.Layer = _oldLayer;
}

public sealed class SetThingSectorCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly Sector? _newSector;
    private readonly Sector? _oldSector;

    public SetThingSectorCommand(Thing thing, Sector? sector)
    {
        _thing = thing;
        _newSector = sector;
        _oldSector = thing.Sector;
    }

    public string Name => "Set thing sector";
    public void Apply() => _thing.Sector = _newSector;
    public void Revert() => _thing.Sector = _oldSector;
}

public sealed class SetThingValueCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly string _key;
    private readonly string _newValue;
    private readonly string? _oldValue;
    private readonly bool _hadKey;

    public SetThingValueCommand(Thing thing, string key, string value)
    {
        _thing = thing;
        _key = key;
        _newValue = value;
        _hadKey = thing.Values.ContainsKey(key);
        _oldValue = _hadKey ? thing.Values[key] : null;
    }

    public string Name => $"Set {_key}";

    public void Apply() => _thing.Values[_key] = _newValue;

    public void Revert()
    {
        if (_hadKey)
            _thing.Values[_key] = _oldValue!;
        else
            _thing.Values.Remove(_key);
    }
}
