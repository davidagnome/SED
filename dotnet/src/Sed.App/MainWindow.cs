using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sed.Core.Model;
using Sed.Formats.Game;
using Sed.Formats.Gob;
using Sed.Formats.Jkl;
using Sed.Formats.Material;
using Sed.Rendering;

namespace Sed.App;

/// <summary>
/// The editor shell. Levels and textures are resolved from a configured game
/// installation directory (Game menu, persisted in <see cref="AppSettings"/>),
/// mirroring the original editor's per-game path options; loose .jkl/.gob files
/// can also be opened directly via File ▸ Open.
/// </summary>
public class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly TextBlock _status;
    private readonly VulkanView _view;
    private readonly ListBox _levelList;
    private readonly TextBlock _sidePanelHeader;

    // Active session (whichever source is open).
    private GameInstall? _install;
    private GobArchive? _adhocGob;
    private GobArchive? _adhocResourceGob;
    private GobArchive? _levelArchive;
    private GobArchive[] _materialArchives = Array.Empty<GobArchive>();
    private List<GobEntry> _levels = new();

    public MainWindow()
    {
        Title = "SED — Sith Engine Editor (.NET / Vulkan)";
        Width = 1100;
        Height = 720;

        _status = new TextBlock
        {
            Margin = new Thickness(8, 4),
            Text = "WASD/QE to fly, drag to look, click to select. Game ▸ Set folder, or File ▸ Open.",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _view = new VulkanView(SampleScene.CreateCube())
        {
            SelectionChanged = desc => _status.Text = desc ?? "Nothing selected",
        };

        _sidePanelHeader = new TextBlock { Margin = new Thickness(10, 8), Text = "No game configured", Foreground = Brushes.Gray };
        _levelList = new ListBox { Background = Brushes.Transparent };
        _levelList.SelectionChanged += (_, _) => OnLevelSelected();

        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2b)), Child = _status, Height = 28,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        var sideContent = new DockPanel { Width = 240 };
        DockPanel.SetDock(_sidePanelHeader, Dock.Top);
        sideContent.Children.Add(_sidePanelHeader);
        sideContent.Children.Add(_levelList);
        var sidePanel = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)), Child = sideContent };
        DockPanel.SetDock(sidePanel, Dock.Left);
        root.Children.Add(sidePanel);

        root.Children.Add(_view);
        Content = root;

        TryAutoOpenConfiguredGame();
    }

    // ---- game install flow ----

    private static readonly (string label, ProjectType game)[] Games =
    {
        ("Dark Forces II / Jedi Knight", ProjectType.JediKnight),
        ("Mysteries of the Sith", ProjectType.MysteriesOfTheSith),
        ("Indiana Jones and the Infernal Machine", ProjectType.InfernalMachine),
    };

    private void TryAutoOpenConfiguredGame()
    {
        foreach (var (_, game) in Games)
        {
            var dir = _settings.DirFor(game);
            if (!string.IsNullOrEmpty(dir) && GameInstall.IsValid(game, dir))
            {
                OpenGame(game);
                return;
            }
        }
    }

    private async void SetGameFolder(ProjectType game)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Select {Games.First(g => g.game == game).label} installation folder",
            AllowMultiple = false,
        });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;

        var dir = folder.Path.LocalPath;
        if (!GameInstall.IsValid(game, dir))
        {
            var (level, resources) = GameInstall.ExpectedFiles(game);
            _status.Text = $"That folder doesn't look like a {game} install " +
                           $"(expected e.g. {string.Join(" / ", level.Concat(resources).Take(2))} under it).";
            return;
        }

        _settings.SetDir(game, dir);
        _settings.Save();
        OpenGame(game);
    }

    private void OpenGame(ProjectType game)
    {
        var dir = _settings.DirFor(game);
        if (string.IsNullOrEmpty(dir)) { _status.Text = $"No folder configured for {game}."; return; }

        try
        {
            var install = GameInstall.TryOpen(game, dir);
            if (install is null) { _status.Text = $"No game archives found under {dir}."; return; }

            ClearSession();
            _install = install;
            _levelArchive = install.LevelArchive;
            _materialArchives = install.ResourceArchives.ToArray();
            PopulateLevels($"{game} — {Path.GetFileName(dir.TrimEnd('/', '\\'))}");
        }
        catch (Exception ex)
        {
            _status.Text = $"Failed to open {game}: {ex.Message}";
        }
    }

    // ---- loose file flow ----

    private async void OpenFile()
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
            ClearSession();
            DiscoverAdhocResources(path);
            if (path.EndsWith(".gob", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".goo", StringComparison.OrdinalIgnoreCase))
            {
                _adhocGob = GobArchive.Open(path);
                _levelArchive = _adhocGob;
                PopulateLevels(Path.GetFileName(path));
            }
            else
            {
                LoadLevel(JklParser.ParseFile(path), file.Name);
            }
        }
        catch (Exception ex)
        {
            _status.Text = $"Failed to open {file.Name}: {ex.Message}";
        }
    }

    private void DiscoverAdhocResources(string openedPath)
    {
        var dir = Path.GetDirectoryName(openedPath);
        if (dir is null) return;
        foreach (var c in new[]
        {
            Path.Combine(dir, "Res2.gob"),
            Path.Combine(dir, "..", "Resource", "Res2.gob"),
            Path.Combine(Directory.GetParent(dir)?.FullName ?? dir, "Resource", "Res2.gob"),
        })
        {
            if (!File.Exists(c)) continue;
            try { _adhocResourceGob = GobArchive.Open(c); _materialArchives = new[] { _adhocResourceGob }; return; }
            catch { /* try next */ }
        }
    }

    // ---- shared session ----

    private void PopulateLevels(string header)
    {
        _levels = _levelArchive?.FindByExtension(".jkl").OrderBy(e => e.NormalizedName).ToList() ?? new();
        _sidePanelHeader.Text = $"{header} — {_levels.Count} levels";
        _levelList.ItemsSource = _levels.Select(e => Path.GetFileName(e.NormalizedName)).ToList();
        if (_levels.Count > 0) _levelList.SelectedIndex = 0;
        else _status.Text = $"{header}: no levels found";
    }

    private void OnLevelSelected()
    {
        int i = _levelList.SelectedIndex;
        if (_levelArchive is null || (uint)i >= (uint)_levels.Count) return;
        var entry = _levels[i];
        try { LoadLevel(JklParser.Parse(_levelArchive.ReadText(entry)), Path.GetFileName(entry.NormalizedName)); }
        catch (Exception ex) { _status.Text = $"Failed to parse {entry.Name}: {ex.Message}"; }
    }

    private void LoadLevel(Level level, string name)
    {
        TextureLookup? textures = null;
        try { textures = MakeTextureLookup(level); }
        catch { /* untextured fallback */ }

        _view.SetLevel(level, textures);
        int surfaces = level.Sectors.Sum(s => s.Surfaces.Count);
        _status.Text = $"{name} — {level.Sectors.Count} sectors, {surfaces} surfaces, " +
                       $"{level.Things.Count} things ({(textures is null ? "untextured" : "textured")})";
    }

    private TextureLookup? MakeTextureLookup(Level level)
    {
        if (_materialArchives.Length == 0) return null;
        var palette = MaterialLibrary.LoadPalette(level.ColorMaps, _materialArchives);
        var library = new MaterialLibrary(palette, _materialArchives);
        return material =>
        {
            var t = library.Get(material);
            return t is { } r ? new TextureData(r.Width, r.Height, r.Rgba) : null;
        };
    }

    private void ClearSession()
    {
        _install?.Dispose(); _install = null;
        _adhocGob?.Dispose(); _adhocGob = null;
        _adhocResourceGob?.Dispose(); _adhocResourceGob = null;
        _levelArchive = null;
        _materialArchives = Array.Empty<GobArchive>();
        _levels = new();
        _levelList.ItemsSource = null;
    }

    private Menu BuildMenu()
    {
        var open = new MenuItem { Header = "_Open…" };
        open.Click += (_, _) => OpenFile();
        var exit = new MenuItem { Header = "E_xit" };
        exit.Click += (_, _) => (Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(open);
        file.Items.Add(new MenuItem { Header = "_Save", IsEnabled = false });
        file.Items.Add(new Separator());
        file.Items.Add(exit);

        var undo = new MenuItem { Header = "_Undo", InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control) };
        undo.Click += (_, _) => _view.History.Undo();
        var redo = new MenuItem { Header = "_Redo", InputGesture = new KeyGesture(Key.Y, KeyModifiers.Control) };
        redo.Click += (_, _) => _view.History.Redo();
        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(undo);
        edit.Items.Add(redo);
        _view.History.Changed += () =>
        {
            undo.IsEnabled = _view.History.CanUndo;
            redo.IsEnabled = _view.History.CanRedo;
            undo.Header = _view.History.CanUndo ? $"_Undo {_view.History.UndoName}" : "_Undo";
            redo.Header = _view.History.CanRedo ? $"_Redo {_view.History.RedoName}" : "_Redo";
        };
        undo.IsEnabled = false;
        redo.IsEnabled = false;

        var game = new MenuItem { Header = "_Game" };
        foreach (var (label, kind) in Games)
        {
            var item = new MenuItem { Header = $"Set _{label} Folder…" };
            item.Click += (_, _) => SetGameFolder(kind);
            game.Items.Add(item);
        }

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(new MenuItem { Header = "3D Preview" });
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(new MenuItem { Header = "About SED" });

        var menu = new Menu();
        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(game);
        menu.Items.Add(view);
        menu.Items.Add(help);
        return menu;
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearSession();
        base.OnClosed(e);
    }
}
