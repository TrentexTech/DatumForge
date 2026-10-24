using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.App.Controls;

public sealed class DrawingViewport : Control
{
    private static Cursor? _panningCursor;

    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;
    public event EventHandler<EntitySelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<EntityTransformRequestedEventArgs>? TransformRequested;
    public event EventHandler? ViewChanged;
    public event EventHandler? InteractionCancelled;
    public event EventHandler? SelectToolRequested;

    public static readonly StyledProperty<DrawingDocument?> DrawingProperty =
        AvaloniaProperty.Register<DrawingViewport, DrawingDocument?>(nameof(Drawing));
    public static readonly StyledProperty<LayoutDefinition?> LayoutProperty =
        AvaloniaProperty.Register<DrawingViewport, LayoutDefinition?>(nameof(Layout));
    public static readonly StyledProperty<string> ActiveToolProperty =
        AvaloniaProperty.Register<DrawingViewport, string>(nameof(ActiveTool), "Select");
    public static readonly StyledProperty<int> RevisionTokenProperty =
        AvaloniaProperty.Register<DrawingViewport, int>(nameof(RevisionToken));

    private CadPoint? _anchor;
    private CadPoint? _arcStart;
    private CadPoint? _arcThrough;
    private readonly PolylineConstruction _polyline = new();
    private CadPoint? _previewPoint;
    private ObjectSnapResult? _snapResult;
    private Guid? _hoveredEntityId;
    private readonly HashSet<Guid> _selectedEntityIds = [];
    private Point? _lastPointerPosition;
    private Point? _lastPanPosition;
    private bool _panChangeReported;

    static DrawingViewport()
    {
        AffectsRender<DrawingViewport>(DrawingProperty, LayoutProperty, ActiveToolProperty, RevisionTokenProperty);
        DrawingProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.RefreshDrawing());
        LayoutProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.RefreshPointerFeedback());
        RevisionTokenProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.RefreshFromModel());
        ActiveToolProperty.Changed.AddClassHandler<DrawingViewport>((control, _) => control.ChangeActiveTool());
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
            DrawEntity(
                context,
                entity,
                layer,
                _selectedEntityIds.Contains(entity.Id),
                _hoveredEntityId == entity.Id);
        }

        if (ActiveTool == "Polyline" && _polyline.IsActive)
            DrawPolylinePreview(context);
        else if (_anchor is { } previewAnchor && _previewPoint is { } previewPoint)
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
        _lastPointerPosition = e.GetPosition(this);
        if (Layout is not null && e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _previewPoint = null;
            _snapResult = null;
            _hoveredEntityId = null;
            _lastPanPosition = e.GetPosition(this);
            _panChangeReported = false;
            Cursor = _panningCursor ??= new Cursor(StandardCursorType.Hand);
            InvalidateVisual();
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (Drawing is null) return;
        var properties = e.GetCurrentPoint(this).Properties;
        var point = ToWorld(e.GetPosition(this));
        if (ActiveTool == "Select")
        {
            if (!properties.IsLeftButtonPressed) return;
            SelectAt(point, e.KeyModifiers);
            return;
        }

        if (ActiveTool == "Polyline" && properties.IsRightButtonPressed)
        {
            FinishPolyline();
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed) return;
        if (ActiveTool == "Polyline" && e.ClickCount > 1)
        {
            FinishPolyline();
            e.Handled = true;
            return;
        }

        point = ResolvePointerPoint(point);
        ProcessInputPoint(point);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _lastPointerPosition = e.GetPosition(this);
        if (_lastPanPosition is { } previous && Layout is not null)
        {
            var current = _lastPointerPosition.Value;
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

        if (Drawing is null)
        {
            ClearPointerFeedback();
            return;
        }

        if (ActiveTool == "Select")
        {
            UpdateSelectionPreview(ToWorld(_lastPointerPosition.Value));
            return;
        }

        _hoveredEntityId = null;
        var pointer = ToWorld(_lastPointerPosition.Value);
        _previewPoint = ResolvePointerPoint(pointer);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _lastPointerPosition = null;
        ClearPointerFeedback();
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
        RefreshPointerFeedback();
        return changed;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Layout is null || e.Delta.Y == 0) return;
        var pointer = e.GetPosition(this);
        _lastPointerPosition = pointer;
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
        if (ActiveTool == "Select" && Drawing is not null) UpdateSelectionPreview(ToWorld(pointer));
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && ActiveTool == "Polyline" && _polyline.IsActive)
        {
            FinishPolyline();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && HandleEscape()) e.Handled = true;
    }

    public bool HandleEscape()
    {
        switch (ViewportEscapeBehavior.Decide(Drawing is not null, _anchor is not null, ActiveTool))
        {
            case ViewportEscapeAction.CancelInteraction:
                ResetInteraction();
                RefreshPointerFeedback();
                InteractionCancelled?.Invoke(this, EventArgs.Empty);
                return true;

            case ViewportEscapeAction.SelectTool:
                SelectToolRequested?.Invoke(this, EventArgs.Empty);
                return true;

            case ViewportEscapeAction.ClearSelection:
                ClearSelection();
                RefreshPointerFeedback();
                return true;

            default:
                return false;
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
        if (ActiveTool == "Polyline" && string.IsNullOrWhiteSpace(input)) return FinishPolyline();
        if (!CoordinateParser.TryResolve(input, _anchor, out var point, out var error))
            return CoordinateSubmissionResult.Failure(error);

        var result = ProcessInputPoint(point);
        InvalidateVisual();
        return result;
    }

    private void ResetInteraction()
    {
        _anchor = null;
        _arcStart = null;
        _arcThrough = null;
        _polyline.Reset();
        _previewPoint = null;
        _snapResult = null;
        _hoveredEntityId = null;
        InvalidateVisual();
    }

    private void ChangeActiveTool()
    {
        ResetInteraction();
        RefreshPointerFeedback();
    }

    private void RefreshDrawing()
    {
        ClearSelection();
        RefreshPointerFeedback();
    }

    private void RefreshFromModel()
    {
        ClearSelection();
        RefreshPointerFeedback();
    }

    private void RefreshPointerFeedback()
    {
        if (_lastPointerPosition is { } pointer && Drawing is not null)
        {
            if (ActiveTool == "Select")
            {
                _previewPoint = null;
                _snapResult = null;
                UpdateSelectionPreview(ToWorld(pointer));
                return;
            }

            _hoveredEntityId = null;
            _previewPoint = ResolvePointerPoint(ToWorld(pointer));
            InvalidateVisual();
            return;
        }

        ClearPointerFeedback();
    }

    private void UpdateSelectionPreview(CadPoint point)
    {
        var hoveredEntityId = Drawing is null
            ? null
            : EntityHitTester.HitTest(Drawing, point, 8 / Math.Max(0.001, Layout?.Zoom ?? 1))?.Id;
        if (_hoveredEntityId == hoveredEntityId) return;
        _hoveredEntityId = hoveredEntityId;
        InvalidateVisual();
    }

    private void ClearPointerFeedback()
    {
        if (_previewPoint is null && _snapResult is null && _hoveredEntityId is null) return;
        _previewPoint = null;
        _snapResult = null;
        _hoveredEntityId = null;
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

        if (ActiveTool == "Arc") return ProcessArcPoint(point);
        if (ActiveTool == "Polyline") return ProcessPolylinePoint(point);

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
            "Dimension" => EntityKind.Dimension,
            _ => EntityKind.Line
        };
        var radius = Math.Sqrt(Math.Pow(point.X - first.X, 2) + Math.Pow(point.Y - first.Y, 2));
        return AddEntity(kind, [first, point], radius, null)
            ? CoordinateSubmissionResult.CompletedInput($"{kind} created.")
            : CoordinateSubmissionResult.Failure("No visible, unlocked layer is available.");
    }

    private CoordinateSubmissionResult ProcessPolylinePoint(CadPoint point)
    {
        if (!_polyline.TryAddVertex(point))
            return CoordinateSubmissionResult.Failure("Choose a point different from the previous polyline vertex.");

        _anchor = point;
        _previewPoint = point;
        InvalidateVisual();
        return CoordinateSubmissionResult.Accepted(
            $"Polyline vertex {_polyline.Vertices.Count} set to {FormatPoint(point)}; add another point or press Enter to finish.");
    }

    private CoordinateSubmissionResult FinishPolyline()
    {
        if (!_polyline.TryComplete(out var points))
            return CoordinateSubmissionResult.Failure("A polyline needs at least two distinct vertices.");

        _anchor = null;
        _previewPoint = null;
        _snapResult = null;
        return AddEntity(EntityKind.Polyline, points, 0, null)
            ? CoordinateSubmissionResult.CompletedInput($"Polyline created with {points.Count} vertices.")
            : CoordinateSubmissionResult.Failure("No visible, unlocked layer is available.");
    }

    private CoordinateSubmissionResult ProcessArcPoint(CadPoint point)
    {
        if (_arcStart is null)
        {
            _arcStart = point;
            _anchor = point;
            _previewPoint = point;
            InvalidateVisual();
            return CoordinateSubmissionResult.Accepted($"Arc start set to {FormatPoint(point)}; enter a point on the arc.");
        }

        if (_arcThrough is null)
        {
            _arcThrough = point;
            _anchor = point;
            _previewPoint = point;
            InvalidateVisual();
            return CoordinateSubmissionResult.Accepted($"Arc point set to {FormatPoint(point)}; enter the end point.");
        }

        var start = _arcStart.Value;
        var through = _arcThrough.Value;
        if (!ArcGeometry.TryCreate(start, through, point, out var arc))
            return CoordinateSubmissionResult.Failure("An arc needs three distinct, non-collinear points.");

        _anchor = null;
        _arcStart = null;
        _arcThrough = null;
        _previewPoint = null;
        return AddEntity(EntityKind.Arc, [start, through, point], arc.Radius, null)
            ? CoordinateSubmissionResult.CompletedInput("Arc created.")
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

        if (_anchor is not { } anchor) return point;
        if (Layout?.OrthographicTracking == true) return TrackingConstraints.ApplyOrthographic(anchor, point);
        return Layout?.PolarTracking == true
            ? TrackingConstraints.ApplyPolar(anchor, point, Layout.PolarIncrementDegrees)
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
            case ObjectSnapKind.Quadrant:
                context.DrawLine(pen, new Point(point.X - size, point.Y), new Point(point.X + size, point.Y));
                context.DrawLine(pen, new Point(point.X, point.Y - size), new Point(point.X, point.Y + size));
                break;
            case ObjectSnapKind.Nearest:
                context.DrawLine(pen, new Point(point.X - size, point.Y - 2), new Point(point.X + size, point.Y - 2));
                context.DrawLine(pen, new Point(point.X - size, point.Y + 2), new Point(point.X + size, point.Y + 2));
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
            case "Arc":
                if (_arcStart is { } arcStart && _arcThrough is { } arcThrough &&
                    ArcGeometry.TryCreate(arcStart, arcThrough, target, out var arc))
                    DrawArc(context, pen, arc);
                else if (_arcStart is { } start)
                {
                    context.DrawLine(pen, ToScreen(start), first);
                    context.DrawLine(pen, first, second);
                }
                break;
            case "Circle":
                var radius = Math.Sqrt(Math.Pow(target.X - anchor.X, 2) + Math.Pow(target.Y - anchor.Y, 2)) *
                             (Layout?.Zoom ?? 1);
                context.DrawEllipse(null, pen, new Rect(first.X - radius, first.Y - radius, radius * 2, radius * 2));
                break;
            case "Rectangle":
                context.DrawRectangle(null, pen, new Rect(first, second));
                break;
            case "Line":
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

    private void DrawPolylinePreview(DrawingContext context)
    {
        var brush = new SolidColorBrush(Color.Parse("#67D6AD")) { Opacity = 0.8 };
        var pen = new Pen(brush, 1.5, dashStyle: new DashStyle([6, 4], 0));
        var vertices = _polyline.Vertices;
        for (var index = 1; index < vertices.Count; index++)
            context.DrawLine(pen, ToScreen(vertices[index - 1]), ToScreen(vertices[index]));

        var anchor = ToScreen(vertices[^1]);
        DrawAlignmentGuides(context, anchor, false, 0.4);
        if (_previewPoint is not { } preview || preview == vertices[^1]) return;

        var target = ToScreen(preview);
        DrawAlignmentGuides(context, target, false, 0.75);
        context.DrawLine(pen, anchor, target);
        context.DrawEllipse(brush, null, new Rect(target.X - 3, target.Y - 3, 6, 6));
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

    private void DrawEntity(
        DrawingContext context,
        DrawingEntity entity,
        DrawingLayer layer,
        bool isSelected,
        bool isHovered)
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
            : isHovered
                ? new Pen(new SolidColorBrush(Color.Parse("#67D6AD")), thickness + 2)
                : new Pen(brush, thickness, dashStyle: dash);
        if (entity.Points.Count == 0) return;
        var first = ToScreen(entity.Points[0]);
        var second = entity.Points.Count > 1 ? ToScreen(entity.Points[1]) : first;

        switch (entity.Kind)
        {
            case EntityKind.Arc when entity.Points.Count > 2 &&
                                     ArcGeometry.TryCreate(entity.Points[0], entity.Points[1], entity.Points[2], out var arc):
                DrawArc(context, pen, arc);
                break;
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

    private void DrawArc(DrawingContext context, Pen pen, CadArc arc)
    {
        var zoom = Math.Max(0.001, Layout?.Zoom ?? 1);
        var screenLength = Math.Abs(arc.SweepAngle) * arc.Radius * zoom;
        var segmentCount = Math.Clamp((int)Math.Ceiling(screenLength / 8), 8, 256);
        Point? previous = null;
        foreach (var point in ArcGeometry.Sample(arc, segmentCount))
        {
            var current = ToScreen(point);
            if (previous is { } start) context.DrawLine(pen, start, current);
            previous = current;
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
