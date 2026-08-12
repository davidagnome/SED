using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sed.Core.Editing;
using Sed.Core.Math;
using Sed.Core.Model;

namespace Sed.App;

/// <summary>The orthographic projection plane of a 2D map view.</summary>
public enum MapAxis { Top, Front, Side }

/// <summary>
/// A 2D orthographic editor pane (the original SED's primary editing surface):
/// renders the level's surfaces as edges, things as markers, over a grid, with
/// pan, zoom, click-to-select, and drag-to-move (reuses the existing
/// IEditCommands). Top = XY, Front = XZ, Side = YZ.
/// </summary>
public sealed class MapView : Control
{
    private Level? _level;
    private MapAxis _axis = MapAxis.Top;
    private double _centerU, _centerV;
    private double _zoom = 20;
    private Point? _lastDrag;
    private bool _needsFrame;
    private double _gridStep = 1;

    public EditHistory? History { get; set; }
    public Action<string?>? SelectionChanged;

    /// <summary>The current editing mode — controls what hit-testing selects.</summary>
    public EditMode Mode { get; set; } = EditMode.Surface;

    private bool _snap = true;

    /// <summary>
    /// The shared multi-selection — the shell assigns the same instance the 3D
    /// view owns, so picks in either pane are one selection. Null until assigned,
    /// in which case this view is read-only.
    /// </summary>
    public SelectionSet? Selection { get; set; }

    /// <summary>Shared layer visibility; hidden layers are neither drawn nor picked.</summary>
    public LayerVisibility? Layers { get; set; }

    private bool Visible(Sector s) => Layers is null || Layers.IsVisible(s);
    private bool Visible(Thing t) => Layers is null || Layers.IsVisible(t);
    private bool Visible(Light l) => Layers is null || Layers.IsVisible(l);

    public Vertex? SelectedVertex => Selection?.PrimaryVertex;
    public Thing? SelectedThing => Selection?.PrimaryThing;
    public Surface? SelectedSurface => Selection?.PrimarySurface;
    public Sector? ActiveSector { get; private set; }

    // Pointer drag state
    private enum DragMode { None, Pan, Object, Box }
    private DragMode _dragMode;
    private Point _pressPoint;
    private Point _currentPoint;
    private Vec3 _dragStartPos;
    private Vertex? _dragVertex;
    private Thing? _dragThing;
    private Light? _dragLight;
    private bool _dragged;

    private static readonly IBrush SelectBrush = new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0x33));
    private static readonly IPen SelectPen = new Pen(new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0x33)), 2);
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xff, 0x66));

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x16));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30)), 1);
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x48)), 1);
    private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9a, 0xb0, 0xc8)), 1);
    private static readonly IPen SelectedEdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0x33)), 2);
    private static readonly IBrush ThingBrush = new SolidColorBrush(Color.FromRgb(0x33, 0xd0, 0xff));
    private static readonly IBrush VertexBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x80, 0x30));
    private static readonly IBrush InactiveVertexBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x52, 0x28));
    private static readonly IBrush LightBrush = new SolidColorBrush(Color.FromRgb(0xff, 0xf0, 0x99));
    private static readonly IBrush BoxFillBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xff, 0xe0, 0x33));
    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(0xff, 0xe0, 0x33)), 1,
        new DashStyle(new double[] { 3, 3 }, 0));

    private bool _boxExtend;
    private Point? _cursor;

    public MapView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public MapAxis Axis
    {
        get => _axis;
        set { _axis = value; FrameLevel(); InvalidateVisual(); }
    }

    public void SetLevel(Level level)
    {
        _level = level;
        ActiveSector = null;
        _needsFrame = true;
        if (Bounds.Width > 1) { FrameLevel(); _needsFrame = false; }
        InvalidateVisual();
    }

    // ---- projection helpers ----

    private (double u, double v) Project(Vec3 p) => _axis switch
    {
        MapAxis.Front => (p.X, p.Z),
        MapAxis.Side => (p.Y, p.Z),
        _ => (p.X, p.Y),
    };

    private Point ToScreen(double u, double v) => new(
        (u - _centerU) * _zoom + Bounds.Width / 2,
        Bounds.Height / 2 - (v - _centerV) * _zoom);

    private Point ToScreen(Vec3 p) { var (u, v) = Project(p); return ToScreen(u, v); }

    /// <summary>
    /// Projects a world position to a control-relative screen point using the
    /// current axis, pan and zoom. Public so overlays and interaction probes can
    /// reason about where geometry lands on screen.
    /// </summary>
    public Point WorldToScreen(Vec3 world) => ToScreen(world);

    private (double u, double v) Unproject(Point screen) => (
        (screen.X - Bounds.Width / 2) / _zoom + _centerU,
        _centerV - (screen.Y - Bounds.Height / 2) / _zoom);

    /// <summary>Converts a 2D projected delta into a 3D world delta (third axis = 0).</summary>
    private Vec3 DeltaToWorld(double du, double dv) => _axis switch
    {
        MapAxis.Front => new Vec3(du, 0, dv),
        MapAxis.Side => new Vec3(0, du, dv),
        _ => new Vec3(du, dv, 0),
    };

    /// <summary>Converts a 2D projected position to a 3D world position (third axis from reference).</summary>
    private Vec3 PosToWorld(double u, double v, double refThird) => _axis switch
    {
        MapAxis.Front => new Vec3(u, refThird, v),
        MapAxis.Side => new Vec3(refThird, u, v),
        _ => new Vec3(u, v, refThird),
    };

    private double ThirdAxis(Vec3 p) => _axis switch
    {
        MapAxis.Front => p.Y,
        MapAxis.Side => p.X,
        _ => p.Z,
    };

    // ---- frame / render ----

    private void FrameLevel()
    {
        if (_level is null) return;
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (var sector in _level.Sectors)
            foreach (var v in sector.Vertices)
            {
                var (u, w) = Project(v.Position);
                minU = System.Math.Min(minU, u); maxU = System.Math.Max(maxU, u);
                minV = System.Math.Min(minV, w); maxV = System.Math.Max(maxV, w);
            }
        if (minU > maxU) { _centerU = _centerV = 0; _zoom = 20; return; }

        _centerU = (minU + maxU) / 2;
        _centerV = (minV + maxV) / 2;
        double spanU = System.Math.Max(1e-3, maxU - minU), spanV = System.Math.Max(1e-3, maxV - minV);
        double w2 = System.Math.Max(64, Bounds.Width), h2 = System.Math.Max(64, Bounds.Height);
        _zoom = 0.85 * System.Math.Min(w2 / spanU, h2 / spanV);
    }

    public override void Render(DrawingContext context)
    {
        if (_needsFrame && Bounds.Width > 1) { FrameLevel(); _needsFrame = false; }

        var size = new Rect(Bounds.Size);
        context.FillRectangle(Background, size);
        DrawGrid(context);
        if (_level is null) return;

        // Surface edges
        var geo = new StreamGeometry();
        using (var g = geo.Open())
            foreach (var sector in _level.Sectors)
            {
                if (!Visible(sector)) continue;
                foreach (var surf in sector.Surfaces)
                {
                    if (surf.Corners.Count < 2) continue;
                    g.BeginFigure(ToScreen(surf.Corners[0].Vertex.Position), false);
                    for (int i = 1; i < surf.Corners.Count; i++)
                        g.LineTo(ToScreen(surf.Corners[i].Vertex.Position));
                    g.EndFigure(true);
                }
            }
        context.DrawGeometry(null, EdgePen, geo);

        // Highlight every selected surface, not just the primary one.
        if (Selection is { Surfaces.Count: > 0 })
        {
            var sgeo = new StreamGeometry();
            using (var g = sgeo.Open())
                foreach (var sel in Selection.Surfaces)
                {
                    if (sel.Corners.Count < 2) continue;
                    g.BeginFigure(ToScreen(sel.Corners[0].Vertex.Position), false);
                    for (int i = 1; i < sel.Corners.Count; i++)
                        g.LineTo(ToScreen(sel.Corners[i].Vertex.Position));
                    g.EndFigure(true);
                }
            context.DrawGeometry(null, SelectedEdgePen, sgeo);
        }

        // Vertex dots. In Vertex mode every visible sector shows them, so a vertex
        // can be picked without first selecting its sector; otherwise only the
        // active sector's are drawn, since a whole level's worth is unreadable.
        if (Mode == EditMode.Vertex)
        {
            foreach (var sector in _level.Sectors)
            {
                if (!Visible(sector)) continue;
                bool active = ReferenceEquals(sector, ActiveSector);
                foreach (var v in sector.Vertices)
                {
                    var c = ToScreen(v.Position);
                    var brush = Selection?.Contains(v) == true ? SelectBrush
                              : active ? VertexBrush : InactiveVertexBrush;
                    double r = active ? 3 : 2;
                    context.FillRectangle(brush, new Rect(c.X - r, c.Y - r, r * 2, r * 2));
                }
            }
        }
        else if (ActiveSector is { } actSec)
        {
            foreach (var v in actSec.Vertices)
            {
                var c = ToScreen(v.Position);
                var brush = Selection?.Contains(v) == true ? SelectBrush : VertexBrush;
                context.FillRectangle(brush, new Rect(c.X - 3, c.Y - 3, 6, 6));
            }
        }

        // Selected vertices outside the active sector still need to be visible.
        if (Selection is not null)
            foreach (var v in Selection.Vertices)
            {
                if (ActiveSector is not null && v.Sector == ActiveSector) continue;
                var c = ToScreen(v.Position);
                context.FillRectangle(SelectBrush, new Rect(c.X - 3, c.Y - 3, 6, 6));
            }

        // Things
        foreach (var thing in _level.Things)
        {
            if (!Visible(thing)) continue;
            var c = ToScreen(thing.Position);
            var brush = Selection?.Contains(thing) == true ? SelectBrush : ThingBrush;
            context.FillRectangle(brush, new Rect(c.X - 3, c.Y - 3, 6, 6));
        }

        // Lights — drawn as diamonds so they read differently from things, and
        // only in Light mode, since a level can carry hundreds.
        if (Mode == EditMode.Light)
            foreach (var light in _level.Lights)
            {
                if (!Visible(light)) continue;
                var c = ToScreen(light.Position);
                var brush = Selection?.Contains(light) == true ? SelectBrush : LightBrush;
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(c.X, c.Y - 4), true);
                    g.LineTo(new Point(c.X + 4, c.Y));
                    g.LineTo(new Point(c.X, c.Y + 4));
                    g.LineTo(new Point(c.X - 4, c.Y));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, null, diamond);
            }

        DrawBoxSelect(context);

        // Coords readout
        int selected = Selection?.Count ?? 0;
        var label = new FormattedText(
            $"{_axis} ({(_axis == MapAxis.Top ? "XY" : _axis == MapAxis.Front ? "XZ" : "YZ")})  zoom={_zoom:0.#}  " +
            $"snap={(_snap ? "on" : "off")}  grid={_gridStep:0.###}  " +
            (selected > 0 ? $"selected={selected}  " : "") +
            "drag=box-select · shift/middle-drag=pan",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.Gray);
        context.DrawText(label, new Point(6, 4));

        DrawMeasurement(context);
    }

    private void DrawGrid(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double step = 1;
        while (step * _zoom < 32) step *= 2;
        while (step * _zoom > 128) step /= 2;
        _gridStep = step;

        double left = _centerU - (w / 2) / _zoom, right = _centerU + (w / 2) / _zoom;
        double bottom = _centerV - (h / 2) / _zoom, top = _centerV + (h / 2) / _zoom;

        for (double u = System.Math.Ceiling(left / step) * step; u <= right; u += step)
        {
            double x = (u - _centerU) * _zoom + w / 2;
            context.DrawLine(System.Math.Abs(u) < step / 2 ? AxisPen : GridPen, new Point(x, 0), new Point(x, h));
        }
        for (double v = System.Math.Ceiling(bottom / step) * step; v <= top; v += step)
        {
            double y = h / 2 - (v - _centerV) * _zoom;
            context.DrawLine(System.Math.Abs(v) < step / 2 ? AxisPen : GridPen, new Point(0, y), new Point(w, y));
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var r = base.ArrangeOverride(finalSize);
        InvalidateVisual();
        return r;
    }

    // ---- hit testing ----

    private static double ScreenDist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    private Thing? HitTestThing(Point screen)
    {
        if (_level is null) return null;
        double best = 8;
        Thing? found = null;
        foreach (var thing in _level.Things)
        {
            if (!Visible(thing)) continue;
            double d = ScreenDist(ToScreen(thing.Position), screen);
            if (d < best) { best = d; found = thing; }
        }
        return found;
    }

    private Light? HitTestLight(Point screen)
    {
        if (_level is null) return null;
        double best = 8;
        Light? found = null;
        foreach (var light in _level.Lights)
        {
            if (!Visible(light)) continue;
            double d = ScreenDist(ToScreen(light.Position), screen);
            if (d < best) { best = d; found = light; }
        }
        return found;
    }

    private Vertex? HitTestVertex(Point screen)
    {
        if (_level is null) return null;
        double best = 7;
        Vertex? found = null;
        foreach (var sector in _level.Sectors)
        {
            if (!Visible(sector)) continue;
            foreach (var v in sector.Vertices)
            {
                double d = ScreenDist(ToScreen(v.Position), screen);
                if (d < best) { best = d; found = v; }
            }
        }
        return found;
    }

    private Surface? HitTestSurface(Point screen)
    {
        if (_level is null) return null;
        var (mu, mv) = Unproject(screen);
        foreach (var sector in _level.Sectors)
        {
            if (!Visible(sector)) continue;
            foreach (var surf in sector.Surfaces)
            {
                if (surf.Corners.Count < 3) continue;
                if (PointInPolygon(mu, mv, surf)) return surf;
            }
        }
        return null;
    }

    private bool PointInPolygon(double u, double v, Surface surf)
    {
        int n = surf.Corners.Count;
        bool result = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (iu, iv) = Project(surf.Corners[i].Vertex.Position);
            var (ju, jv) = Project(surf.Corners[j].Vertex.Position);
            if ((iv > v) != (jv > v) &&
                u < (ju - iu) * (v - iv) / (jv - iv + 1e-30) + iu)
                result = !result;
        }
        return result;
    }

    // ---- pointer interaction ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        _pressPoint = e.GetPosition(this);
        _currentPoint = _pressPoint;
        _dragged = false;
        e.Pointer.Capture(this);

        // Ctrl/Cmd extends the selection. Panning is middle-drag, Shift+drag or
        // Alt+drag — a plain drag on empty space rubber-band selects instead
        // (as the original editor's 2D panes do).
        bool extend = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool pan = e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed
                   || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                   || e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (pan)
        {
            _dragMode = DragMode.Pan;
            _lastDrag = _pressPoint;
            return;
        }

        Vertex? vtx = null;
        Thing? thing = null;
        Surface? surf = null;

        switch (Mode)
        {
            case EditMode.Vertex:
                vtx = HitTestVertex(_pressPoint);
                break;
            case EditMode.Thing:
                thing = HitTestThing(_pressPoint);
                break;
            case EditMode.Light:
                if (HitTestLight(_pressPoint) is { } hitLight)
                {
                    SelectLight(hitLight, extend);
                    _dragMode = DragMode.Object;
                    _dragLight = hitLight;
                    _dragVertex = null;
                    _dragThing = null;
                    _dragStartPos = hitLight.Position;
                    return;
                }
                break;
            case EditMode.Sector:
            case EditMode.Surface:
            default:
                vtx = HitTestVertex(_pressPoint);
                thing = vtx is null ? HitTestThing(_pressPoint) : null;
                surf = vtx is null && thing is null ? HitTestSurface(_pressPoint) : null;
                break;
        }

        if (vtx is not null)
        {
            SelectVertex(vtx, extend);
            // Dragging an already-selected item moves the whole selection.
            _dragMode = DragMode.Object;
            _dragVertex = vtx;
            _dragThing = null;
            _dragStartPos = vtx.Position;
            return;
        }
        if (thing is not null)
        {
            SelectThing(thing, extend);
            _dragMode = DragMode.Object;
            _dragThing = thing;
            _dragVertex = null;
            _dragStartPos = thing.Position;
            return;
        }

        if (surf is not null)
        {
            SelectSurface(surf, extend);
            _dragMode = DragMode.Pan;
            _lastDrag = _pressPoint;
            return;
        }

        // Empty space: begin a rubber-band selection. A plain press also clears,
        // so a click on nothing deselects even if the drag never starts.
        if (!extend) ClearSelection();
        _boxExtend = extend;
        _dragMode = DragMode.Box;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        _currentPoint = pos;
        _cursor = pos;
        if (_dragMode == DragMode.None) InvalidateVisual();   // refresh the readout

        if (_dragMode == DragMode.Pan)
        {
            if (_lastDrag is not { } last) return;
            _centerU -= (pos.X - last.X) / _zoom;
            _centerV += (pos.Y - last.Y) / _zoom;
            _lastDrag = pos;
            InvalidateVisual();
            return;
        }

        if (System.Math.Abs(pos.X - _pressPoint.X) + System.Math.Abs(pos.Y - _pressPoint.Y) > 3)
        {
            if (_dragMode is DragMode.Object or DragMode.Box)
            {
                _dragged = true;
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_dragMode == DragMode.Object && _dragged && History is not null)
        {
            // Compute world delta from press to release.
            var (u0, v0) = Unproject(_pressPoint);
            var (u1, v1) = Unproject(pos);
            double du = u1 - u0, dv = v1 - v0;

            if (_snap)
            {
                // Snap the final position to the grid.
                double targetU = u0 + du, targetV = v0 + dv;
                targetU = System.Math.Round((targetU - Project(_dragStartPos).u) / _gridStep) * _gridStep + Project(_dragStartPos).u;
                targetV = System.Math.Round((targetV - Project(_dragStartPos).v) / _gridStep) * _gridStep + Project(_dragStartPos).v;
                du = targetU - u0;
                dv = targetV - v0;
            }

            var delta = DeltaToWorld(du, dv);
            if (delta.LengthSquared > 1e-12)
                MoveDraggedSelection(delta);
        }
        else if (_dragMode == DragMode.Box && _dragged)
        {
            SelectInBox(new Rect(_pressPoint, pos), _boxExtend);
        }

        _dragMode = DragMode.None;
        _dragged = false;
        _lastDrag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _cursor = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        double uBefore = (p.X - Bounds.Width / 2) / _zoom + _centerU;
        double vBefore = _centerV - (p.Y - Bounds.Height / 2) / _zoom;
        _zoom = System.Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 0.05, 5000);
        _centerU = uBefore - (p.X - Bounds.Width / 2) / _zoom;
        _centerV = vBefore + (p.Y - Bounds.Height / 2) / _zoom;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.V)
        {
            Axis = _axis == MapAxis.Top ? MapAxis.Front : _axis == MapAxis.Front ? MapAxis.Side : MapAxis.Top;
            e.Handled = true;
        }
        else if (e.Key == Key.G)
        {
            _snap = !_snap;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // ---- box selection ----

    /// <summary>
    /// Adds everything inside the screen rectangle to the selection, choosing what
    /// counts by the active <see cref="Mode"/>. Points (vertices, things) must lie
    /// inside; surfaces and sectors must be *fully* contained, so a band drawn
    /// across a level does not sweep in half-visible geometry.
    /// </summary>
    private void SelectInBox(Rect box, bool extend)
    {
        if (_level is null || Selection is null) return;

        using (Selection.Defer())
        {
            if (!extend) Selection.Clear();

            switch (Mode)
            {
                case EditMode.Vertex:
                    foreach (var sector in _level.Sectors)
                        foreach (var v in Visible(sector) ? sector.Vertices : Enumerable.Empty<Vertex>())
                            if (box.Contains(ToScreen(v.Position)))
                            {
                                ActiveSector = sector;
                                Selection.Add(v);
                            }
                    break;

                case EditMode.Thing:
                    foreach (var t in _level.Things)
                        if (Visible(t) && box.Contains(ToScreen(t.Position)))
                            Selection.Add(t);
                    break;

                case EditMode.Light:
                    foreach (var l in _level.Lights)
                        if (Visible(l) && box.Contains(ToScreen(l.Position)))
                            Selection.Add(l);
                    break;

                case EditMode.Sector:
                    foreach (var sector in _level.Sectors)
                        if (Visible(sector) && sector.Vertices.Count > 0 &&
                            sector.Vertices.All(v => box.Contains(ToScreen(v.Position))))
                            Selection.Add(sector);
                    break;

                default:
                    foreach (var sector in _level.Sectors)
                        foreach (var surf in Visible(sector) ? sector.Surfaces : Enumerable.Empty<Surface>())
                            if (surf.Corners.Count > 0 &&
                                surf.Corners.All(c => box.Contains(ToScreen(c.Vertex.Position))))
                            {
                                ActiveSector = sector;
                                Selection.Add(surf);
                            }
                    break;
            }
        }

        int n = Selection.Count;
        SelectionChanged?.Invoke(n == 0
            ? "Box select: nothing inside the box"
            : $"Box select: {n} item(s) selected — arrows move all, Ctrl+click to adjust");
        InvalidateVisual();
    }

    /// <summary>
    /// Bottom-left readout: the cursor's world position, and while a box is being
    /// dragged the span it covers — the measuring tool the original's panes have.
    /// </summary>
    private void DrawMeasurement(DrawingContext context)
    {
        if (_cursor is not { } cursor) return;

        var (u, v) = Unproject(cursor);
        var axes = _axis switch
        {
            MapAxis.Front => ("X", "Z"),
            MapAxis.Side => ("Y", "Z"),
            _ => ("X", "Y"),
        };

        var text = $"{axes.Item1}={u:0.###}  {axes.Item2}={v:0.###}";

        if (_dragMode == DragMode.Box && _dragged)
        {
            var (u0, v0) = Unproject(_pressPoint);
            double du = System.Math.Abs(u - u0), dv = System.Math.Abs(v - v0);
            text += $"   Δ{axes.Item1}={du:0.###}  Δ{axes.Item2}={dv:0.###}  " +
                    $"diag={System.Math.Sqrt(du * du + dv * dv):0.###}";
        }

        var readout = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.Gray);
        context.DrawText(readout, new Point(6, Bounds.Height - 18));
    }

    private void DrawBoxSelect(DrawingContext context)
    {
        if (_dragMode != DragMode.Box || !_dragged) return;
        var rect = new Rect(_pressPoint, _currentPoint);
        context.FillRectangle(BoxFillBrush, rect);
        context.DrawRectangle(null, BoxPen, rect);
    }

    // ---- selection ----

    /// <summary>
    /// Moves everything currently selected by <paramref name="delta"/> as one undo
    /// step. If the dragged object is not itself part of the selection (a plain
    /// click on something new), only that object moves.
    /// </summary>
    private void MoveDraggedSelection(Vec3 delta)
    {
        if (History is null) return;

        var parts = new List<IEditCommand>();

        if (Selection is not null && !Selection.IsEmpty)
        {
            var verts = Selection.AffectedVertices();
            if (verts.Count > 0)
                parts.Add(new TransformVerticesCommand(verts, TransformVerticesCommand.Translate(delta),
                    verts.Count == 1 ? "Move vertex" : $"Move {verts.Count} vertices"));
            foreach (var t in Selection.Things)
                parts.Add(new MoveThingCommand(t, delta));
            foreach (var l in Selection.Lights)
                parts.Add(new MoveLightCommand(l, delta));
        }
        else if (_dragVertex is not null) parts.Add(new MoveVertexCommand(_dragVertex, delta));
        else if (_dragThing is not null) parts.Add(new MoveThingCommand(_dragThing, delta));
        else if (_dragLight is not null) parts.Add(new MoveLightCommand(_dragLight, delta));

        if (parts.Count == 0) return;
        History.Do(parts.Count == 1 ? parts[0] : new CompositeCommand($"Move {parts.Count} items", parts));
    }

    public void SelectVertex(Vertex? v, bool extend = false)
    {
        if (Selection is null || v is null) { if (!extend) ClearSelection(); return; }
        if (v.Sector is not null) ActiveSector = v.Sector;
        if (extend) Selection.Toggle(v); else Selection.SelectOnly(v);
        InvalidateVisual();
    }

    public void SelectThing(Thing? t, bool extend = false)
    {
        if (Selection is null || t is null) { if (!extend) ClearSelection(); return; }
        if (extend) Selection.Toggle(t); else Selection.SelectOnly(t);
        InvalidateVisual();
    }

    public void SelectLight(Light? l, bool extend = false)
    {
        if (Selection is null || l is null) { if (!extend) ClearSelection(); return; }
        if (extend) Selection.Toggle(l); else Selection.SelectOnly(l);
        InvalidateVisual();
    }

    public void SelectSurface(Surface? s, bool extend = false)
    {
        if (Selection is null || s is null) { if (!extend) ClearSelection(); return; }
        ActiveSector = s.Sector;
        if (extend) Selection.Toggle(s); else Selection.SelectOnly(s);
        InvalidateVisual();
    }

    private void ClearSelection()
    {
        Selection?.Clear();
        InvalidateVisual();
    }

    /// <summary>Called by the shell when the shared selection changed elsewhere.</summary>
    public void NotifySelectionChanged(Sector? activeSec)
    {
        if (activeSec is not null) ActiveSector = activeSec;
        InvalidateVisual();
    }
}
