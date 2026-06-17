using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Game;
using Sed.Formats.Jkl;
using Sed.Formats.Material;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

// Renders interior views from inside a level (camera placed at a thing position)
// to inspect geometry + texturing as a player would see it.
//   Sed.InteriorProbe <baseDir> <level> [thingIndex]
if (args.Length < 2) { Console.Error.WriteLine("usage: Sed.InteriorProbe <baseDir> <level> [thingIndex]"); return 1; }
var baseDir = args[0];
var levelName = args[1];
int thingIndex = args.Length > 2 && int.TryParse(args[2], out var ti) ? ti : -1;

using var install = GameInstall.TryOpen(ProjectType.JediKnight, baseDir)
    ?? throw new DirectoryNotFoundException(baseDir);
var entry = install.Levels.FirstOrDefault(e => e.NormalizedName.Contains(levelName.ToLowerInvariant()))
    ?? throw new FileNotFoundException(levelName);
var level = JklParser.Parse(install.ReadLevel(entry));
Console.WriteLine($"{entry.Name}: {level.Sectors.Count} sectors, {level.Things.Count} things");

// Pick a camera anchor: requested thing, else a 'player'/'start' thing, else thing 0.
Thing? anchor = thingIndex >= 0 && thingIndex < level.Things.Count
    ? level.Things[thingIndex]
    : level.Things.FirstOrDefault(t => t.Name.Contains("player", StringComparison.OrdinalIgnoreCase)
                                    || t.Name.Contains("start", StringComparison.OrdinalIgnoreCase))
      ?? level.Things.FirstOrDefault();
if (anchor is null) { Console.Error.WriteLine("no things to anchor camera"); return 2; }
Console.WriteLine($"camera at thing #{anchor.Num} '{anchor.Name}' {anchor.Position}");
foreach (var t in level.Things.Take(8))
    Console.WriteLine($"   thing {t.Num,3} '{t.Name}' sec={t.Sector?.Num}");

var palette = MaterialLibrary.LoadPalette(level.ColorMaps, install.ResourceArchives.ToArray());
var library = new MaterialLibrary(palette, install.ResourceArchives.ToArray());
var scene = SceneBuilder.BuildScene(level);
int emptyTris = scene.Submeshes.Where(s => string.IsNullOrEmpty(s.Material)).Sum(s => s.IndexCount / 3);
int totalTris = scene.Submeshes.Sum(s => s.IndexCount / 3);
int missing = 0, resolved = 0;
foreach (var sm in scene.Submeshes.Where(s => !string.IsNullOrEmpty(s.Material)))
    if (library.Get(sm.Material) is null) missing++; else resolved++;
Console.WriteLine($"  submeshes={scene.Submeshes.Count}, no-material tris={emptyTris}/{totalTris}, " +
                  $"materials resolved={resolved} missing={missing}");
TextureData? Lookup(string m) { var t = library.Get(m); return t is { } r ? new TextureData(r.Width, r.Height, r.Rgba) : null; }

const uint W = 720, H = 540;
using var ctx = VulkanContext.Create("SED Interior");
using var device = VulkanDevice.Create(ctx);
using var renderer = new SceneRenderer(device);
renderer.SetScene(scene, Lookup);

var eye = anchor.Position + new Vec3(0, 0, 0.05); // just above the placement point
for (int i = 0; i < 4; i++)
{
    double yaw = i * System.Math.PI / 2;
    var cam = new Camera { Position = eye, Yaw = yaw, Pitch = -0.15, FieldOfViewDegrees = 80, NearPlane = 0.01, FarPlane = 200 };
    var pixels = renderer.Render(cam.ViewProjection((double)W / H), W, H);
    var outPath = $"/tmp/interior_{levelName}_{i * 90}.png";
    PngWriter.Write(outPath, pixels, (int)W, (int)H);
    Console.WriteLine($"  yaw {i * 90,3}° → {outPath}");
}
return 0;
