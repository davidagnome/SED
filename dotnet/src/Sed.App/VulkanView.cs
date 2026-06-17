using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

namespace Sed.App;

/// <summary>
/// A live Vulkan 3D viewport. Owns the Vulkan device + scene renderer, renders
/// the level mesh to an offscreen image, and blits it into a bitmap drawn to the
/// control bounds. Re-renders on resize and on orbit (mouse drag).
/// </summary>
public sealed class VulkanView : Control
{
    private readonly VulkanContext? _ctx;
    private readonly VulkanDevice? _device;
    private readonly SceneRenderer? _renderer;
    private readonly string? _error;

    // Orbit camera state (spherical around the target).
    private Vec3 _target = Vec3.Zero;
    private double _radius = 6.0;
    private double _azimuth = 0.6;
    private double _elevation = 0.5;

    private WriteableBitmap? _bitmap;
    private PixelSize _lastSize;
    private Point? _lastPointer;

    public VulkanView(Level level)
    {
        try
        {
            _ctx = VulkanContext.Create("SED");
            _device = VulkanDevice.Create(_ctx);
            _renderer = new SceneRenderer(_device);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }

        ClipToBounds = true;
        SetLevel(level);
    }

    /// <summary>
    /// Loads a level, frames the camera to its bounds, and redraws. When a
    /// <paramref name="textures"/> lookup is given the level is drawn textured;
    /// otherwise it falls back to flat per-surface hues.
    /// </summary>
    public void SetLevel(Level level, TextureLookup? textures = null)
    {
        if (_renderer is null) return;
        if (textures is not null)
            _renderer.SetScene(SceneBuilder.BuildScene(level), textures);
        else
            _renderer.SetMesh(SceneBuilder.FromLevel(level));

        var box = Box.Empty;
        foreach (var sector in level.Sectors)
            foreach (var v in sector.Vertices)
                box.Encapsulate(v.Position);

        if (box.Max.X >= box.Min.X)
        {
            _target = box.Center;
            double r = (box.Max - _target).Length;
            _radius = r <= 0 ? 6.0 : r * 2.4;
        }
        RenderFrame();
    }

    private Vec3 Eye()
    {
        double ce = System.Math.Cos(_elevation), se = System.Math.Sin(_elevation);
        var offset = new Vec3(System.Math.Sin(_azimuth) * ce, System.Math.Cos(_azimuth) * ce, se) * _radius;
        return _target + offset;
    }

    private void RenderFrame()
    {
        if (_renderer is null || _lastSize.Width < 1 || _lastSize.Height < 1) return;

        uint w = (uint)_lastSize.Width;
        uint h = (uint)_lastSize.Height;

        var camera = Camera.LookingAt(Eye(), _target);
        var mvp = camera.ViewProjection((double)w / h);
        var pixels = _renderer.Render(mvp, w, h);

        _bitmap = VulkanViewport.ToBitmap(pixels, (int)w, (int)h);
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var px = new PixelSize(
            (int)System.Math.Max(1, finalSize.Width * scale),
            (int)System.Math.Max(1, finalSize.Height * scale));
        if (px != _lastSize)
        {
            _lastSize = px;
            RenderFrame();
        }
        return result;
    }

    public override void Render(DrawingContext context)
    {
        if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, new Rect(Bounds.Size));
            return;
        }

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x0d, 0x0d, 0x14)), new Rect(Bounds.Size));
        if (_error is not null)
        {
            var text = new FormattedText($"Vulkan unavailable:\n{_error}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 14, Brushes.OrangeRed);
            context.DrawText(text, new Point(20, 20));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_lastPointer is not { } last) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - last.X;
        var dy = pos.Y - last.Y;
        _lastPointer = pos;

        _azimuth -= dx * 0.01;
        _elevation = System.Math.Clamp(_elevation + dy * 0.01, -1.5, 1.5);
        RenderFrame();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _radius = System.Math.Clamp(_radius - e.Delta.Y * 0.4, 2.0, 40.0);
        RenderFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _renderer?.Dispose();
        _device?.Dispose();
        _ctx?.Dispose();
    }
}
