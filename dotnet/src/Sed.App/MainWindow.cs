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
    private readonly ListBox _materialList;
    private readonly TextBlock _sidePanelHeader;
    private Sed.Formats.Material.MaterialLibrary? _matLibrary;
    private List<MaterialThumb> _matThumbs = new();

    // Active session (whichever source is open).
    private GameInstall? _install;
    private GobArchive? _adhocGob;
    private GobArchive? _adhocResourceGob;
    private GobArchive? _levelArchive;
    private GobArchive[] _materialArchives = Array.Empty<GobArchive>();
    private Sed.Formats.ThreeDo.ModelLibrary? _modelLibrary;
    private JklDocument? _currentDoc;     // for saving the currently loaded level
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

        _materialList = new ListBox { Background = Brushes.Transparent, ItemTemplate = MaterialItemTemplate() };
        _materialList.SelectionChanged += (_, _) => OnMaterialPicked();

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

        // Right: material inspector (click a material to assign to the selected surface).
        var matContent = new DockPanel { Width = 200 };
        var matHeader = new TextBlock { Margin = new Thickness(10, 8), Text = "Materials — click to assign", Foreground = Brushes.Gray };
        DockPanel.SetDock(matHeader, Dock.Top);
        matContent.Children.Add(matHeader);
        matContent.Children.Add(_materialList);
        var matPanel = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)), Child = matContent };
        DockPanel.SetDock(matPanel, Dock.Right);
        root.Children.Add(matPanel);

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
                _currentDoc = JklParser.ParseDocument(File.ReadAllText(path));
                LoadLevel(_currentDoc.Level, file.Name);
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
        try
        {
            _currentDoc = JklParser.ParseDocument(_levelArchive.ReadText(entry));
            LoadLevel(_currentDoc.Level, Path.GetFileName(entry.NormalizedName));
        }
        catch (Exception ex) { _status.Text = $"Failed to parse {entry.Name}: {ex.Message}"; }
    }

    private void LoadLevel(Level level, string name)
    {
        TextureLookup? textures = null;
        Func<string, Sed.Formats.ThreeDo.ThreeDoModel?>? models = null;
        byte[]? paletteRgb = null, lightTable = null;

        if (_materialArchives.Length > 0)
        {
            try
            {
                var colormap = MaterialLibrary.LoadPalette(level.ColorMaps, _materialArchives);
                var library = new MaterialLibrary(colormap, _materialArchives);
                _matLibrary = library;
                paletteRgb = colormap.PaletteRgb;
                lightTable = colormap.LightTable;
                textures = material =>
                {
                    var t = library.GetIndexed(material);
                    return t is { } r ? new IndexedTexture(r.Width, r.Height, r.Indices) : null;
                };
                _modelLibrary ??= new Sed.Formats.ThreeDo.ModelLibrary(_materialArchives);
                models = _modelLibrary.Get;
            }
            catch { textures = null; }
        }

        _view.Materials = _currentDoc?.Materials ?? new List<string>();
        _view.SetLevel(level, textures, models, paletteRgb, lightTable);
        PopulateMaterials();
        int surfaces = level.Sectors.Sum(s => s.Surfaces.Count);
        _status.Text = $"{name} — {level.Sectors.Count} sectors, {surfaces} surfaces, " +
                       $"{level.Things.Count} things ({(textures is null ? "untextured" : "textured")})";
    }

    // ---- material inspector ----

    private sealed class MaterialThumb
    {
        public string Name { get; init; } = string.Empty;
        public int Index { get; init; }
        public Avalonia.Media.Imaging.Bitmap? Image { get; init; }
    }

    private static Avalonia.Controls.Templates.IDataTemplate MaterialItemTemplate() =>
        new Avalonia.Controls.Templates.FuncDataTemplate<MaterialThumb>((m, _) =>
        {
            var img = new Image { Width = 32, Height = 32, Source = m?.Image, Stretch = Stretch.Fill };
            var txt = new TextBlock { Text = m?.Name, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(img);
            sp.Children.Add(txt);
            return sp;
        });

    private void PopulateMaterials()
    {
        _matThumbs = new List<MaterialThumb>();
        var names = _currentDoc?.Materials ?? new List<string>();
        for (int i = 0; i < names.Count; i++)
        {
            Avalonia.Media.Imaging.Bitmap? thumb = null;
            try
            {
                var t = _matLibrary?.Get(names[i]);
                if (t is { } r) thumb = MakeThumb(r.Rgba, r.Width, r.Height);
            }
            catch { /* skip unrenderable */ }
            _matThumbs.Add(new MaterialThumb { Name = names[i], Index = i, Image = thumb });
        }
        _materialList.ItemsSource = _matThumbs;
    }

    private static Avalonia.Media.Imaging.Bitmap MakeThumb(byte[] rgba, int w, int h)
    {
        const int S = 32;
        var dst = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int sx = w > 0 ? x * w / S : 0, sy = h > 0 ? y * h / S : 0;
                int si = (sy * w + sx) * 4, di = (y * S + x) * 4;
                if (si + 3 < rgba.Length) Array.Copy(rgba, si, dst, di, 4);
            }
        return VulkanViewport.ToBitmap(dst, S, S);
    }

    private void OnMaterialPicked()
    {
        if (_materialList.SelectedItem is MaterialThumb m && !_view.SetSelectedSurfaceMaterial(m.Name, m.Index))
            _status.Text = "Select a surface first, then click a material";
    }

    private async void SaveAs()
    {
        if (_currentDoc is null) { _status.Text = "No level loaded to save."; return; }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save level as JKL",
            DefaultExtension = "jkl",
            SuggestedFileName = "edited.jkl",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Sith engine level (*.jkl)") { Patterns = new[] { "*.jkl" } },
            },
        });
        if (file is null) return;

        try
        {
            JklWriter.Save(_currentDoc, file.Path.LocalPath);
            _status.Text = $"Saved → {file.Name}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Save failed: {ex.Message}";
        }
    }

    private void ClearSession()
    {
        _currentDoc = null;
        _install?.Dispose(); _install = null;
        _adhocGob?.Dispose(); _adhocGob = null;
        _adhocResourceGob?.Dispose(); _adhocResourceGob = null;
        _levelArchive = null;
        _materialArchives = Array.Empty<GobArchive>();
        _modelLibrary = null;
        _matLibrary = null;
        _materialList.ItemsSource = null;
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
        var saveAs = new MenuItem { Header = "Save _As…", InputGesture = new KeyGesture(Key.S, KeyModifiers.Control) };
        saveAs.Click += (_, _) => SaveAs();
        file.Items.Add(saveAs);
        file.Items.Add(new Separator());
        file.Items.Add(exit);

        var undo = new MenuItem { Header = "_Undo", InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control) };
        undo.Click += (_, _) => _view.History.Undo();
        var redo = new MenuItem { Header = "_Redo", InputGesture = new KeyGesture(Key.Y, KeyModifiers.Control) };
        redo.Click += (_, _) => _view.History.Redo();
        var newSector = new MenuItem { Header = "_New Box Sector", InputGesture = new KeyGesture(Key.N) };
        newSector.Click += (_, _) => _view.CreateSector();
        var delSector = new MenuItem { Header = "Delete Se_ctor" };
        delSector.Click += (_, _) => _view.DeleteSector();

        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(undo);
        edit.Items.Add(redo);
        edit.Items.Add(new Separator());
        edit.Items.Add(newSector);
        edit.Items.Add(delSector);
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
        var brightness = new MenuItem { Header = "Cycle _Brightness", InputGesture = new KeyGesture(Key.B) };
        brightness.Click += (_, _) => _view.CycleBrightness();
        view.Items.Add(brightness);
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
