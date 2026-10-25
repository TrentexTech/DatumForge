using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public readonly record struct CadSelectionBounds(double Left, double Bottom, double Right, double Top)
{
    public static CadSelectionBounds FromPoints(CadPoint first, CadPoint second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X),
        Math.Max(first.Y, second.Y));

    public bool Contains(CadPoint point, double tolerance = 1e-9) =>
        point.X >= Left - tolerance && point.X <= Right + tolerance &&
        point.Y >= Bottom - tolerance && point.Y <= Top + tolerance;

    public CadPoint[] Corners() =>
    [
        new(Left, Bottom),
        new(Right, Bottom),
        new(Right, Top),
        new(Left, Top)
    ];
}

public static class EntityRectangleSelector
{
    public static IReadOnlyList<Guid> Select(
        DrawingDocument drawing,
        CadPoint first,
        CadPoint second,
        bool crossing)
    {
        var bounds = CadSelectionBounds.FromPoints(first, second);
        var visibleLayerIds = drawing.Layers
            .Where(layer => layer.IsVisible)
            .Select(layer => layer.Id)
            .ToHashSet();

        return drawing.Entities
            .Where(entity => visibleLayerIds.Contains(entity.LayerId))
            .Where(entity => crossing ? Intersects(entity, bounds) : IsContained(entity, bounds))
            .Select(entity => entity.Id)
            .ToArray();
    }

    private static bool IsContained(DrawingEntity entity, CadSelectionBounds bounds)
    {
        if (entity.Points.Count == 0) return false;
        if (entity.Kind == EntityKind.Circle)
        {
            var center = entity.Points[0];
            var radius = Math.Abs(entity.Radius);
            return bounds.Contains(new CadPoint(center.X - radius, center.Y - radius)) &&
                   bounds.Contains(new CadPoint(center.X + radius, center.Y + radius));
        }

        if (TryGetArc(entity, out var arc))
            return ArcGeometry.BoundsPoints(arc).All(point => bounds.Contains(point));

        return GeometryPoints(entity).All(point => bounds.Contains(point));
    }

    private static bool Intersects(DrawingEntity entity, CadSelectionBounds bounds)
    {
        if (entity.Points.Count == 0) return false;
        if (entity.Kind == EntityKind.Circle) return CircleIntersects(bounds, entity.Points[0], Math.Abs(entity.Radius));
        if (TryGetArc(entity, out var arc)) return ArcIntersects(bounds, arc);

        var points = GeometryPoints(entity);
        if (points.Any(point => bounds.Contains(point))) return true;
        return Segments(entity, points).Any(segment => SegmentIntersects(bounds, segment.Start, segment.End));
    }

    private static CadPoint[] GeometryPoints(DrawingEntity entity) =>
        entity.Kind switch
        {
            EntityKind.Rectangle => RectangleGeometry.Corners(entity.Points),
            EntityKind.Text => TextGeometry.Corners(entity),
            _ => [.. entity.Points]
        };

    private static IEnumerable<Segment> Segments(DrawingEntity entity, IReadOnlyList<CadPoint> points)
    {
        for (var index = 1; index < points.Count; index++)
            yield return new Segment(points[index - 1], points[index]);
        if (entity.Kind is (EntityKind.Rectangle or EntityKind.Text) && points.Count > 2)
            yield return new Segment(points[^1], points[0]);
    }

    private static bool CircleIntersects(CadSelectionBounds bounds, CadPoint center, double radius)
    {
        var closestX = Math.Clamp(center.X, bounds.Left, bounds.Right);
        var closestY = Math.Clamp(center.Y, bounds.Bottom, bounds.Top);
        var minimumSquared = DistanceSquared(center, new CadPoint(closestX, closestY));
        var maximumSquared = bounds.Corners().Max(corner => DistanceSquared(center, corner));
        var radiusSquared = radius * radius;
        const double tolerance = 1e-9;
        return minimumSquared <= radiusSquared + tolerance && maximumSquared >= radiusSquared - tolerance;
    }

    private static bool ArcIntersects(CadSelectionBounds bounds, CadArc arc)
    {
        if (ArcGeometry.BoundsPoints(arc).Any(point => bounds.Contains(point))) return true;

        var corners = bounds.Corners();
        for (var index = 0; index < corners.Length; index++)
        {
            foreach (var point in CircleSegmentIntersections(
                         arc.Center,
                         arc.Radius,
                         corners[index],
                         corners[(index + 1) % corners.Length]))
            {
                var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X);
                if (arc.ContainsAngle(angle)) return true;
            }
        }

        return false;
    }

    private static IEnumerable<CadPoint> CircleSegmentIntersections(
        CadPoint center,
        double radius,
        CadPoint start,
        CadPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var fromCenterX = start.X - center.X;
        var fromCenterY = start.Y - center.Y;
        var a = dx * dx + dy * dy;
        if (a < 1e-18) yield break;

        var b = 2 * (fromCenterX * dx + fromCenterY * dy);
        var c = fromCenterX * fromCenterX + fromCenterY * fromCenterY - radius * radius;
        var discriminant = b * b - 4 * a * c;
        if (discriminant < -1e-9) yield break;

        var root = Math.Sqrt(Math.Max(0, discriminant));
        var firstPosition = (-b - root) / (2 * a);
        if (firstPosition is >= 0 and <= 1)
            yield return new CadPoint(start.X + firstPosition * dx, start.Y + firstPosition * dy);

        var secondPosition = (-b + root) / (2 * a);
        if (root > 1e-9 && secondPosition is >= 0 and <= 1)
            yield return new CadPoint(start.X + secondPosition * dx, start.Y + secondPosition * dy);
    }

    private static bool SegmentIntersects(CadSelectionBounds bounds, CadPoint start, CadPoint end)
    {
        if (bounds.Contains(start) || bounds.Contains(end)) return true;
        var corners = bounds.Corners();
        for (var index = 0; index < corners.Length; index++)
            if (SegmentsIntersect(start, end, corners[index], corners[(index + 1) % corners.Length])) return true;
        return false;
    }

    private static bool SegmentsIntersect(CadPoint firstStart, CadPoint firstEnd, CadPoint secondStart, CadPoint secondEnd)
    {
        var firstDx = firstEnd.X - firstStart.X;
        var firstDy = firstEnd.Y - firstStart.Y;
        var secondDx = secondEnd.X - secondStart.X;
        var secondDy = secondEnd.Y - secondStart.Y;
        var denominator = firstDx * secondDy - firstDy * secondDx;
        if (Math.Abs(denominator) < 1e-12)
        {
            var offsetX = secondStart.X - firstStart.X;
            var offsetY = secondStart.Y - firstStart.Y;
            if (Math.Abs(offsetX * firstDy - offsetY * firstDx) > 1e-9) return false;
            return Math.Max(Math.Min(firstStart.X, firstEnd.X), Math.Min(secondStart.X, secondEnd.X)) <=
                   Math.Min(Math.Max(firstStart.X, firstEnd.X), Math.Max(secondStart.X, secondEnd.X)) + 1e-9 &&
                   Math.Max(Math.Min(firstStart.Y, firstEnd.Y), Math.Min(secondStart.Y, secondEnd.Y)) <=
                   Math.Min(Math.Max(firstStart.Y, firstEnd.Y), Math.Max(secondStart.Y, secondEnd.Y)) + 1e-9;
        }

        var deltaX = secondStart.X - firstStart.X;
        var deltaY = secondStart.Y - firstStart.Y;
        var firstPosition = (deltaX * secondDy - deltaY * secondDx) / denominator;
        var secondPosition = (deltaX * firstDy - deltaY * firstDx) / denominator;
        const double tolerance = 1e-9;
        return firstPosition is >= -tolerance and <= 1 + tolerance &&
               secondPosition is >= -tolerance and <= 1 + tolerance;
    }

    private static bool TryGetArc(DrawingEntity entity, out CadArc arc)
    {
        arc = default;
        return entity.Kind == EntityKind.Arc && entity.Points.Count > 2 &&
               ArcGeometry.TryCreate(entity.Points[0], entity.Points[1], entity.Points[2], out arc);
    }

    private static double DistanceSquared(CadPoint first, CadPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private readonly record struct Segment(CadPoint Start, CadPoint End);
}
