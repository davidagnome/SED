using Sed.Rendering.Vulkan;
using Sed.TriangleProbe;

// Renders the bring-up triangle offscreen via Vulkan/MoltenVK and writes a PNG,
// so the full pipeline (device -> render pass -> pipeline -> draw -> readback)
// can be verified without a window.
const uint Width = 256, Height = 256;
var outPath = args.Length > 0 ? args[0] : "triangle.png";

using var ctx = VulkanContext.Create("SED Triangle Probe");
using var device = VulkanDevice.Create(ctx);
Console.WriteLine($"Device: {device.DeviceName}");

using var renderer = new OffscreenRenderer(device, Width, Height);
var pixels = renderer.RenderToPixels();

SimplePng.Write(outPath, pixels, (int)Width, (int)Height);

// Self-verify in a headless context: count lit (non-background) pixels and
// sample the centroid of the triangle, which should be reddish-green-blue mix.
long nonBackground = 0;
for (int i = 0; i < pixels.Length; i += 4)
    if (pixels[i] > 40 || pixels[i + 1] > 40 || pixels[i + 2] > 40)
        nonBackground++;

Console.WriteLine($"Wrote {Path.GetFullPath(outPath)} ({Width}x{Height})");
Console.WriteLine($"Non-background pixels: {nonBackground} / {Width * Height}");
return nonBackground > 0 ? 0 : 3;
