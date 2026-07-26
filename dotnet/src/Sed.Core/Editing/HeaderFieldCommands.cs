using Sed.Core.Model;

namespace Sed.Core.Editing;

/// <summary>
/// Sets one field of the level header, reversibly. The header has ~20 scalar
/// fields (gravity, two sky descriptions, two distance arrays, fog); a single
/// generic setter parameterised by a getter/setter pair keeps that from becoming
/// twenty near-identical command classes.
/// </summary>
public sealed class SetHeaderFieldCommand<T> : IEditCommand
{
    private readonly LevelHeader _header;
    private readonly Action<LevelHeader, T> _set;
    private readonly T _new;
    private readonly T _old;

    public SetHeaderFieldCommand(LevelHeader header, string name, T value,
        Func<LevelHeader, T> get, Action<LevelHeader, T> set)
    {
        _header = header;
        Name = $"Set {name}";
        _new = value;
        _set = set;
        _old = get(header);
    }

    public string Name { get; }
    public void Apply() => _set(_header, _new);
    public void Revert() => _set(_header, _old);
}

/// <summary>Factory shorthand so call sites can rely on type inference.</summary>
public static class HeaderField
{
    public static IEditCommand Set<T>(LevelHeader header, string name, T value,
        Func<LevelHeader, T> get, Action<LevelHeader, T> set) =>
        new SetHeaderFieldCommand<T>(header, name, value, get, set);
}
