using Sed.Core.Editing;
using Sed.Core.Model;

namespace Sed.Plugins;

/// <summary>
/// What a plugin command can reach. Unlike the original's COM shim — which had to
/// expose ~100 accessor methods because a Delphi DLL could not share an object
/// graph with the host — a managed plugin gets the real model and the real undo
/// stack.
/// </summary>
public sealed class PluginContext
{
    private readonly Action<string> _report;

    public PluginContext(Level level, EditHistory history, SelectionSet selection, Action<string> report)
    {
        Level = level;
        History = history;
        Selection = selection;
        _report = report;
    }

    /// <summary>The level being edited.</summary>
    public Level Level { get; }

    /// <summary>
    /// The editor's undo stack. Plugins must push changes through this rather
    /// than mutating the model directly, so their edits are undoable like any
    /// other and the views refresh.
    /// </summary>
    public EditHistory History { get; }

    /// <summary>The current selection, shared with both views.</summary>
    public SelectionSet Selection { get; }

    /// <summary>Shows a message in the editor's status bar.</summary>
    public void Report(string message) => _report(message);
}

/// <summary>One entry a plugin contributes to the Plugins menu.</summary>
public sealed record PluginCommand(string Label, Action<PluginContext> Execute)
{
    /// <summary>Optional tooltip / longer description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Implement this and drop the assembly in the editor's <c>plugins</c> folder.
/// The type must have a public parameterless constructor.
/// </summary>
public interface ISedPlugin
{
    string Name { get; }

    string? Description => null;

    /// <summary>The commands this plugin adds to the Plugins menu.</summary>
    IEnumerable<PluginCommand> GetCommands();
}
