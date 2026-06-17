using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sed.Rendering.Vulkan;

namespace Sed.App;

/// <summary>
/// Bridges the Vulkan offscreen renderer to an Avalonia <see cref="Bitmap"/>.
///
/// For now the 3D viewport renders to an offscreen image and is blitted into a
/// <see cref="WriteableBitmap"/>. This is the portable embedding path and is the
/// foundation for a live, resizable viewport (re-render on size/camera change).
/// </summary>
public static class VulkanViewport
{
    public static WriteableBitmap RenderTriangle(uint width, uint height)
    {
        using var ctx = VulkanContext.Create("SED");
        using var device = VulkanDevice.Create(ctx);
        using var renderer = new OffscreenRenderer(device, width, height);
        var pixels = renderer.RenderToPixels();
        return ToBitmap(pixels, (int)width, (int)height);
    }

    /// <summary>Copies tightly-packed RGBA8 pixels into a new Avalonia bitmap.</summary>
    public static WriteableBitmap ToBitmap(byte[] rgba, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var fb = bitmap.Lock();
        int srcStride = width * 4;
        for (int y = 0; y < height; y++)
        {
            var dst = fb.Address + y * fb.RowBytes;
            Marshal.Copy(rgba, y * srcStride, dst, srcStride);
        }
        return bitmap;
    }
}
