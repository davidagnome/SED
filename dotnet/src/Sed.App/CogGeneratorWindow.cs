using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sed.Formats.Cogs;

namespace Sed.App;

/// <summary>
/// Generates a level master COG (`U_COGGEN.PAS`) — the script that registers
/// itself as the level's master, initialises goals, grants starting weapons and
/// sets the Force rank. Writes a <c>.cog</c> file; place it in the level's COGS
/// section afterwards with the COG editor.
/// </summary>
public sealed class CogGeneratorWindow : Window
{
    private static readonly (string label, JkWeapons flag)[] WeaponChoices =
    {
        ("Bryar pistol", JkWeapons.Briar),
        ("Stormtrooper rifle", JkWeapons.StormtrooperRifle),
        ("Thermal detonators", JkWeapons.ThermalDetonators),
        ("Crossbow", JkWeapons.Crossbow),
        ("Repeater", JkWeapons.Repeater),
        ("Railgun", JkWeapons.Railgun),
        ("Sequencer charges", JkWeapons.SequencerCharges),
        ("Concussion rifle", JkWeapons.ConcussionRifle),
        ("Lightsaber", JkWeapons.Lightsaber),
    };

    private readonly ListBox _goals = new() { Background = Brushes.Transparent, Height = 150 };
    private readonly TextBox _goalText = new() { PlaceholderText = "goal description", Margin = new Thickness(0, 0, 4, 0) };
    private readonly TextBox _goalBase = new() { Text = "0", Width = 80 };
    private readonly ComboBox _rank;
    private readonly List<CheckBox> _weapons = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(10, 4) };

    private readonly List<string> _goalList = new();

    public CogGeneratorWindow()
    {
        Title = "Generate Master COG";
        Width = 520;
        Height = 620;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22));

        _rank = new ComboBox
        {
            ItemsSource = Enumerable.Range(0, 9).Select(i => $"Rank {i}").ToList(),
            SelectedIndex = 0,
            Width = 120,
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10) };

        root.Children.Add(Heading("Goals"));
        root.Children.Add(_goals);

        var goalButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4), Spacing = 4 };
        goalButtons.Children.Add(_goalText);
        goalButtons.Children.Add(Button("Add", AddGoal));
        goalButtons.Children.Add(Button("Update", UpdateGoal));
        goalButtons.Children.Add(Button("Delete", DeleteGoal));
        root.Children.Add(goalButtons);
        _goalText.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddGoal(); };

        var baseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6), Spacing = 6 };
        baseRow.Children.Add(new TextBlock
        {
            Text = "First goal index", VerticalAlignment = VerticalAlignment.Center, FontSize = 11,
            Foreground = Brushes.LightGray,
        });
        baseRow.Children.Add(_goalBase);
        baseRow.Children.Add(new TextBlock
        {
            Text = "(GOAL_nnnnn in cogstrings.uni)", VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10, Foreground = Brushes.DimGray,
        });
        root.Children.Add(baseRow);

        root.Children.Add(Heading("Starting weapons"));
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, _) in WeaponChoices)
        {
            var box = new CheckBox { Content = label, FontSize = 11, Width = 160 };
            _weapons.Add(box);
            grid.Children.Add(box);
        }
        root.Children.Add(grid);

        root.Children.Add(Heading("Force rank"));
        root.Children.Add(_rank);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12), Spacing = 6 };
        actions.Children.Add(Button("Preview", Preview));
        actions.Children.Add(Button("Save as .cog…", Save));
        root.Children.Add(actions);

        root.Children.Add(_status);

        Content = new ScrollViewer { Content = root };
        RefreshGoals();
    }

    private MasterCogOptions Options()
    {
        var weapons = JkWeapons.None;
        for (int i = 0; i < WeaponChoices.Length; i++)
            if (_weapons[i].IsChecked == true) weapons |= WeaponChoices[i].flag;

        int.TryParse(_goalBase.Text, out int baseIndex);

        return new MasterCogOptions
        {
            GoalBase = baseIndex,
            Goals = _goalList.ToList(),
            Weapons = weapons,
            ForceRank = System.Math.Max(0, _rank.SelectedIndex),
        };
    }

    private void Preview()
    {
        var text = MasterCogGenerator.Generate(Options());
        var window = new Window
        {
            Title = "Master COG preview",
            Width = 620,
            Height = 620,
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)),
            Content = new ScrollViewer
            {
                Content = new TextBox
                {
                    Text = text,
                    AcceptsReturn = true,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("monospace"),
                    FontSize = 11,
                },
            },
        };
        window.Show(this);
    }

    private async void Save()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save master COG",
            DefaultExtension = "cog",
            SuggestedFileName = "master.cog",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("COG script (*.cog)") { Patterns = new[] { "*.cog" } },
            },
        });
        if (file is null) return;

        try
        {
            var options = Options();
            await File.WriteAllTextAsync(file.Path.LocalPath, MasterCogGenerator.Generate(options));

            // The goal strings themselves live in cogstrings.uni, which this does
            // not write — say so rather than letting them look handled.
            _status.Text = options.Goals.Count > 0
                ? $"Wrote {file.Name}. Add the {options.Goals.Count} goal string(s) to cogstrings.uni as " +
                  $"{MasterCogGenerator.GoalKey(options.GoalBase, 0)}…"
                : $"Wrote {file.Name}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Save failed: {ex.Message}";
        }
    }

    private void AddGoal()
    {
        var text = _goalText.Text?.Trim() ?? string.Empty;
        if (text.Length == 0) { _status.Text = "Enter a goal description first."; return; }

        int at = _goals.SelectedIndex;
        if (at >= 0) _goalList.Insert(at, text); else _goalList.Add(text);
        _goalText.Text = string.Empty;
        RefreshGoals();
    }

    private void UpdateGoal()
    {
        int at = _goals.SelectedIndex;
        if (at < 0) { _status.Text = "Select a goal to update."; return; }
        _goalList[at] = _goalText.Text?.Trim() ?? string.Empty;
        RefreshGoals();
    }

    private void DeleteGoal()
    {
        int at = _goals.SelectedIndex;
        if (at < 0) { _status.Text = "Select a goal to delete."; return; }
        _goalList.RemoveAt(at);
        RefreshGoals();
    }

    private void RefreshGoals()
    {
        int baseIndex = int.TryParse(_goalBase.Text, out var b) ? b : 0;
        _goals.ItemsSource = _goalList
            .Select((g, i) => $"{MasterCogGenerator.GoalKey(baseIndex, i)}   {g}")
            .ToList();
        _status.Text = $"{_goalList.Count} goal(s).";
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Foreground = Brushes.White,
        FontSize = 12,
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static Button Button(string content, Action onClick)
    {
        var button = new Button { Content = content, FontSize = 11, Padding = new Thickness(8, 2) };
        button.Click += (_, _) => onClick();
        return button;
    }
}
