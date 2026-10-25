using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class EntityHitTester
{
    public static DrawingEntity? HitTest(DrawingDocument drawing, CadPoint point, double tolerance)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));

        var visibleLayerIds = drawing.Layers
            .Where(layer => layer.IsVisible)
            .Select(layer => layer.Id)
            .ToHashSet();

        return drawing.Entities
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(entity => visibleLayerIds.Contains(entity.LayerId) && IsHit(entity, point, tolerance));
    }

    private static bool IsHit(DrawingEntity entity, CadPoint point, double tolerance)
    {
        if (entity.Points.Count == 0) return false;

        return entity.Kind switch
        {
            EntityKind.Circle => Math.Abs(Distance(entity.Points[0], point) - entity.Radius) <= tolerance,
            EntityKind.Arc when entity.Points.Count > 2 &&
                                ArcGeometry.TryCreate(entity.Points[0], entity.Points[1], entity.Points[2], out var arc) =>
                ArcGeometry.DistanceTo(arc, point) <= tolerance,
            EntityKind.Rectangle when entity.Points.Count > 1 => RectangleDistance(entity.Points, point) <= tolerance,
            EntityKind.Polyline => PolylineDistance(entity.Points, point) <= tolerance,
            EntityKind.Text => Distance(entity.Points[0], point) <= tolerance * 1.5,
            _ when entity.Points.Count > 1 => SegmentDistance(entity.Points[0], entity.Points[1], point) <= tolerance,
            _ => Distance(entity.Points[0], point) <= tolerance
        };
    }

    private static double PolylineDistance(IReadOnlyList<CadPoint> points, CadPoint point)
    {
        if (points.Count == 1) return Distance(points[0], point);
        var distance = double.MaxValue;
        for (var index = 1; index < points.Count; index++)
            distance = Math.Min(distance, SegmentDistance(points[index - 1], points[index], point));
        return distance;
    }

    private static double RectangleDistance(IReadOnlyList<CadPoint> points, CadPoint point)
    {
        var corners = RectangleGeometry.Corners(points);
        return Enumerable.Range(0, corners.Length)
            .Min(index => SegmentDistance(corners[index], corners[(index + 1) % corners.Length], point));
    }

    private static double SegmentDistance(CadPoint start, CadPoint end, CadPoint point)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0) return Distance(start, point);

        var projection = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        return Distance(new CadPoint(start.X + projection * dx, start.Y + projection * dy), point);
    }

    private static double Distance(CadPoint first, CadPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
