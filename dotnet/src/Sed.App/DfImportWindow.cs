using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Sed.App;

/// <summary>
/// The Dark Forces import dialog, mirroring the original's U_DFI: a scaling
/// factor (the dialog default 40, the code constant 35) and the texture
/// handling choice (everything DFLT.MAT, or keep the .lev texture names).
/// The level's sibling <c>.O</c> object file is imported too, and the df2jk.lst
/// logic table is loaded from the game install when present.
/// </summary>
public sealed class DfImportWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TextBox _scale;
    private readonly RadioButton _useDefaultMaterial;
    private readonly RadioButton _keepTextureNames;

    public string? LevPath { get; private set; }
    public double ScaleFactor => double.TryParse(_scale.Text, out var v) && v > 0 ? v : 35;
    public bool KeepTextureNames => _keepTextureNames.IsChecked == true;

    public DfImportWindow(AppSettings settings)
    {
        _settings = settings;

        Title = "Import Dark Forces Level";
        Width = 440;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _scale = InspectorPanel.TextField("40", _ => { });
        _scale.Width = 90;

        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 4) };
        scaleRow.Children.Add(new TextBlock
        {
            Text = "Scaling factor",
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        scaleRow.Children.Add(_scale);

        _useDefaultMaterial = new RadioButton { Content = "Set to DFLT.MAT", IsChecked = true, Margin = new Thickness(12, 6, 0, 0) };
        _keepTextureNames = new RadioButton { Content = "Keep texture names", Margin = new Thickness(12, 2, 0, 0) };

        var textures = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        textures.Children.Add(new TextBlock
        {
            Text = "Textures",
            Margin = new Thickness(12, 0, 0, 0),
            FontSize = 12,
            Foreground = Brushes.Gray,
        });
        textures.Children.Add(_useDefaultMaterial);
        textures.Children.Add(_keepTextureNames);

        var hint = new TextBlock
        {
            Text = "Imports a Dark Forces .lev level (and its .O object file) as a " +
                   "Jedi Knight level. Geometry, wall textures, lights and objects " +
                   "are converted; the df2jk.lst logic table is read from the " +
                   "game's data folder when present.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(12, 8, 12, 0),
        };

        var import = new Button { Content = "Import…", Margin = new Thickness(0, 0, 6, 0) };
        import.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a Dark Forces level (.lev)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Dark Forces level (*.lev)") { Patterns = new[] { "*.lev" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.FirstOrDefault() is { } file)
            {
                LevPath = file.Path.LocalPath;
                Close();
            }
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(import);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var stack = new StackPanel();
        stack.Children.Add(scaleRow);
        stack.Children.Add(textures);
        stack.Children.Add(hint);
        root.Children.Add(stack);
        Content = root;
    }
}
