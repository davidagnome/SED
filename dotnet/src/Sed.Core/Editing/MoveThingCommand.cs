using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>Translates a thing by a fixed delta; reversible.</summary>
public sealed class MoveThingCommand : IEditCommand
{
    private readonly Thing _thing;
    private readonly Vec3 _delta;

    public MoveThingCommand(Thing thing, Vec3 delta)
    {
        _thing = thing;
        _delta = delta;
    }

    public string Name => $"Move {(_thing.Name.Length > 0 ? _thing.Name : "thing")}";

    public void Apply() => _thing.Position += _delta;
    public void Revert() => _thing.Position -= _delta;
}
