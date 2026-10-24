using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.App.Controls;

public sealed class DrawingViewport : Control
{
    private static readonly Cursor PanningCursor = new(StandardCursorType.Hand);

    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;
    public event EventHandler<EntitySelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<EntityTransformRequestedEventArgs>? TransformRequested;
    public event EventHandler? ViewChanged;

    public static readonly StyledProperty<DrawingDocument?> DrawingProperty =
        AvaloniaProperty.Register<DrawingViewport, DrawingDocument?>(nameof(Drawing));
    public static readonly StyledProperty<LayoutDefinition?> LayoutProperty =
        AvaloniaProperty.Register<DrawingViewport, LayoutDefinition?>(nameof(Layout));
    public static readonly StyledProperty<string> ActiveToolProperty =
        AvaloniaProperty.Register<DrawingViewport, string>(nameof(ActiveTool), "Select");
    public static readonly StyledProperty<int> RevisionTokenProperty =
        AvaloniaProperty.Register<DrawingViewport, int>(nameof(RevisionToken));

    private CadPoint? _anchor;
    private CadPoint? _previewPoint;
    private ObjectSnapResult? _snapResult;
    private readonly HashSet<Guid> _selectedEntityIds = [];
    private Point? _lastPanPosition;
    private bool _panChangeReported;

    static DrawingViewport()
    {
        AffectsRender<DrawingViewport>(DrawingProperty, LayoutProperty, ActiveToolProperty, RevisionTokenProperty);
        DrawingProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.ClearSelection());
        RevisionTokenProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.ClearSelection());
        ActiveToolProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.ResetInteraction());
    }

    public DrawingDocument? Drawing { get => GetValue(DrawingProperty); set => SetValue(DrawingProperty, value); }
    public LayoutDefinition? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public string ActiveTool { get => GetValue(ActiveToolProperty); set => SetValue(ActiveToolProperty, value); }
    public int RevisionToken { get => GetValue(RevisionTokenProperty); set => SetValue(RevisionTokenProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = ParseBrush(Layout?.BackgroundColor, "#101419");
        context.FillRectangle(background, Bounds);
        DrawGrid(context);

        if (Drawing is null) return;
        foreach (var entity in Drawing.Entities)
        {
            var layer = Drawing.Layers.FirstOrDefault(item => item.Id == entity.LayerId);
            if (layer is null || !layer.IsVisible) continue;
            DrawEntity(context, entity, layer, _selectedEntityIds.Contains(entity.Id));
        }

        if (_anchor is { } previewAnchor && _previewPoint is { } previewPoint)
            DrawInteractionPreview(context, previewAnchor, previewPoint);
        else if (_previewPoint is { } firstPoint)
            DrawFirstPointPreview(context, firstPoint);

        if (_snapResult is not null) DrawObjectSnapMarker(context, _snapResult);

        if (_anchor is { } anchor)
        {
            var marker = ToScreen(anchor);
            var markerPen = new Pen(new SolidColorBrush(Color.Parse("#F4B95E")), 1.5);
            context.DrawEllipse(null, markerPen, new Rect(marker.X - 5, marker.Y - 5, 10, 10));
            context.DrawLine(markerPen, new Point(marker.X - 8, marker.Y), new Point(marker.X + 8, marker.Y));
            context.DrawLine(markerPen, new Point(marker.X, marker.Y - 8), new Point(marker.X, marker.Y + 8));
        }

        var origin = ToScreen(new CadPoint(0, 0));
        context.DrawLine(new Pen(Brushes.IndianRed, 1), origin, new Point(origin.X + 36, origin.Y));
        context.DrawLine(new Pen(Brushes.MediumSeaGreen, 1), origin, new Point(origin.X, origin.Y - 36));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Layout is not null && e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _previewPoint = null;
            _snapResult = null;
            _lastPanPosition = e.GetPosition(this);
            _panChangeReported = false;
            Cursor = PanningCursor;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (Drawing is null) return;
        var point = ToWorld(e.GetPosition(this));
        if (ActiveTool == "Select")
        {
            SelectAt(point, e.KeyModifiers);
            return;
        }

        point = ResolvePointerPoint(point);
        ProcessInputPoint(point);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPanPosition is { } previous && Layout is not null)
        {
            var current = e.GetPosition(this);
            var delta = current - previous;
            _lastPanPosition = current;
            if (delta.X != 0 || delta.Y != 0)
            {
                Layout.Pan = ViewportNavigation.PanByPixels(Layout.Pan, delta.X, delta.Y, Layout.Zoom);
                _snapResult = null;
                InvalidateVisual();
                _panChangeReported = true;
            }

            e.Handled = true;
            return;
        }

        if (Drawing is null || ActiveTool == "Select")
        {
            if (_snapResult is null && _previewPoint is null) return;
            _snapResult = null;
            _previewPoint = null;
            InvalidateVisual();
            return;
        }

        var pointer = ToWorld(e.GetPosition(this));
        _previewPoint = ResolvePointerPoint(pointer);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_previewPoint is null && _snapResult is null) return;
        _previewPoint = null;
        _snapResult = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_lastPanPosition is null || e.InitialPressMouseButton != MouseButton.Middle) return;
        var changed = EndPanning();
        e.Pointer.Capture(null);
        if (changed) ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_lastPanPosition is null) return;
        if (EndPanning()) ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool EndPanning()
    {
        var changed = _panChangeReported;
        _lastPanPosition = null;
        _panChangeReported = false;
        Cursor = null;
        return changed;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Layout is null || e.Delta.Y == 0) return;
        var pointer = e.GetPosition(this);
        var view = ViewportNavigation.ZoomAt(
            Layout.Pan,
            Layout.Zoom,
            e.Delta.Y,
            pointer.X,
            pointer.Y,
            Bounds.Width,
            Bounds.Height);
        if (view.Zoom == Layout.Zoom && view.Pan == Layout.Pan) return;
        Layout.Zoom = view.Zoom;
        Layout.Pan = view.Pan;
        _previewPoint = Drawing is not null && ActiveTool != "Select"
            ? ResolvePointerPoint(ToWorld(pointer))
            : null;
        if (_previewPoint is null) _snapResult = null;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _anchor = null;
            _previewPoint = null;
            _snapResult = null;
            ClearSelection();
            e.Handled = true;
        }
    }

    public void ClearSelection()
    {
        if (_selectedEntityIds.Count == 0) return;
        _selectedEntityIds.Clear();
        SelectionChanged?.Invoke(this, new EntitySelectionChangedEventArgs([]));
        InvalidateVisual();
    }

    public CoordinateSubmissionResult SubmitCoordinate(string? input)
    {
        if (Drawing is null) return CoordinateSubmissionResult.Failure("Open a drawing before entering coordinates.");
        if (ActiveTool == "Select") return CoordinateSubmissionResult.Failure("Choose a drawing or modify tool before entering coordinates.");
        if (!CoordinateParser.TryResolve(input, _anchor, out var point, out var error))
            return CoordinateSubmissionResult.Failure(error);

        var result = ProcessInputPoint(point);
        InvalidateVisual();
        return result;
    }

    private void ResetInteraction()
    {
        _anchor = null;
        _previewPoint = null;
        _snapResult = null;
        InvalidateVisual();
    }

    private CoordinateSubmissionResult ProcessInputPoint(CadPoint point)
    {
        if (Enum.TryParse<EntityTransformKind>(ActiveTool, out var transformKind))
        {
            if (_selectedEntityIds.Count == 0)
                return CoordinateSubmissionResult.Failure("Select entities before entering transform coordinates.");
            if (_anchor is null)
            {
                _anchor = point;
                _previewPoint = point;
                InvalidateVisual();
                return CoordinateSubmissionResult.Accepted($"Reference point set to {FormatPoint(point)}; enter the target point.");
            }

            RequestTransform(transformKind, point);
            return CoordinateSubmissionResult.CompletedInput("Transform applied.");
        }

        if (ActiveTool == "Text")
            return AddEntity(EntityKind.Text, [point], 0, "Note")
                ? CoordinateSubmissionResult.CompletedInput("Text created.")
                : CoordinateSubmissionResult.Failure("No visible, unlocked layer is available.");

        if (_anchor is null)
        {
            _anchor = point;
            _previewPoint = point;
            InvalidateVisual();
            return CoordinateSubmissionResult.Accepted($"First point set to {FormatPoint(point)}; enter the next point.");
        }

        var first = _anchor.Value;
        _anchor = null;
        _previewPoint = null;
        var kind = ActiveTool switch
        {
            "Circle" => EntityKind.Circle,
            "Rectangle" => EntityKind.Rectangle,
            "Polyline" => EntityKind.Polyline,
            "Dimension" => EntityKind.Dimension,
            _ => EntityKind.Line
        };
        var radius = Math.Sqrt(Math.Pow(point.X - first.X, 2) + Math.Pow(point.Y - first.Y, 2));
        return AddEntity(kind, [first, point], radius, null)
            ? CoordinateSubmissionResult.CompletedInput($"{kind} created.")
            : CoordinateSubmissionResult.Failure("No visible, unlocked layer is available.");
    }

    private CadPoint ResolvePointerPoint(CadPoint point)
    {
        _snapResult = Layout?.SnapToObjects == true && Drawing is not null
            ? ObjectSnapper.FindNearest(
                Drawing,
                point,
                12 / Math.Max(0.001, Layout.Zoom),
                Layout.ObjectSnaps)
            : null;
        if (_snapResult is not null) return _snapResult.Point;
        if (Layout?.SnapToGrid == true && Layout.GridSpacing > 0)
        {
            point = new CadPoint(
                Math.Round(point.X / Layout.GridSpacing) * Layout.GridSpacing,
                Math.Round(point.Y / Layout.GridSpacing) * Layout.GridSpacing);
        }

        return Layout?.OrthographicTracking == true && _anchor is { } anchor
            ? TrackingConstraints.ApplyOrthographic(anchor, point)
            : point;
    }

    private void RequestTransform(EntityTransformKind kind, CadPoint point)
    {
        if (_selectedEntityIds.Count == 0) return;
        if (_anchor is null)
        {
            _anchor = point;
            InvalidateVisual();
            return;
        }

        var reference = _anchor.Value;
        _anchor = null;
        _previewPoint = null;
        TransformRequested?.Invoke(this, new EntityTransformRequestedEventArgs(
            kind, [.. _selectedEntityIds], reference, point));
        InvalidateVisual();
    }

    private void SelectAt(CadPoint point, KeyModifiers modifiers)
    {
        var hit = EntityHitTester.HitTest(Drawing!, point, 8 / Math.Max(0.001, Layout?.Zoom ?? 1));
        var additive = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Shift);
        if (!additive) _selectedEntityIds.Clear();

        if (hit is not null)
        {
            if (additive && _selectedEntityIds.Contains(hit.Id)) _selectedEntityIds.Remove(hit.Id);
            else _selectedEntityIds.Add(hit.Id);
        }

        SelectionChanged?.Invoke(this, new EntitySelectionChangedEventArgs([.. _selectedEntityIds]));
        InvalidateVisual();
    }

    private bool AddEntity(EntityKind kind, List<CadPoint> points, double radius, string? text)
    {
        var layer = Drawing!.Layers.FirstOrDefault(item => item.IsVisible && !item.IsLocked);
        if (layer is null) return false;
        Drawing.Entities.Add(new DrawingEntity { Kind = kind, LayerId = layer.Id, Points = points, Radius = radius, Text = text });
        EntityCreated?.Invoke(this, new EntityCreatedEventArgs(Drawing.Entities[^1]));
        InvalidateVisual();
        return true;
    }

    private void DrawObjectSnapMarker(DrawingContext context, ObjectSnapResult snap)
    {
        var point = ToScreen(snap.Point);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#F4B95E")), 1.5);
        const double size = 6;
        switch (snap.Kind)
        {
            case ObjectSnapKind.Endpoint:
                context.DrawRectangle(null, pen, new Rect(point.X - size, point.Y - size, size * 2, size * 2));
                break;
            case ObjectSnapKind.Midpoint:
                context.DrawLine(pen, new Point(point.X, point.Y - size), new Point(point.X + size, point.Y));
                context.DrawLine(pen, new Point(point.X + size, point.Y), new Point(point.X, point.Y + size));
                context.DrawLine(pen, new Point(point.X, point.Y + size), new Point(point.X - size, point.Y));
                context.DrawLine(pen, new Point(point.X - size, point.Y), new Point(point.X, point.Y - size));
                break;
            case ObjectSnapKind.Center:
                context.DrawEllipse(null, pen, new Rect(point.X - size, point.Y - size, size * 2, size * 2));
                break;
            case ObjectSnapKind.Intersection:
                context.DrawLine(pen, new Point(point.X - size, point.Y - size), new Point(point.X + size, point.Y + size));
                context.DrawLine(pen, new Point(point.X - size, point.Y + size), new Point(point.X + size, point.Y - size));
                break;
        }
    }

    private void DrawInteractionPreview(DrawingContext context, CadPoint anchor, CadPoint target)
    {
        if (ActiveTool is "Select" or "Text") return;
        var isTransform = Enum.TryParse<EntityTransformKind>(ActiveTool, out _);
        var brush = new SolidColorBrush(Color.Parse(isTransform ? "#F4B95E" : "#67D6AD")) { Opacity = 0.8 };
        var pen = new Pen(brush, 1.5, dashStyle: new DashStyle([6, 4], 0));
        var first = ToScreen(anchor);
        var second = ToScreen(target);
        DrawAlignmentGuides(context, first, isTransform, 0.4);
        DrawAlignmentGuides(context, second, isTransform, 0.75);

        switch (ActiveTool)
        {
            case "Circle":
                var radius = Math.Sqrt(Math.Pow(target.X - anchor.X, 2) + Math.Pow(target.Y - anchor.Y, 2)) *
                             (Layout?.Zoom ?? 1);
                context.DrawEllipse(null, pen, new Rect(first.X - radius, first.Y - radius, radius * 2, radius * 2));
                break;
            case "Rectangle":
                context.DrawRectangle(null, pen, new Rect(first, second));
                break;
            case "Line":
            case "Polyline":
            case "Dimension":
            case "Move":
            case "Copy":
            case "Rotate":
            case "Scale":
                context.DrawLine(pen, first, second);
                break;
            default:
                return;
        }

        context.DrawEllipse(brush, null, new Rect(second.X - 3, second.Y - 3, 6, 6));
    }

    private void DrawFirstPointPreview(DrawingContext context, CadPoint point)
    {
        if (ActiveTool == "Select") return;
        var screen = ToScreen(point);
        var brush = new SolidColorBrush(Color.Parse("#67D6AD")) { Opacity = 0.85 };
        var pen = new Pen(brush, 1.25);
        const double radius = 5;
        const double arm = 9;
        DrawAlignmentGuides(context, screen, false, 0.65);
        context.DrawEllipse(null, pen, new Rect(screen.X - radius, screen.Y - radius, radius * 2, radius * 2));
        context.DrawLine(pen, new Point(screen.X - arm, screen.Y), new Point(screen.X + arm, screen.Y));
        context.DrawLine(pen, new Point(screen.X, screen.Y - arm), new Point(screen.X, screen.Y + arm));
    }

    private void DrawAlignmentGuides(DrawingContext context, Point point, bool isTransform, double opacity)
    {
        var brush = new SolidColorBrush(Color.Parse(isTransform ? "#9D783D" : "#347B66")) { Opacity = opacity };
        var pen = new Pen(brush, 1, dashStyle: new DashStyle([3, 5], 0));
        context.DrawLine(pen, new Point(0, point.Y), new Point(Bounds.Width, point.Y));
        context.DrawLine(pen, new Point(point.X, 0), new Point(point.X, Bounds.Height));
    }

    private void DrawGrid(DrawingContext context)
    {
        if (Layout?.ShowGrid != true || Layout.GridSpacing <= 0) return;
        var spacing = Layout.GridSpacing * Layout.Zoom;
        if (spacing < 4) return;
        var origin = ToScreen(new CadPoint(0, 0));
        var pen = new Pen(new SolidColorBrush(Color.Parse("#26303A")), 1);
        for (var x = origin.X % spacing; x < Bounds.Width; x += spacing) context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (var y = origin.Y % spacing; y < Bounds.Height; y += spacing) context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawEntity(DrawingContext context, DrawingEntity entity, DrawingLayer layer, bool isSelected)
    {
        var brush = ParseBrush(layer.Color, "#E8EDF2");
        brush.Opacity = Math.Clamp(layer.Opacity, 0, 1);
        var thickness = Layout?.ShowLineWeights == false ? 1 : Math.Max(1, layer.LineWeightMm * 3 * (Layout?.Zoom ?? 1));
        var dash = layer.LineType switch
        {
            CadLineType.Dashed => new DashStyle([8, 5], 0),
            CadLineType.Dotted => new DashStyle([1, 4], 0),
            CadLineType.DashDot => new DashStyle([8, 4, 1, 4], 0),
            CadLineType.Center => new DashStyle([12, 4, 2, 4], 0),
            _ => null
        };
        var pen = isSelected
            ? new Pen(new SolidColorBrush(Color.Parse("#65D6FF")), thickness + 2)
            : new Pen(brush, thickness, dashStyle: dash);
        if (entity.Points.Count == 0) return;
        var first = ToScreen(entity.Points[0]);
        var second = entity.Points.Count > 1 ? ToScreen(entity.Points[1]) : first;

        switch (entity.Kind)
        {
            case EntityKind.Circle:
                var r = entity.Radius * (Layout?.Zoom ?? 1);
                context.DrawEllipse(null, pen, new Rect(first.X - r, first.Y - r, r * 2, r * 2));
                break;
            case EntityKind.Rectangle:
                context.DrawRectangle(null, pen, new Rect(first, second));
                break;
            case EntityKind.Polyline:
                for (var index = 1; index < entity.Points.Count; index++)
                    context.DrawLine(pen, ToScreen(entity.Points[index - 1]), ToScreen(entity.Points[index]));
                break;
            default:
                context.DrawLine(pen, first, second);
                break;
        }

        if (isSelected)
        {
            foreach (var grip in entity.Points)
            {
                var screenPoint = ToScreen(grip);
                context.FillRectangle(Brushes.White, new Rect(screenPoint.X - 3, screenPoint.Y - 3, 6, 6));
                context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#167FA4")), 1), new Rect(screenPoint.X - 3, screenPoint.Y - 3, 6, 6));
            }
        }
    }

    private Point ToScreen(CadPoint point)
    {
        var zoom = Layout?.Zoom ?? 1;
        var pan = Layout?.Pan ?? default;
        return new Point(Bounds.Width / 2 + (point.X + pan.X) * zoom, Bounds.Height / 2 - (point.Y + pan.Y) * zoom);
    }

    private CadPoint ToWorld(Point point)
    {
        var zoom = Math.Max(0.001, Layout?.Zoom ?? 1);
        var pan = Layout?.Pan ?? default;
        return new CadPoint((point.X - Bounds.Width / 2) / zoom - pan.X, -(point.Y - Bounds.Height / 2) / zoom - pan.Y);
    }

    private static SolidColorBrush ParseBrush(string? value, string fallback)
    {
        try { return new SolidColorBrush(Color.Parse(value ?? fallback)); }
        catch (FormatException) { return new SolidColorBrush(Color.Parse(fallback)); }
    }

    private static string FormatPoint(CadPoint point) =>
        FormattableString.Invariant($"{point.X:0.###},{point.Y:0.###}");
}

public sealed record CoordinateSubmissionResult(bool Success, bool Completed, string Message)
{
    public static CoordinateSubmissionResult Failure(string message) => new(false, false, message);
    public static CoordinateSubmissionResult Accepted(string message) => new(true, false, message);
    public static CoordinateSubmissionResult CompletedInput(string message) => new(true, true, message);
}

public sealed class EntityCreatedEventArgs(DrawingEntity entity) : EventArgs
{
    public DrawingEntity Entity { get; } = entity;
}

public sealed class EntitySelectionChangedEventArgs(IReadOnlyCollection<Guid> entityIds) : EventArgs
{
    public IReadOnlyCollection<Guid> EntityIds { get; } = entityIds;
}

public sealed class EntityTransformRequestedEventArgs(
    EntityTransformKind kind,
    IReadOnlyCollection<Guid> entityIds,
    CadPoint reference,
    CadPoint target) : EventArgs
{
    public EntityTransformKind Kind { get; } = kind;
    public IReadOnlyCollection<Guid> EntityIds { get; } = entityIds;
    public CadPoint Reference { get; } = reference;
    public CadPoint Target { get; } = target;
}
