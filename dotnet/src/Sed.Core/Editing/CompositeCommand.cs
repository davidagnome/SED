namespace Sed.Core.Editing;

/// <summary>
/// Groups several commands into one undo step — moving a multi-selection of five
/// things should be one Ctrl+Z, not five. Applies in order and reverts in
/// reverse order, so commands that depend on each other unwind correctly.
/// </summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly IEditCommand[] _commands;

    public CompositeCommand(string name, IEnumerable<IEditCommand> commands)
    {
        Name = name;
        _commands = commands.ToArray();
    }

    public string Name { get; }

    /// <summary>Number of sub-commands; zero means applying is a no-op.</summary>
    public int Count => _commands.Length;

    public void Apply()
    {
        foreach (var c in _commands) c.Apply();
    }

    public void Revert()
    {
        for (int i = _commands.Length - 1; i >= 0; i--) _commands[i].Revert();
    }
}
