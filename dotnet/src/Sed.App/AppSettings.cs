using System.Text.Json;
using Sed.Core.Model;

namespace Sed.App;

/// <summary>
/// Persisted editor settings — per-game installation directories, mirroring the
/// original editor's JK/MotS/IJIM path options. Stored as JSON under the user's
/// application-data directory.
/// </summary>
public sealed class AppSettings
{
    public string? JediKnightDir { get; set; }
    public string? MysteriesDir { get; set; }
    public string? InfernalMachineDir { get; set; }

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create),
        "SED", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = ConfigPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { /* corrupt/unreadable — start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    public string? DirFor(ProjectType game) => game switch
    {
        ProjectType.MysteriesOfTheSith => MysteriesDir,
        ProjectType.InfernalMachine => InfernalMachineDir,
        _ => JediKnightDir,
    };

    public void SetDir(ProjectType game, string dir)
    {
        switch (game)
        {
            case ProjectType.MysteriesOfTheSith: MysteriesDir = dir; break;
            case ProjectType.InfernalMachine: InfernalMachineDir = dir; break;
            default: JediKnightDir = dir; break;
        }
    }
}
