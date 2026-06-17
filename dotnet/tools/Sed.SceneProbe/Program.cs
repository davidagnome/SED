using Sed.Core.Math;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

// Renders the sample cube (built through the level model) with perspective MVP
// and depth testing, saving a PNG for verification.
const uint Width = 640, Height = 480;
var outPath = args.Length > 0 ? args[0] : "/tmp/sed_scene.png";

var level = SampleScene.CreateCube();
var mesh = SceneBuilder.FromLevel(level);
Console.WriteLine($"Mesh: {mesh.Vertices.Count} verts, {mesh.TriangleCount} tris");

var camera = Camera.LookingAt(new Vec3(3.5, -4.0, 2.8), Vec3.Zero);
var mvp = camera.ViewProjection((double)Width / Height);

using var ctx = VulkanContext.Create("SED Scene Probe");
using var device = VulkanDevice.Create(ctx);
using var renderer = new SceneRenderer(device);
renderer.SetMesh(mesh);
var pixels = renderer.Render(mvp, Width, Height);

PngWriter.Write(outPath, pixels, (int)Width, (int)Height);

long lit = 0;
for (int i = 0; i < pixels.Length; i += 4)
    if (pixels[i] > 30 || pixels[i + 1] > 30 || pixels[i + 2] > 30) lit++;
Console.WriteLine($"Wrote {Path.GetFullPath(outPath)} — lit pixels {lit}/{Width * Height}");
return lit > 0 ? 0 : 3;
