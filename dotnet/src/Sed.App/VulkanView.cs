using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;
using Sed.Formats.ThreeDo;
using Sed.Rendering;
using Sed.Rendering.Vulkan;

namespace Sed.App;

/// <summary>
/// A live Vulkan 3D viewport with a fly camera (WASD + Q/E, mouse-look on drag,
/// wheel to dolly) and click-to-pick surface selection. Renders the level to an
/// offscreen image blitted into a bitmap.
/// </summary>
public sealed class VulkanView : Control
{
    private readonly VulkanContext? _ctx;
    private readonly VulkanDevice? _device;
    private readonly SceneRenderer? _renderer;
    private readonly string? _error;

    private Level? _level;

    // Fly camera state
    private Vec3 _camPos;
    private double _yaw, _pitch;
    private double _fov = 65, _near = 0.05, _far = 500;
    private double _moveSpeed = 0.2;

    private WriteableBitmap? _bitmap;
    private PixelSize _lastSize;

    private readonly HashSet<Key> _pressed = new();
    private readonly DispatcherTimer _moveTimer;
    private Point? _lastPointer;
    private Point _pressOrigin;
    private bool _dragging;

    // Selection + editing
    private double _markerSize = 0.1;
    private Thing? _selectedThing;
    private Surface? _selectedSurface;

    /// <summary>Undo/redo history for edits made in this viewport.</summary>
    public EditHistory History { get; } = new();

    /// <summary>Raised when the selection changes; argument is a human-readable description (or null).</summary>
    public Action<string?>? SelectionChanged;

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
        Focusable = true;
        _moveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _moveTimer.Tick += (_, _) => MoveTick();
        History.Changed += OnHistoryChanged;
        SetLevel(level);
    }

    private List<Thing> _markerThings = new();

    public void SetLevel(Level level, TextureLookup? textures = null, Func<string, ThreeDoModel?>? models = null)
    {
        if (_renderer is null) return;
        _level = level;
        _selectedThing = null;
        _selectedSurface = null;
        _renderer.SetSelection(null);
        History.Clear();

        if (textures is not null)
        {
            var assembler = new SceneAssembler();
            assembler.AddLevel(level);
            // Things with a 3DO model render as geometry; the rest get markers.
            _markerThings = models is not null ? assembler.AddThings(level, models) : level.Things.ToList();
            _renderer.SetScene(assembler.Build(), textures);
        }
        else
        {
            _renderer.SetMesh(SceneBuilder.FromLevel(level));
            _markerThings = level.Things.ToList();
        }

        FrameLevel(level);
        _markerSize = System.Math.Max(0.02, _moveSpeed * 0.5);
        RefreshMarkers();
        RenderFrame();
    }

    private static readonly ColorF MarkerColor = new(0.2f, 0.9f, 1f);   // cyan things
    private static readonly ColorF SelectColor = new(1f, 0.9f, 0.2f);   // yellow selection

    private void RefreshMarkers()
    {
        if (_renderer is null) return;
        _renderer.SetMarkers(_markerThings.Count > 0
            ? SceneBuilder.BuildThingMarkers(_markerThings, _markerSize, MarkerColor)
            : null);
    }

    private void OnHistoryChanged()
    {
        RefreshMarkers();
        UpdateSelectionHighlight();
        RenderFrame();
        SelectionChanged?.Invoke(SelectionText());
    }

    private Camera Cam() => new()
    {
        Position = _camPos, Yaw = _yaw, Pitch = _pitch,
        FieldOfViewDegrees = _fov, NearPlane = _near, FarPlane = _far,
    };

    private void FrameLevel(Level level)
    {
        var box = Box.Empty;
        foreach (var sector in level.Sectors)
            foreach (var v in sector.Vertices)
                box.Encapsulate(v.Position);
        if (box.Max.X < box.Min.X) { _camPos = new Vec3(0, -6, 0); _yaw = 0; _pitch = 0; return; }

        var center = box.Center;
        double radius = System.Math.Max(1.0, (box.Max - center).Length);
        _camPos = center + new Vec3(radius * 0.4, -radius * 1.3, radius * 0.5);
        var dir = (center - _camPos).Normalized();
        _pitch = System.Math.Asin(System.Math.Clamp(dir.Z, -1, 1));
        _yaw = System.Math.Atan2(dir.X, dir.Y);
        _moveSpeed = radius * 0.015;
        _far = radius * 6;
        _near = System.Math.Max(0.01, radius * 0.001);
    }

    private void RenderFrame()
    {
        if (_renderer is null || _lastSize.Width < 1 || _lastSize.Height < 1) return;
        uint w = (uint)_lastSize.Width, h = (uint)_lastSize.Height;
        var mvp = Cam().ViewProjection((double)w / h);
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
        if (px != _lastSize) { _lastSize = px; RenderFrame(); }
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

    // ---- keyboard fly movement ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { History.Redo(); e.Handled = true; return; }
            if (e.Key == Key.Z) { History.Undo(); e.Handled = true; return; }
            if (e.Key == Key.Y) { History.Redo(); e.Handled = true; return; }
        }

        if (_selectedThing is not null && TryThingMoveDelta(e.Key, out var delta))
        {
            History.Do(new MoveThingCommand(_selectedThing, delta));
            e.Handled = true;
            return;
        }

        if (IsMoveKey(e.Key))
        {
            _pressed.Add(e.Key);
            if (!_moveTimer.IsEnabled) _moveTimer.Start();
            e.Handled = true;
        }
    }

    private bool TryThingMoveDelta(Key key, out Vec3 delta)
    {
        delta = Vec3.Zero;
        double step = System.Math.Max(_markerSize * 2, _moveSpeed);
        var cam = Cam();
        var fwd = new Vec3(cam.Forward.X, cam.Forward.Y, 0).Normalized();
        var right = new Vec3(cam.Right.X, cam.Right.Y, 0).Normalized();
        switch (key)
        {
            case Key.Right: delta = right * step; break;
            case Key.Left: delta = right * -step; break;
            case Key.Up: delta = fwd * step; break;
            case Key.Down: delta = fwd * -step; break;
            case Key.PageUp: delta = Camera.Up * step; break;
            case Key.PageDown: delta = Camera.Up * -step; break;
            default: return false;
        }
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _pressed.Remove(e.Key);
        if (_pressed.Count == 0) _moveTimer.Stop();
    }

    private static bool IsMoveKey(Key k) =>
        k is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E;

    private void MoveTick()
    {
        if (_pressed.Count == 0) { _moveTimer.Stop(); return; }
        var cam = Cam();
        var move = Vec3.Zero;
        if (_pressed.Contains(Key.W)) move += cam.Forward;
        if (_pressed.Contains(Key.S)) move -= cam.Forward;
        if (_pressed.Contains(Key.D)) move += cam.Right;
        if (_pressed.Contains(Key.A)) move -= cam.Right;
        if (_pressed.Contains(Key.E)) move += Camera.Up;
        if (_pressed.Contains(Key.Q)) move -= Camera.Up;

        if (move.LengthSquared > 0)
        {
            _camPos += move.Normalized() * _moveSpeed;
            RenderFrame();
        }
    }

    // ---- mouse look + picking ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        _lastPointer = e.GetPosition(this);
        _pressOrigin = _lastPointer.Value;
        _dragging = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_lastPointer is not { } last) return;
        var pos = e.GetPosition(this);
        double dx = pos.X - last.X, dy = pos.Y - last.Y;
        _lastPointer = pos;

        if (System.Math.Abs(pos.X - _pressOrigin.X) + System.Math.Abs(pos.Y - _pressOrigin.Y) > 3)
            _dragging = true;

        if (_dragging)
        {
            _yaw -= dx * 0.005;
            _pitch = System.Math.Clamp(_pitch - dy * 0.005, -1.5, 1.5);
            RenderFrame();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_dragging) PickAt(e.GetPosition(this));
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _camPos += Cam().Forward * (e.Delta.Y * _moveSpeed * 6);
        RenderFrame();
    }

    private void PickAt(Point p)
    {
        if (_renderer is null || _level is null || _lastSize.Width < 1) return;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var ray = Picker.ScreenPointToRay(Cam(), p.X * scale, p.Y * scale, _lastSize.Width, _lastSize.Height);

        var surfaceHit = Picker.Pick(_level, ray);
        var thingHit = Picker.PickThing(_level, ray, _markerSize * 1.8);

        _selectedThing = null;
        _selectedSurface = null;
        if (thingHit is not null && (surfaceHit is null || thingHit.Distance <= surfaceHit.Distance))
            _selectedThing = thingHit.Thing;
        else
            _selectedSurface = surfaceHit?.Surface;

        UpdateSelectionHighlight();
        RenderFrame();
        SelectionChanged?.Invoke(SelectionText());
    }

    private void UpdateSelectionHighlight()
    {
        if (_renderer is null) return;
        if (_selectedThing is not null)
            _renderer.SetSelection(SceneBuilder.BuildMarker(_selectedThing.Position, _markerSize * 1.3, SelectColor));
        else if (_selectedSurface is not null)
            _renderer.SetSelection(BuildSurfaceHighlight(_selectedSurface));
        else
            _renderer.SetSelection(null);
    }

    private string SelectionText()
    {
        if (_selectedThing is { } t)
        {
            var name = t.Name.Length > 0 ? t.Name : "?";
            return $"Thing #{t.Num} '{name}' @ {t.Position}  — arrows/PgUp-PgDn to move, Ctrl+Z undo";
        }
        if (_selectedSurface is { } s)
        {
            var mat = string.IsNullOrEmpty(s.Material) ? "(no material)" : s.Material;
            return $"Sector {s.Sector.Num}, surface {s.Num} — {mat}";
        }
        return "Nothing selected";
    }

    private static Mesh BuildSurfaceHighlight(Surface surface)
    {
        var mesh = new Mesh();
        var n = surface.Normal;
        var c0 = surface.Corners[0].Vertex.Position;
        for (int i = 1; i + 1 < surface.Corners.Count; i++)
            mesh.AddTriangle(
                new MeshVertex(c0, n, SelectColor),
                new MeshVertex(surface.Corners[i].Vertex.Position, n, SelectColor),
                new MeshVertex(surface.Corners[i + 1].Vertex.Position, n, SelectColor));
        return mesh;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _moveTimer.Stop();
        _renderer?.Dispose();
        _device?.Dispose();
        _ctx?.Dispose();
    }
}
