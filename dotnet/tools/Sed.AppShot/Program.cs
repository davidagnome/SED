using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Sed.App;

// Renders the editor MainWindow headlessly (Skia) and saves a PNG, so the full
// composed UI — menu, side panel, and the Vulkan viewport bitmap — can be
// verified without opening a GUI window.
var outPath = args.Length > 0 ? args[0] : "/tmp/sed_app.png";

var session = HeadlessUnitTestSession.StartNew(typeof(AppEntry));
await session.Dispatch(() =>
{
    var window = new MainWindow { Width = 1100, Height = 720 };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var frame = window.CaptureRenderedFrame();
    frame?.Save(outPath);
    Console.WriteLine(frame is null ? "No frame captured" : $"Wrote {Path.GetFullPath(outPath)}");
}, CancellationToken.None);

return 0;

// Headless session entry point: configure Avalonia for offscreen Skia rendering.
internal sealed class AppEntry
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
