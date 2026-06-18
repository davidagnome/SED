using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Gob;
using Sed.Formats.Jkl;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

// Usage:
//   Sed.GobTool <archive.gob>                 list JKL entries
//   Sed.GobTool <archive.gob> --jkl <name> [--png out.png]   parse + render a level
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: Sed.GobTool <archive.gob> [--jkl <name>] [--png <out>]");
    return 1;
}

var gobPath = args[0];
string? jklName = OptValue(args, "--jkl");
string pngPath = OptValue(args, "--png") ?? "/tmp/sed_level.png";

using var gob = GobArchive.Open(gobPath);
var jkls = gob.FindByExtension(".jkl").OrderBy(e => e.NormalizedName).ToList();
Console.WriteLine($"Archive: {gob.Entries.Count} entries, {jkls.Count} JKL levels");

var extArg = OptValue(args, "--ext");
if (extArg is not null)
{
    var entries = gob.FindByExtension(extArg).OrderBy(e => e.NormalizedName).ToList();
    Console.WriteLine($"{entries.Count} *.{extArg.TrimStart('.')} entries:");
    foreach (var e in entries.Take(40))
        Console.WriteLine($"  {e.Name,-32} {e.Length,9:n0}");
    return 0;
}

if (args.Contains("--types"))
{
    foreach (var grp in gob.Entries
        .GroupBy(e => Path.GetExtension(e.NormalizedName))
        .OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {grp.Key,-8} {grp.Count(),5}   {grp.Sum(e => (long)e.Length),12:n0} bytes");
    return 0;
}

if (jklName is null)
{
    foreach (var e in jkls)
        Console.WriteLine($"  {e.Name,-32} {e.Length,9:n0} bytes");
    Console.WriteLine("\nRe-run with --jkl <name> to parse and render one.");
    return 0;
}

var entry = gob.Find(jklName) ?? jkls.FirstOrDefault(e => e.NormalizedName.Contains(jklName.ToLowerInvariant()));
if (entry is null) { Console.Error.WriteLine($"JKL '{jklName}' not found"); return 2; }

Console.WriteLine($"Parsing {entry.Name} ({entry.Length:n0} bytes)…");
var text = gob.ReadText(entry);
Level level = JklParser.Parse(text);

int surfaces = level.Sectors.Sum(s => s.Surfaces.Count);
int verts = level.Sectors.Sum(s => s.Vertices.Count);
Console.WriteLine($"  kind={level.Kind} version={level.Header.Version}");
Console.WriteLine($"  {level.Sectors.Count} sectors, {surfaces} surfaces, {verts} sector-verts, {level.Things.Count} things");

var mesh = SceneBuilder.FromLevel(level);
Console.WriteLine($"  mesh: {mesh.Vertices.Count:n0} verts, {mesh.TriangleCount:n0} tris");
if (mesh.IsEmpty) { Console.Error.WriteLine("Empty mesh — nothing to render"); return 3; }

const uint W = 960, H = 720;
var (center, radius) = Bounds(level);
var eye = center + new Vec3(radius * 0.9, -radius * 1.4, radius * 0.8);
var camera = Camera.LookingAt(eye, center);
var mvp = camera.ViewProjection((double)W / H);

using var ctx = VulkanContext.Create("SED GobTool");
using var device = VulkanDevice.Create(ctx);
using var renderer = new SceneRenderer(device);
renderer.SetMesh(mesh);
var pixels = renderer.Render(mvp, camera.Position, W, H);
PngWriter.Write(pngPath, pixels, (int)W, (int)H);
Console.WriteLine($"Rendered → {Path.GetFullPath(pngPath)}");
return 0;

static (Vec3 center, double radius) Bounds(Level level)
{
    var box = Box.Empty;
    foreach (var s in level.Sectors)
        foreach (var v in s.Vertices)
            box.Encapsulate(v.Position);
    if (box.Max.X < box.Min.X) return (Vec3.Zero, 1);
    var c = box.Center;
    double r = (box.Max - c).Length;
    return (c, r <= 0 ? 1 : r);
}

static string? OptValue(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
