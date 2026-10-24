using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.App.Controls;

public sealed class DrawingViewport : Control
{
    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;
    public event EventHandler<EntitySelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<EntityTransformRequestedEventArgs>? TransformRequested;

    public static readonly StyledProperty<DrawingDocument?> DrawingProperty =
        AvaloniaProperty.Register<DrawingViewport, DrawingDocument?>(nameof(Drawing));
    public static readonly StyledProperty<LayoutDefinition?> LayoutProperty =
        AvaloniaProperty.Register<DrawingViewport, LayoutDefinition?>(nameof(Layout));
    public static readonly StyledProperty<string> ActiveToolProperty =
        AvaloniaProperty.Register<DrawingViewport, string>(nameof(ActiveTool), "Select");
    public static readonly StyledProperty<int> RevisionTokenProperty =
        AvaloniaProperty.Register<DrawingViewport, int>(nameof(RevisionToken));

    private CadPoint? _anchor;
    private ObjectSnapResult? _snapResult;
    private readonly HashSet<Guid> _selectedEntityIds = [];

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
        if (Drawing is null || ActiveTool == "Select" || Layout?.SnapToObjects != true)
        {
            if (_snapResult is null) return;
            _snapResult = null;
            InvalidateVisual();
            return;
        }

        var pointer = ToWorld(e.GetPosition(this));
        _snapResult = ObjectSnapper.FindNearest(
            Drawing,
            pointer,
            12 / Math.Max(0.001, Layout.Zoom),
            Layout.ObjectSnaps);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _anchor = null;
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
            InvalidateVisual();
            return CoordinateSubmissionResult.Accepted($"First point set to {FormatPoint(point)}; enter the next point.");
        }

        var first = _anchor.Value;
        _anchor = null;
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
        if (Layout?.SnapToGrid != true || Layout.GridSpacing <= 0) return point;
        return new CadPoint(
            Math.Round(point.X / Layout.GridSpacing) * Layout.GridSpacing,
            Math.Round(point.Y / Layout.GridSpacing) * Layout.GridSpacing);
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
