using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

// Verifies geometry editing + live mesh rebuild: renders a textured cube, moves a
// vertex, then UpdateGeometry and renders again — the cube should be deformed.
const uint W = 512, H = 512;

var level = SampleScene.CreateCube(1.5);
foreach (var sector in level.Sectors)
    foreach (var surf in sector.Surfaces)
        surf.Material = "grid"; // give faces a material so the textured path includes them

// 4x4 checker of two palette indices so the surface + deformation are visible.
var indices = new byte[4 * 4];
for (int y = 0; y < 4; y++)
    for (int x = 0; x < 4; x++)
        indices[y * 4 + x] = (byte)(((x + y) & 1) == 0 ? 200 : 100);
IndexedTexture? Lookup(string m) => new IndexedTexture(4, 4, indices);

// Synthetic palette (grayscale) + identity light ramp.
var palRgb = new byte[256 * 3];
for (int i = 0; i < 256; i++) { palRgb[i * 3] = (byte)i; palRgb[i * 3 + 1] = (byte)i; palRgb[i * 3 + 2] = (byte)i; }
var ramp = new byte[64 * 256];
for (int l = 0; l < 64; l++) for (int i = 0; i < 256; i++) ramp[l * 256 + i] = (byte)i;

var camera = Camera.LookingAt(new Vec3(4, -5, 3.5), Vec3.Zero);
var mvp = camera.ViewProjection((double)W / H);

using var ctx = VulkanContext.Create("SED EditProbe");
using var device = VulkanDevice.Create(ctx);
using var renderer = new SceneRenderer(device);

renderer.SetColormap(palRgb, ramp);
var assembler = new SceneAssembler();
assembler.AddLevel(level);
renderer.SetScene(assembler.Build(), Lookup);
PngWriter.Write("/tmp/edit_before.png", renderer.Render(mvp, camera.Position, W, H), (int)W, (int)H);
Console.WriteLine("before → /tmp/edit_before.png");

// Pull the top-most vertex further up (and out) — deform the cube.
var v = level.Sectors[0].Vertices.OrderByDescending(p => p.Position.Z).First();
Console.WriteLine($"moving vertex {v.Position} up");
v.Position += new Vec3(0.8, 0.8, 2.0);

var rebuilt = new SceneAssembler();
rebuilt.AddLevel(level);
renderer.UpdateGeometry(rebuilt.Build());   // reuses the texture, re-uploads geometry
PngWriter.Write("/tmp/edit_after.png", renderer.Render(mvp, camera.Position, W, H), (int)W, (int)H);
Console.WriteLine("after  → /tmp/edit_after.png");
return 0;
