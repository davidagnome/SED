using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Sed.App;

/// <summary>
/// Configures "Save and Test" (mirror of the original's ProjectDir + test batch):
/// where the test GOB is written, and — on macOS/Linux — the command that launches
/// the game. Placeholders: {project} {gob} {game} {gameexe} {levelname}. Inside
/// Wine/CrossOver, mac paths are Z:\&lt;mac path&gt;.
/// </summary>
public sealed class TestSetupWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TextBox _projectDir;
    private readonly TextBox _command;

    public TestSetupWindow(AppSettings settings)
    {
        _settings = settings;

        Title = "Test Setup";
        Width = 640;
        Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _projectDir = InspectorPanel.TextField(settings.ProjectDir ?? string.Empty, _ => { });
        _projectDir.Width = 420;

        var browse = new Button { Content = "Browse…" };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the project folder (game's -path)",
                AllowMultiple = false,
            });
            if (folders.FirstOrDefault() is { } folder)
                _projectDir.Text = folder.Path.LocalPath;
        };

        var projectRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 4) };
        projectRow.Children.Add(new TextBlock
        {
            Text = "Project folder",
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        projectRow.Children.Add(_projectDir);
        projectRow.Children.Add(browse);

        var projectHint = new TextBlock
        {
            Text = "Where the test GOB is written. The game must be able to read this directory — " +
                   "the original pointed the engine's -path at it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(12, 0, 12, 8),
        };

        _command = InspectorPanel.TextField(settings.TestCommand ?? string.Empty, _ => { });
        _command.Width = 500;

        var commandRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 4) };
        commandRow.Children.Add(new TextBlock
        {
            Text = "Launch command",
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        commandRow.Children.Add(_command);

        var commandHint = new TextBlock
        {
            Text = "Windows: empty uses the original's generated test batch. macOS/Linux: a shell command " +
                   "template. Placeholders: {project} {gob} {game} {gameexe} {levelname}. Example (Wine):\n" +
                   "wine \"{gameexe}\" -devmode -dispstats -debug log -displayconfig -path \"{project}\"\n" +
                   "Inside Wine the paths resolve as Z:\\<mac path>.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(12, 0, 12, 8),
        };

        var save = new Button { Content = "Save", Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) =>
        {
            var project = _projectDir.Text?.Trim() ?? string.Empty;
            var command = _command.Text?.Trim() ?? string.Empty;
            _settings.ProjectDir = project.Length > 0 ? project : null;
            _settings.TestCommand = command.Length > 0 ? command : null;
            _settings.Save();
            Close();
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var stack = new StackPanel();
        stack.Children.Add(projectRow);
        stack.Children.Add(projectHint);
        stack.Children.Add(commandRow);
        stack.Children.Add(commandHint);
        root.Children.Add(stack);
        Content = root;
    }
}
