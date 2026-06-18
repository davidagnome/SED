using System.Text;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.Jkl;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

// Parses a JKL level (a real file if given, otherwise a generated cube level)
// and renders it through the model -> mesh -> Vulkan path to a PNG.
const uint Width = 640, Height = 480;
var path = args.FirstOrDefault(a => a.EndsWith(".jkl", StringComparison.OrdinalIgnoreCase));
var outPath = args.FirstOrDefault(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ?? "/tmp/sed_jkl.png";

Level level;
if (path is not null)
{
    Console.WriteLine($"Parsing {path}");
    level = JklParser.ParseFile(path);
}
else
{
    Console.WriteLine("No .jkl given — parsing a generated cube level");
    level = JklParser.Parse(CubeJkl());
}

int surfaces = level.Sectors.Sum(s => s.Surfaces.Count);
Console.WriteLine($"Parsed: {level.Sectors.Count} sectors, {surfaces} surfaces, {level.Things.Count} things, kind={level.Kind}");

var mesh = SceneBuilder.FromLevel(level);
Console.WriteLine($"Mesh: {mesh.Vertices.Count} verts, {mesh.TriangleCount} tris");
if (mesh.IsEmpty) { Console.Error.WriteLine("Empty mesh"); return 4; }

// Frame the geometry automatically.
var (center, radius) = Bounds(level);
var eye = center + new Vec3(radius * 1.6, -radius * 2.0, radius * 1.4);
var camera = Camera.LookingAt(eye, center);
var mvp = camera.ViewProjection((double)Width / Height);

using var ctx = VulkanContext.Create("SED JKL Probe");
using var device = VulkanDevice.Create(ctx);
using var renderer = new SceneRenderer(device);
renderer.SetMesh(mesh);
var pixels = renderer.Render(mvp, camera.Position, Width, Height);

PngWriter.Write(outPath, pixels, (int)Width, (int)Height);
long lit = 0;
for (int i = 0; i < pixels.Length; i += 4)
    if (pixels[i] > 30 || pixels[i + 1] > 30 || pixels[i + 2] > 30) lit++;
Console.WriteLine($"Wrote {Path.GetFullPath(outPath)} — lit {lit}/{Width * Height}");
return lit > 0 ? 0 : 3;

static (Vec3 center, double radius) Bounds(Level level)
{
    var box = Box.Empty;
    foreach (var s in level.Sectors)
        foreach (var v in s.Vertices)
            box.Encapsulate(v.Position);
    var c = box.Center;
    double r = (box.Max - c).Length;
    return (c, r <= 0 ? 1 : r);
}

// Generates a JKL describing a unit cube as one sector with six quad surfaces.
static string CubeJkl()
{
    Vec3[] p =
    {
        new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1),
        new(-1,-1, 1), new(1,-1, 1), new(1,1, 1), new(-1,1, 1),
    };
    int[][] faces =
    {
        new[]{0,1,2,3}, new[]{4,5,6,7}, new[]{0,1,5,4},
        new[]{3,2,6,7}, new[]{0,3,7,4}, new[]{1,2,6,5},
    };

    var sb = new StringBuilder();
    sb.AppendLine("SECTION: HEADER").AppendLine("VERSION 1").AppendLine("END").AppendLine();
    sb.AppendLine("SECTION: GEORESOURCE");
    sb.AppendLine("WORLD COLORMAPS 1").AppendLine("0:\tdflt.cmp");
    sb.AppendLine($"WORLD VERTICES {p.Length}");
    for (int i = 0; i < p.Length; i++)
        sb.AppendLine($"{i}:\t{p[i].X:0.0} {p[i].Y:0.0} {p[i].Z:0.0}");
    sb.AppendLine("WORLD TEXTURE VERTICES 1").AppendLine("0:\t0.0 0.0");
    sb.AppendLine("WORLD ADJOINS 0");
    sb.AppendLine($"WORLD SURFACES {faces.Length}");
    for (int i = 0; i < faces.Length; i++)
    {
        var verts = string.Join(' ', faces[i].Select(v => $"{v},0"));
        var inten = string.Join(' ', faces[i].Select(_ => "0.7"));
        sb.AppendLine($"{i}:\t0 0 0 3 0 0 -1 0.7 4 {verts} {inten}");
    }
    // Surface normals: one line per surface (skipped by the loader, but present
    // in real files and consumed by count).
    for (int i = 0; i < faces.Length; i++)
        sb.AppendLine($"{i}:\t0.0 0.0 1.0");
    sb.AppendLine("END").AppendLine();

    sb.AppendLine("SECTION: SECTORS").AppendLine("World sectors 1").AppendLine("SECTOR 0");
    sb.AppendLine("FLAGS\t0x0");
    sb.AppendLine($"VERTICES {p.Length}");
    for (int i = 0; i < p.Length; i++) sb.AppendLine($"{i}:\t{i}");
    sb.AppendLine($"SURFACES 0 {faces.Length}").AppendLine("END");

    return sb.ToString();
}
