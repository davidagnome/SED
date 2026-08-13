using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Sed.Formats.Game;

namespace Sed.App;

/// <summary>
/// The episode editor (U_MEDIT): edits the project's episode.jk — the episode
/// name, game type and the ordered level sequences (with the original's fields:
/// line, cd, level number, LEVEL/DECIDE type, file, light/dark power, gotoA/B)
/// — plus the cogstrings.uni entries the game reads for level names and the
/// mission text shown before each level. Saving writes both files into the
/// project directory (misc\cogstrings.uni), exactly where the game looks.
/// </summary>
public sealed class EpisodeEditorWindow : Window
{
    private readonly AppSettings _settings;

    private readonly TextBox _epName;
    private readonly ComboBox _gameType;
    private readonly ListBox _seqList;
    private readonly TextBox _lnum, _cdnum, _levnum, _fname, _gotoA, _gotoB;
    private readonly ComboBox _type;
    private readonly ComboBox _lpow, _dpow;
    private readonly TextBox _levName, _text00, _text01;
    private readonly TextBlock _summary;

    private EpisodeFile _episode = new();
    private CogStrings _strings = new();
    private int _selected = -1;

    public EpisodeEditorWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Episode Editor";
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _epName = InspectorPanel.TextField(string.Empty, _ => { });
        _epName.Width = 220;

        _gameType = new ComboBox
        {
            ItemsSource = new[] { "1 (Single Player)", "2 (DeathMatch)", "8 (Special/CTF)" },
            SelectedIndex = 0,
            MinWidth = 160,
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 4) };
        header.Children.Add(new TextBlock { Text = "Episode name", Width = 100, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_epName);
        header.Children.Add(new TextBlock { Text = "Game type", Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        header.Children.Add(_gameType);

        _seqList = new ListBox
        {
            Background = Brushes.Transparent,
            MinHeight = 160,
            ItemTemplate = new FuncDataTemplate<EpisodeSequence>((seq, _) => new TextBlock
            {
                Text = seq is null ? string.Empty : $"{seq.Line}: {seq.File}  (level {seq.LevelNum}, type {seq.Type})",
                FontSize = 12,
                Margin = new Thickness(4, 2),
            }),
        };
        _seqList.SelectionChanged += (_, _) =>
        {
            _selected = _seqList.SelectedIndex;
            ShowSequence();
        };

        _summary = new TextBlock { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(4, 2) };

        var seqBox = new DockPanel { Margin = new Thickness(12, 4) };
        DockPanel.SetDock(_summary, Dock.Top);
        seqBox.Children.Add(_summary);
        seqBox.Children.Add(_seqList);

        // Per-sequence fields.
        _lnum = InspectorPanel.TextField("", _ => { });
        _cdnum = InspectorPanel.TextField("", _ => { });
        _levnum = InspectorPanel.TextField("", _ => { });
        _fname = InspectorPanel.TextField("", _ => { });
        _gotoA = InspectorPanel.TextField("", _ => { });
        _gotoB = InspectorPanel.TextField("", _ => { });
        _type = new ComboBox { ItemsSource = new[] { "LEVEL", "DECIDE" }, SelectedIndex = 0, MinWidth = 90 };
        _lpow = new ComboBox { ItemsSource = new[] { "0 (None)", "22 (Speed)", "21 (Jump)", "24 (Pull)" }, SelectedIndex = 0, MinWidth = 90 };
        _dpow = new ComboBox { ItemsSource = new[] { "0 (None)", "22 (Speed)", "21 (Jump)", "24 (Pull)" }, SelectedIndex = 0, MinWidth = 90 };
        _levName = InspectorPanel.TextField("", _ => { });
        _text00 = InspectorPanel.TextField("", _ => { });
        _text01 = InspectorPanel.TextField("", _ => { });

        var fields = new StackPanel { Margin = new Thickness(12, 8) };
        fields.Children.Add(FieldRow("Line", _lnum, "CD", _cdnum, "Level", _levnum));
        fields.Children.Add(FieldRow("File", _fname, "Type", _type, "Light pow", _lpow, "Dark pow", _dpow));
        fields.Children.Add(FieldRow("Goto A", _gotoA, "Goto B", _gotoB));

        var textLabel = new TextBlock
        {
            Text = "Mission text (cogstrings.uni — shown before the level)",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(2, 6, 0, 2),
        };
        fields.Children.Add(textLabel);
        fields.Children.Add(FieldRow("Level name", _levName));
        fields.Children.Add(FieldRow("Text line 1", _text00));
        fields.Children.Add(FieldRow("Text line 2", _text01));

        // Commit on focus loss / enter.
        foreach (var box in new[] { _lnum, _cdnum, _levnum, _fname, _gotoA, _gotoB, _levName, _text00, _text01 })
        {
            box.PropertyChanged += (_, e) => { if (e.Property.Name == "Text") CommitSequence(); };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) CommitSequence();
            };
        }
        _type.SelectionChanged += (_, _) => CommitSequence();
        _lpow.SelectionChanged += (_, _) => CommitSequence();
        _dpow.SelectionChanged += (_, _) => CommitSequence();

        var add = new Button { Content = "Add", Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) => AddSequence();
        var remove = new Button { Content = "Remove", Margin = new Thickness(0, 0, 6, 0) };
        remove.Click += (_, _) => RemoveSequence();
        var save = new Button { Content = "Save", Margin = new Thickness(6, 0, 0, 0) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(6, 0, 0, 0) };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(seqBox, Dock.Top);
        root.Children.Add(seqBox);
        DockPanel.SetDock(fields, Dock.Top);
        root.Children.Add(fields);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        Content = root;

        Load();
    }

    private static StackPanel FieldRow(params object[] labelAndField)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2) };
        for (int i = 0; i < labelAndField.Length; i += 2)
        {
            row.Children.Add(new TextBlock
            {
                Text = (string)labelAndField[i],
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            });
            row.Children.Add((Control)labelAndField[i + 1]);
            row.Children.Add(new TextBlock { Width = 14 });
        }
        return row;
    }

    private void Load()
    {
        var dir = _settings.ResolvedProjectDir();
        var path = Path.Combine(dir, "episode.jk");
        if (File.Exists(path))
        {
            try { _episode = EpisodeFile.Parse(File.ReadAllText(path)); }
            catch { _episode = new EpisodeFile(); }
        }
        else
        {
            // A fresh episode with the current level as the first sequence
            // (the original's CreateNewEpisode).
            _episode = new EpisodeFile
            {
                Name = "New Episode",
                GameType = 1,
            };
            _episode.Sequences.Add(new EpisodeSequence
            {
                Line = 10,
                LevelNum = 1,
                Type = "LEVEL",
                File = "level.jkl",
                GotoA = -1,
                GotoB = -1,
            });
        }

        var stringsPath = Path.Combine(dir, "misc", "cogstrings.uni");
        if (File.Exists(stringsPath))
        {
            try { _strings = CogStrings.Parse(File.ReadAllText(stringsPath)); }
            catch { _strings = new CogStrings(); }
        }

        _epName.Text = _episode.Name;
        _gameType.SelectedIndex = _episode.GameType switch
        {
            2 => 1,
            8 => 2,
            _ => 0,
        };

        _seqList.ItemsSource = _episode.Sequences;
        if (_episode.Sequences.Count > 0) _seqList.SelectedIndex = 0;
        UpdateSummary();
    }

    private void ShowSequence()
    {
        if (_selected < 0 || _selected >= _episode.Sequences.Count) return;
        var s = _episode.Sequences[_selected];
        _lnum.Text = s.Line.ToString();
        _cdnum.Text = s.Cd.ToString();
        _levnum.Text = s.LevelNum.ToString();
        _fname.Text = s.File;
        _type.SelectedIndex = s.Type == "DECIDE" ? 1 : 0;
        _lpow.SelectedIndex = ForceIndex(s.LightPow);
        _dpow.SelectedIndex = ForceIndex(s.DarkPow);
        _gotoA.Text = s.GotoA.ToString();
        _gotoB.Text = s.GotoB.ToString();

        var baseName = Path.GetFileNameWithoutExtension(s.File);
        _levName.Text = _strings.GetString(baseName);
        _text00.Text = _strings.GetString(baseName + "_TEXT_00");
        _text01.Text = _strings.GetString(baseName + "_TEXT_01");
    }

    private static int ForceIndex(int value) => value switch
    {
        22 => 1,
        21 => 2,
        24 => 3,
        _ => 0,
    };

    private void CommitSequence()
    {
        if (_selected < 0 || _selected >= _episode.Sequences.Count) return;
        var s = _episode.Sequences[_selected];
        s.Line = ParseInt(_lnum.Text, s.Line);
        s.Cd = ParseInt(_cdnum.Text, s.Cd);
        s.LevelNum = ParseInt(_levnum.Text, s.LevelNum);
        s.File = _fname.Text?.Trim() ?? string.Empty;
        s.Type = _type.SelectedIndex == 1 ? "DECIDE" : "LEVEL";
        s.LightPow = _lpow.SelectedIndex switch { 1 => 22, 2 => 21, 3 => 24, _ => 0 };
        s.DarkPow = _dpow.SelectedIndex switch { 1 => 22, 2 => 21, 3 => 24, _ => 0 };
        s.GotoA = ParseInt(_gotoA.Text, s.GotoA);
        s.GotoB = ParseInt(_gotoB.Text, s.GotoB);

        // Sync the string table from the mission-text fields.
        var baseName = Path.GetFileNameWithoutExtension(s.File);
        if (baseName.Length > 0)
        {
            _strings.SetString(baseName, _levName.Text ?? string.Empty);
            _strings.SetString(baseName + "_TEXT_00", _text00.Text ?? string.Empty);
            _strings.SetString(baseName + "_TEXT_01", _text01.Text ?? string.Empty);
        }

        _seqList.ItemsSource = null;
        _seqList.ItemsSource = _episode.Sequences;
        _seqList.SelectedIndex = _selected;
        UpdateSummary();
    }

    private void AddSequence()
    {
        CommitSequence();
        _episode.Sequences.Add(new EpisodeSequence
        {
            Line = _episode.Sequences.Count > 0
                ? _episode.Sequences.Max(x => x.Line) + 10
                : 10,
            LevelNum = _episode.Sequences.Count + 1,
            Type = "LEVEL",
            File = $"level{_episode.Sequences.Count + 1}.jkl",
            GotoA = -1,
            GotoB = -1,
        });
        _selected = _episode.Sequences.Count - 1;
        _seqList.ItemsSource = null;
        _seqList.ItemsSource = _episode.Sequences;
        _seqList.SelectedIndex = _selected;
        ShowSequence();
        UpdateSummary();
    }

    private void RemoveSequence()
    {
        if (_selected < 0 || _selected >= _episode.Sequences.Count) return;
        var s = _episode.Sequences[_selected];
        var baseName = Path.GetFileNameWithoutExtension(s.File);
        _strings.RemoveString(baseName);
        _strings.RemoveString(baseName + "_TEXT_00");
        _strings.RemoveString(baseName + "_TEXT_01");
        _episode.Sequences.RemoveAt(_selected);
        _selected = Math.Min(_selected, _episode.Sequences.Count - 1);
        _seqList.ItemsSource = null;
        _seqList.ItemsSource = _episode.Sequences;
        _seqList.SelectedIndex = _selected;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _summary.Text = $"Sequences: {_episode.Sequences.Count} — edit in the project dir " +
                        $"{_settings.ResolvedProjectDir()} (episode.jk + misc/cogstrings.uni)";
    }

    private void Save()
    {
        CommitSequence();
        _episode.Name = _epName.Text ?? string.Empty;
        _episode.GameType = _gameType.SelectedIndex switch { 1 => 2, 2 => 8, _ => 1 };

        try
        {
            var dir = _settings.ResolvedProjectDir();
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "misc"));
            File.WriteAllText(Path.Combine(dir, "episode.jk"), _episode.Build());

            var stringsText = _strings.Build();
            if (stringsText.Length == 0)
                File.Delete(Path.Combine(dir, "misc", "cogstrings.uni"));
            else
                File.WriteAllText(Path.Combine(dir, "misc", "cogstrings.uni"), stringsText);

            Close();
        }
        catch (Exception ex)
        {
            // surface via a simple status-like label: reuse the summary.
            _summary.Text = $"Save failed: {ex.Message}";
        }
    }

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
