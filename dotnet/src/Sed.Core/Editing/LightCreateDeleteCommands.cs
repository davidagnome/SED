using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Adds a light to the level; reversible.</summary>
public sealed class CreateLightCommand : IEditCommand
{
    private readonly Level _level;

    public Light Light { get; }

    public CreateLightCommand(Level level, Light light)
    {
        _level = level;
        Light = light;
    }

    public string Name => "Create light";

    public void Apply()
    {
        _level.Lights.Add(Light);
        _level.RenumberLights();
    }

    public void Revert()
    {
        _level.Lights.Remove(Light);
        _level.RenumberLights();
    }
}

/// <summary>
/// Removes a light, remembering its position in the list so undo puts it back
/// where it was rather than at the end (light numbers are referenced by COGs).
/// </summary>
public sealed class DeleteLightCommand : IEditCommand
{
    private readonly Level _level;
    private readonly Light _light;
    private int _index = -1;

    public DeleteLightCommand(Level level, Light light)
    {
        _level = level;
        _light = light;
    }

    public string Name => "Delete light";

    public void Apply()
    {
        _index = _level.Lights.IndexOf(_light);
        if (_index < 0) return;
        _level.Lights.RemoveAt(_index);
        _level.RenumberLights();
    }

    public void Revert()
    {
        if (_index < 0) return;
        _level.Lights.Insert(System.Math.Min(_index, _level.Lights.Count), _light);
        _level.RenumberLights();
    }
}

/// <summary>
/// Moves a light by a delta. Distinct from <see cref="SetLightPositionCommand"/>,
/// which sets an absolute position from the inspector — a delta composes cleanly
/// when several lights move together.
/// </summary>
public sealed class MoveLightCommand : IEditCommand
{
    private readonly Light _light;
    private readonly Vec3 _delta;

    public MoveLightCommand(Light light, Vec3 delta)
    {
        _light = light;
        _delta = delta;
    }

    public string Name => "Move light";
    public void Apply() => _light.Position += _delta;
    public void Revert() => _light.Position -= _delta;
}
