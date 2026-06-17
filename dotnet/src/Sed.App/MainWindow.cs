using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Sed.App;

/// <summary>
/// The editor shell: menu, a sector/things side panel placeholder, the Vulkan
/// 3D viewport, and a status bar. Built in code (no XAML) to keep the bring-up
/// dependency surface small.
/// </summary>
public class MainWindow : Window
{
    public MainWindow()
    {
        Title = "SED — Sith Engine Editor (.NET / Vulkan)";
        Width = 1100;
        Height = 720;

        var status = new TextBlock
        {
            Margin = new Thickness(8, 4),
            Text = "Ready",
            VerticalAlignment = VerticalAlignment.Center,
        };

        var viewport = BuildViewport(status);

        var root = new DockPanel();

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x2b)),
            Child = status,
            Height = 28,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);

        var sidePanel = new Border
        {
            Width = 240,
            Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)),
            Child = new TextBlock
            {
                Margin = new Thickness(10),
                Text = "Sectors / Things\n(coming next)",
                Foreground = Brushes.Gray,
            },
        };
        DockPanel.SetDock(sidePanel, Dock.Left);
        root.Children.Add(sidePanel);

        root.Children.Add(viewport);
        Content = root;
    }

    private static Control BuildViewport(TextBlock status)
    {
        var level = Sed.Rendering.SampleScene.CreateCube();
        status.Text = "Vulkan viewport (MoltenVK) — drag to orbit, wheel to zoom";
        return new VulkanView(level);
    }

    private static Menu BuildMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(new MenuItem { Header = "_Open…" });
        file.Items.Add(new MenuItem { Header = "_Save" });
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
}
