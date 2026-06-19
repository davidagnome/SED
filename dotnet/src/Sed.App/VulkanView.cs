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
    private Vertex? _selectedVertex;
    private Sector? _activeSector;        // vertices of this sector are shown for editing

    private TextureLookup? _textures;
    private Func<string, ThreeDoModel?>? _models;

    /// <summary>Material names available for assignment (for cycling a surface's material).</summary>
    public List<string> Materials { get; set; } = new();

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

    public void SetLevel(Level level, TextureLookup? textures = null, Func<string, ThreeDoModel?>? models = null,
        byte[]? paletteRgb = null, byte[]? lightTable = null)
    {
        if (_renderer is null) return;
        _level = level;
        _textures = textures;
        _models = models;
        _selectedThing = null;
        _selectedSurface = null;
        _selectedVertex = null;
        _activeSector = null;
        _renderer.SetSelection(null);
        History.Clear();

        if (paletteRgb is not null && lightTable is not null)
            _renderer.SetColormap(paletteRgb, lightTable);
        _renderer.SetSky(level.Header.CeilingSky.Height, (float)level.Header.CeilingSky.Offset.X, (float)level.Header.CeilingSky.Offset.Y);

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

    private static readonly ColorF MarkerColor = new(0.2f, 0.9f, 1f);    // cyan things
    private static readonly ColorF VertexColor = new(1f, 0.5f, 0.1f);    // orange vertices
    private static readonly ColorF SelectColor = new(1f, 0.9f, 0.2f);    // yellow selection

    private void RefreshMarkers()
    {
        if (_renderer is null) return;
        var markers = new Mesh();
        foreach (var thing in _markerThings)
            markers.Append(SceneBuilder.BuildMarker(thing.Position, _markerSize, MarkerColor));
        if (_activeSector is not null)
            foreach (var v in _activeSector.Vertices)
                markers.Append(SceneBuilder.BuildMarker(v.Position, _markerSize * 0.7, VertexColor));
        _renderer.SetMarkers(markers.IsEmpty ? null : markers);
    }

    /// <summary>Rebuilds the rendered geometry from the (possibly edited) level, reusing textures.</summary>
    private void RebuildScene()
    {
        if (_renderer is null || _level is null || _textures is null) return;
        var assembler = new SceneAssembler();
        assembler.AddLevel(_level);
        _markerThings = _models is not null ? assembler.AddThings(_level, _models) : _level.Things.ToList();
        _renderer.UpdateGeometry(assembler.Build());
    }

    private void OnHistoryChanged()
    {
        RebuildScene();          // geometry edits change the mesh
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
        var deg = 180.0 / System.Math.PI;
        var pixels = _renderer.Render(mvp, _camPos, new Vec3(_yaw * deg, _pitch * deg, 0), w, h);
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

        if (e.Key == Key.B) { CycleBrightness(); e.Handled = true; return; }

        if (e.Key == Key.Insert)
        {
            if (_selectedSurface is not null) History.Do(new InsertSurfaceVertexCommand(_selectedSurface, 0));
            else CreateThing();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; return; }
        if (e.Key == Key.N) { CreateSector(); e.Handled = true; return; }
        if (e.Key == Key.OemOpenBrackets) { Rotate(-15); e.Handled = true; return; }
        if (e.Key == Key.OemCloseBrackets) { Rotate(15); e.Handled = true; return; }
        if (e.Key == Key.OemComma) { ScaleSelection(0.9); e.Handled = true; return; }
        if (e.Key == Key.OemPeriod) { ScaleSelection(1.0 / 0.9); e.Handled = true; return; }
        if (e.Key == Key.M) { CycleMaterial(); e.Handled = true; return; }

        if (HasSelection && TryMoveDelta(e.Key, out var delta))
        {
            if (_selectedVertex is not null) History.Do(new MoveVertexCommand(_selectedVertex, delta));
            else if (_selectedSurface is not null) History.Do(new MoveSurfaceCommand(_selectedSurface, delta));
            else if (_selectedThing is not null) History.Do(new MoveThingCommand(_selectedThing, delta));
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

    private float _brightness;

    /// <summary>Cycles editor brightness: real → medium → full → real.</summary>
    public void CycleBrightness()
    {
        _brightness = _brightness < 0.25f ? 0.6f : _brightness < 0.85f ? 1f : 0f;
        _renderer?.SetBrightness(_brightness);
        RenderFrame();
        var label = _brightness == 0 ? "real lighting" : _brightness >= 1f ? "full bright" : "medium";
        SelectionChanged?.Invoke($"Brightness: {label} (press B to cycle)");
    }

    private void CreateThing()
    {
        if (_level is null || _renderer is null) return;
        Thing t;
        if (_selectedThing is { } sel)
        {
            t = new Thing
            {
                Template = sel.Template, Name = sel.Name, Sector = sel.Sector,
                Position = sel.Position + new Vec3(_moveSpeed * 4, 0, 0),
                Pitch = sel.Pitch, Yaw = sel.Yaw, Roll = sel.Roll,
            };
            foreach (var (k, v) in sel.Values) t.Values[k] = v;
        }
        else
        {
            t = new Thing
            {
                Template = string.Empty, Name = "newthing",
                Position = _camPos + Cam().Forward * (_moveSpeed * 15),
                Sector = _activeSector ?? _level.Sectors.FirstOrDefault(),
            };
        }
        _selectedThing = t; _selectedSurface = null; _selectedVertex = null;
        History.Do(new CreateThingCommand(_level, t));
    }

    /// <summary>Creates a default box-room sector in front of the camera.</summary>
    public void CreateSector()
    {
        if (_level is null || _renderer is null) return;
        var sample = _level.Sectors.SelectMany(s => s.Surfaces).FirstOrDefault(s => !string.IsNullOrEmpty(s.Material));
        var center = _camPos + Cam().Forward * (_moveSpeed * 15);
        double size = System.Math.Max(0.5, _moveSpeed * 8);
        var sector = SectorFactory.CreateBox(_level, center, size, sample?.Material ?? string.Empty, sample?.MaterialIndex ?? 0);
        _selectedSurface = null; _selectedVertex = null; _selectedThing = null;
        _activeSector = sector;
        History.Do(new CreateSectorCommand(_level, sector));
    }

    /// <summary>Deletes the sector of the current selection (surface/vertex) or the active sector.</summary>
    public void DeleteSector()
    {
        if (_level is null) return;
        var sec = _selectedSurface?.Sector ?? _activeSector;
        if (sec is null) { SelectionChanged?.Invoke("Select a surface first, then Delete Sector"); return; }
        _selectedSurface = null; _selectedVertex = null; _activeSector = null;
        History.Do(new DeleteSectorCommand(_level, sec));
    }

    /// <summary>Rotates the selected thing's yaw, or the active sector's geometry, about Z.</summary>
    private void Rotate(double degrees)
    {
        if (_selectedThing is { } t) { History.Do(new RotateThingCommand(t, degrees)); return; }
        if (_activeSector is { } sec && sec.Vertices.Count > 0)
            History.Do(new TransformVerticesCommand(sec.Vertices,
                TransformVerticesCommand.RotateZ(SectorCenter(sec), degrees * System.Math.PI / 180.0), "Rotate sector"));
    }

    /// <summary>Scales the active sector's geometry about its centre.</summary>
    private void ScaleSelection(double factor)
    {
        if (_activeSector is { } sec && sec.Vertices.Count > 0)
            History.Do(new TransformVerticesCommand(sec.Vertices,
                TransformVerticesCommand.Scale(SectorCenter(sec), factor), "Scale sector"));
    }

    /// <summary>Cycles the selected surface's material to the next available one.</summary>
    private void CycleMaterial()
    {
        if (_selectedSurface is not { } s || Materials.Count == 0) return;
        int cur = Materials.FindIndex(m => string.Equals(m, s.Material, StringComparison.OrdinalIgnoreCase));
        int next = (cur + 1) % Materials.Count;
        History.Do(new SetMaterialCommand(s, Materials[next], next));
        SelectionChanged?.Invoke($"Material → {Materials[next]} (M to cycle)");
    }

    private static Vec3 SectorCenter(Sector sector)
    {
        var sum = Vec3.Zero;
        foreach (var v in sector.Vertices) sum += v.Position;
        return sum * (1.0 / sector.Vertices.Count);
    }

    private void DeleteSelected()
    {
        if (_level is null) return;
        if (_selectedVertex is { } v && _activeSector is { } sec)
        {
            _selectedVertex = null;
            History.Do(new DeleteVertexCommand(sec, v));
        }
        else if (_selectedThing is { } t)
        {
            _selectedThing = null;
            History.Do(new DeleteThingCommand(_level, t));
        }
    }

    private bool HasSelection => _selectedVertex is not null || _selectedSurface is not null || _selectedThing is not null;

    private bool TryMoveDelta(Key key, out Vec3 delta)
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

        var thingHit = Picker.PickThing(_level, ray, _markerSize * 1.8);
        var vertexHit = Picker.PickVertex(_level, ray, _markerSize);
        var surfaceHit = Picker.Pick(_level, ray);

        _selectedThing = null;
        _selectedSurface = null;
        _selectedVertex = null;

        double tThing = thingHit?.Distance ?? double.MaxValue;
        double tVertex = vertexHit?.Distance ?? double.MaxValue;
        double tSurface = surfaceHit?.Distance ?? double.MaxValue;

        if (thingHit is not null && tThing <= tVertex && tThing <= tSurface)
        {
            _selectedThing = thingHit.Thing;
        }
        else if (vertexHit is not null && tVertex <= tSurface + 0.05)
        {
            _selectedVertex = vertexHit.Vertex;
            _activeSector = vertexHit.Sector;
        }
        else if (surfaceHit is not null)
        {
            _selectedSurface = surfaceHit.Surface;
            _activeSector = surfaceHit.Sector;   // show this sector's vertices for editing
        }

        RefreshMarkers();
        UpdateSelectionHighlight();
        RenderFrame();
        SelectionChanged?.Invoke(SelectionText());
    }

    private void UpdateSelectionHighlight()
    {
        if (_renderer is null) return;
        if (_selectedVertex is not null)
            _renderer.SetSelection(SceneBuilder.BuildMarker(_selectedVertex.Position, _markerSize * 1.1, SelectColor));
        else if (_selectedThing is not null)
            _renderer.SetSelection(SceneBuilder.BuildMarker(_selectedThing.Position, _markerSize * 1.3, SelectColor));
        else if (_selectedSurface is not null)
            _renderer.SetSelection(BuildSurfaceHighlight(_selectedSurface));
        else
            _renderer.SetSelection(null);
    }

    private string SelectionText()
    {
        if (_selectedVertex is { } v)
            return $"Vertex @ {v.Position}  — arrows/PgUp-PgDn move, click a face to pick another, Ctrl+Z undo";
        if (_selectedThing is { } t)
        {
            var name = t.Name.Length > 0 ? t.Name : "?";
            return $"Thing #{t.Num} '{name}' @ {t.Position}  — arrows/PgUp-PgDn to move, Ctrl+Z undo";
        }
        if (_selectedSurface is { } s)
        {
            var mat = string.IsNullOrEmpty(s.Material) ? "(no material)" : s.Material;
            return $"Sector {s.Sector.Num}, surface {s.Num} — {mat}  (orange dots = vertices; arrows move whole surface)";
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
