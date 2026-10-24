using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DatumForge.Core.Models;

namespace DatumForge.App.Controls;

public sealed class DrawingViewport : Control
{
    public event EventHandler? EntityCreated;

    public static readonly StyledProperty<DrawingDocument?> DrawingProperty =
        AvaloniaProperty.Register<DrawingViewport, DrawingDocument?>(nameof(Drawing));
    public static readonly StyledProperty<LayoutDefinition?> LayoutProperty =
        AvaloniaProperty.Register<DrawingViewport, LayoutDefinition?>(nameof(Layout));
    public static readonly StyledProperty<string> ActiveToolProperty =
        AvaloniaProperty.Register<DrawingViewport, string>(nameof(ActiveTool), "Select");

    private CadPoint? _anchor;

    static DrawingViewport()
    {
        AffectsRender<DrawingViewport>(DrawingProperty, LayoutProperty, ActiveToolProperty);
    }

    public DrawingDocument? Drawing { get => GetValue(DrawingProperty); set => SetValue(DrawingProperty, value); }
    public LayoutDefinition? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    public string ActiveTool { get => GetValue(ActiveToolProperty); set => SetValue(ActiveToolProperty, value); }

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
            DrawEntity(context, entity, layer);
        }

        var origin = ToScreen(new CadPoint(0, 0));
        context.DrawLine(new Pen(Brushes.IndianRed, 1), origin, new Point(origin.X + 36, origin.Y));
        context.DrawLine(new Pen(Brushes.MediumSeaGreen, 1), origin, new Point(origin.X, origin.Y - 36));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Drawing is null || ActiveTool == "Select") return;
        var point = ToWorld(e.GetPosition(this));
        if (Layout?.SnapToGrid == true && Layout.GridSpacing > 0)
        {
            point = new CadPoint(
                Math.Round(point.X / Layout.GridSpacing) * Layout.GridSpacing,
                Math.Round(point.Y / Layout.GridSpacing) * Layout.GridSpacing);
        }

        if (ActiveTool == "Text")
        {
            AddEntity(EntityKind.Text, [point], 0, "Note");
            return;
        }

        if (_anchor is null)
        {
            _anchor = point;
            InvalidateVisual();
            return;
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
        AddEntity(kind, [first, point], radius, null);
    }

    private void AddEntity(EntityKind kind, List<CadPoint> points, double radius, string? text)
    {
        var layer = Drawing!.Layers.FirstOrDefault(item => item.IsVisible && !item.IsLocked) ?? Drawing.Layers.FirstOrDefault();
        if (layer is null) return;
        Drawing.Entities.Add(new DrawingEntity { Kind = kind, LayerId = layer.Id, Points = points, Radius = radius, Text = text });
        EntityCreated?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
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

    private void DrawEntity(DrawingContext context, DrawingEntity entity, DrawingLayer layer)
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
        var pen = new Pen(brush, thickness, dashStyle: dash);
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
}
