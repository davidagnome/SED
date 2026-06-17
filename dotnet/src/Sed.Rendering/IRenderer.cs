using Sed.Core.Model;

namespace Sed.Rendering;

/// <summary>
/// Backend-agnostic renderer contract. The original SED had three backends
/// (software, OpenGL, DirectDraw/D3D7); this rewrite targets a single Vulkan
/// backend (MoltenVK on macOS/arm64, native Vulkan elsewhere) behind this seam.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Initializes the backend (device, swapchain, pipelines).</summary>
    void Initialize();

    /// <summary>Uploads/refreshes geometry derived from the level model.</summary>
    void SetScene(Level level);

    /// <summary>Renders one frame from the given camera into the current surface.</summary>
    void RenderFrame(Camera camera, int widthPx, int heightPx);
}
