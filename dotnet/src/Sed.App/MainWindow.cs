using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sed.Formats.Gob;
using Sed.Formats.Jkl;

namespace Sed.App;

/// <summary>
/// The editor shell: menu, a level/sector side panel, the Vulkan 3D viewport,
/// and a status bar. Opens loose .jkl files or .gob archives (Jedi Knight's
/// container), listing the archive's levels in the side panel.
/// </summary>
public class MainWindow : Window
{
    private readonly TextBlock _status;
    private readonly VulkanView _view;
    private readonly ListBox _levelList;
    private readonly TextBlock _sidePanelHeader;

    private GobArchive? _gob;
    private List<GobEntry> _gobLevels = new();

    public MainWindow()
    {
        Title = "SED — Sith Engine Editor (.NET / Vulkan)";
        Width = 1100;
        Height = 720;

        _status = new TextBlock
        {
            Margin = new Thickness(8, 4),
            Text = "Vulkan viewport (MoltenVK) — drag to orbit, wheel to zoom. File ▸ Open a .jkl or .gob",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _view = new VulkanView(Sed.Rendering.SampleScene.CreateCube());

        _sidePanelHeader = new TextBlock
        {
            Margin = new Thickness(10, 8),
            Text = "No archive open",
            Foreground = Brushes.Gray,
        };
        _levelList = new ListBox { Background = Brushes.Transparent };
        _levelList.SelectionChanged += (_, _) => OnLevelSelected();

        var root = new DockPanel();

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2b)),
            Child = _status,
            Height = 28,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        var sideContent = new DockPanel { Width = 240 };
        DockPanel.SetDock(_sidePanelHeader, Dock.Top);
        sideContent.Children.Add(_sidePanelHeader);
        sideContent.Children.Add(_levelList);

        var sidePanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)),
            Child = sideContent,
        };
        DockPanel.SetDock(sidePanel, Dock.Left);
        root.Children.Add(sidePanel);

        root.Children.Add(_view);
        Content = root;
    }

    private async void OpenLevel()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open level or archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Jedi Knight files (*.jkl, *.gob)")
                    { Patterns = new[] { "*.jkl", "*.gob", "*.goo" } },
                FilePickerFileTypes.All,
            },
        });

        var file = files.FirstOrDefault();
        if (file is null) return;
        var path = file.Path.LocalPath;

        try
        {
            if (path.EndsWith(".gob", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".goo", StringComparison.OrdinalIgnoreCase))
                OpenArchive(path);
            else
                LoadLevel(JklParser.ParseFile(path), file.Name);
        }
        catch (Exception ex)
        {
            _status.Text = $"Failed to open {file.Name}: {ex.Message}";
        }
    }

    private void OpenArchive(string path)
    {
        _gob?.Dispose();
        _gob = GobArchive.Open(path);
        _gobLevels = _gob.FindByExtension(".jkl").OrderBy(e => e.NormalizedName).ToList();

        _sidePanelHeader.Text = $"{Path.GetFileName(path)} — {_gobLevels.Count} levels";
        _levelList.ItemsSource = _gobLevels.Select(e => Path.GetFileName(e.NormalizedName)).ToList();

        if (_gobLevels.Count > 0)
            _levelList.SelectedIndex = 0; // triggers OnLevelSelected
        else
            _status.Text = $"{Path.GetFileName(path)} contains no JKL levels";
    }

    private void OnLevelSelected()
    {
        int i = _levelList.SelectedIndex;
        if (_gob is null || (uint)i >= (uint)_gobLevels.Count) return;

        var entry = _gobLevels[i];
        try
        {
            var level = JklParser.Parse(_gob.ReadText(entry));
            LoadLevel(level, Path.GetFileName(entry.NormalizedName));
        }
        catch (Exception ex)
        {
            _status.Text = $"Failed to parse {entry.Name}: {ex.Message}";
        }
    }

    private void LoadLevel(Sed.Core.Model.Level level, string name)
    {
        _view.SetLevel(level);
        int surfaces = level.Sectors.Sum(s => s.Surfaces.Count);
        _status.Text = $"{name} — {level.Sectors.Count} sectors, {surfaces} surfaces, " +
                       $"{level.Things.Count} things ({level.Kind})";
    }

    private Menu BuildMenu()
    {
        var open = new MenuItem { Header = "_Open…" };
        open.Click += (_, _) => OpenLevel();

        var file = new MenuItem { Header = "_File" };
        file.Items.Add(open);
        file.Items.Add(new MenuItem { Header = "_Save", IsEnabled = false });
        file.Items.Add(new Separator());
        var exit = new MenuItem { Header = "E_xit" };
        exit.Click += (_, _) => (Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        file.Items.Add(exit);

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(new MenuItem { Header = "3D Preview" });

        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(new MenuItem { Header = "About SED" });

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(view);
        menu.Items.Add(help);
        return menu;
    }

    protected override void OnClosed(EventArgs e)
    {
        _gob?.Dispose();
        base.OnClosed(e);
    }
}
