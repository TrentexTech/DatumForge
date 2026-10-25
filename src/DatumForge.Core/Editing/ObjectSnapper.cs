using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public enum ObjectSnapKind { Endpoint, Midpoint, Center, Intersection, Quadrant, Nearest }

public sealed record ObjectSnapResult(CadPoint Point, ObjectSnapKind Kind, Guid? EntityId, double Distance);

public static class ObjectSnapper
{
    public static ObjectSnapResult? FindNearest(
        DrawingDocument drawing,
        CadPoint pointer,
        double tolerance,
        ObjectSnapMode modes)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (modes == ObjectSnapMode.None) return null;

        var visibleLayerIds = drawing.Layers
            .Where(layer => layer.IsVisible)
            .Select(layer => layer.Id)
            .ToHashSet();
        var entities = drawing.Entities.Where(entity => visibleLayerIds.Contains(entity.LayerId)).ToArray();
        var candidates = entities.SelectMany(entity => EntityCandidates(entity, modes, pointer)).ToList();

        if (modes.HasFlag(ObjectSnapMode.Intersection))
        {
            var segments = entities.SelectMany(Segments).ToArray();
            for (var first = 0; first < segments.Length; first++)
                for (var second = first + 1; second < segments.Length; second++)
                    if (segments[first].EntityId != segments[second].EntityId &&
                        TryIntersect(segments[first], segments[second], out var intersection))
                        candidates.Add(new ObjectSnapResult(intersection, ObjectSnapKind.Intersection, null, 0));
        }

        return candidates
            .Select(candidate => candidate with { Distance = Distance(candidate.Point, pointer) })
            .Where(candidate => candidate.Distance <= tolerance)
            .OrderBy(candidate => candidate.Kind == ObjectSnapKind.Nearest)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Kind)
            .FirstOrDefault();
    }

    private static IEnumerable<ObjectSnapResult> EntityCandidates(
        DrawingEntity entity,
        ObjectSnapMode modes,
        CadPoint pointer)
    {
        if (modes.HasFlag(ObjectSnapMode.Endpoint))
        {
            foreach (var point in Endpoints(entity))
                yield return new ObjectSnapResult(point, ObjectSnapKind.Endpoint, entity.Id, 0);
        }

        if (modes.HasFlag(ObjectSnapMode.Midpoint))
        {
            if (TryGetArc(entity, out var arc))
                yield return new ObjectSnapResult(arc.PointAt(0.5), ObjectSnapKind.Midpoint, entity.Id, 0);
            else
                foreach (var segment in Segments(entity))
                    yield return new ObjectSnapResult(Midpoint(segment.Start, segment.End), ObjectSnapKind.Midpoint, entity.Id, 0);
        }

        if (modes.HasFlag(ObjectSnapMode.Center))
        {
            if (entity.Kind == EntityKind.Circle && entity.Points.Count > 0)
                yield return new ObjectSnapResult(entity.Points[0], ObjectSnapKind.Center, entity.Id, 0);
            else if (TryGetArc(entity, out var arc))
                yield return new ObjectSnapResult(arc.Center, ObjectSnapKind.Center, entity.Id, 0);
            else if (entity.Kind == EntityKind.Rectangle && RectangleGeometry.Center(entity.Points) is { } center)
                yield return new ObjectSnapResult(center, ObjectSnapKind.Center, entity.Id, 0);
        }

        if (modes.HasFlag(ObjectSnapMode.Quadrant) &&
            entity.Kind == EntityKind.Circle &&
            entity.Points.Count > 0)
        {
            var center = entity.Points[0];
            var radius = Math.Abs(entity.Radius);
            yield return new ObjectSnapResult(new CadPoint(center.X + radius, center.Y), ObjectSnapKind.Quadrant, entity.Id, 0);
            yield return new ObjectSnapResult(new CadPoint(center.X, center.Y + radius), ObjectSnapKind.Quadrant, entity.Id, 0);
            yield return new ObjectSnapResult(new CadPoint(center.X - radius, center.Y), ObjectSnapKind.Quadrant, entity.Id, 0);
            yield return new ObjectSnapResult(new CadPoint(center.X, center.Y - radius), ObjectSnapKind.Quadrant, entity.Id, 0);
        }
        else if (modes.HasFlag(ObjectSnapMode.Quadrant) && TryGetArc(entity, out var arc))
        {
            foreach (var quadrant in ArcGeometry.Quadrants(arc))
                yield return new ObjectSnapResult(quadrant, ObjectSnapKind.Quadrant, entity.Id, 0);
        }

        if (modes.HasFlag(ObjectSnapMode.Nearest) && NearestPoint(entity, pointer) is { } nearest)
            yield return new ObjectSnapResult(nearest, ObjectSnapKind.Nearest, entity.Id, 0);
    }

    private static CadPoint? NearestPoint(DrawingEntity entity, CadPoint pointer)
    {
        if (entity.Kind == EntityKind.Circle && entity.Points.Count > 0)
        {
            var center = entity.Points[0];
            var radius = Math.Abs(entity.Radius);
            var dx = pointer.X - center.X;
            var dy = pointer.Y - center.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            return distance < 1e-9
                ? new CadPoint(center.X + radius, center.Y)
                : new CadPoint(center.X + dx / distance * radius, center.Y + dy / distance * radius);
        }

        if (TryGetArc(entity, out var arc)) return ArcGeometry.NearestPoint(arc, pointer);

        var nearestSegmentPoint = Segments(entity)
            .Select(segment => ClosestPoint(segment.Start, segment.End, pointer))
            .OrderBy(point => Distance(point, pointer))
            .Select(point => (CadPoint?)point)
            .FirstOrDefault();
        if (nearestSegmentPoint is not null) return nearestSegmentPoint;
        return entity.Points.Count > 0 ? entity.Points[0] : null;
    }

    private static IEnumerable<CadPoint> Endpoints(DrawingEntity entity)
    {
        if (entity.Kind == EntityKind.Circle) yield break;
        if (entity.Kind == EntityKind.Arc && entity.Points.Count > 2)
        {
            yield return entity.Points[0];
            yield return entity.Points[2];
            yield break;
        }
        if (entity.Kind == EntityKind.Rectangle && entity.Points.Count > 1)
        {
            foreach (var corner in RectangleGeometry.Corners(entity.Points)) yield return corner;
            yield break;
        }
        foreach (var point in entity.Points) yield return point;
    }

    private static IEnumerable<Segment> Segments(DrawingEntity entity)
    {
        if (entity.Kind == EntityKind.Rectangle && entity.Points.Count > 1)
        {
            var corners = RectangleGeometry.Corners(entity.Points);
            for (var index = 0; index < corners.Length; index++)
                yield return new Segment(entity.Id, corners[index], corners[(index + 1) % corners.Length]);
            yield break;
        }

        if (entity.Kind is EntityKind.Circle or EntityKind.Arc or EntityKind.Text || entity.Points.Count < 2) yield break;
        for (var index = 1; index < entity.Points.Count; index++)
            yield return new Segment(entity.Id, entity.Points[index - 1], entity.Points[index]);
    }

    private static bool TryIntersect(Segment first, Segment second, out CadPoint intersection)
    {
        intersection = default;
        var firstDx = first.End.X - first.Start.X;
        var firstDy = first.End.Y - first.Start.Y;
        var secondDx = second.End.X - second.Start.X;
        var secondDy = second.End.Y - second.Start.Y;
        var denominator = firstDx * secondDy - firstDy * secondDx;
        if (Math.Abs(denominator) < 1e-9) return false;

        var deltaX = second.Start.X - first.Start.X;
        var deltaY = second.Start.Y - first.Start.Y;
        var firstPosition = (deltaX * secondDy - deltaY * secondDx) / denominator;
        var secondPosition = (deltaX * firstDy - deltaY * firstDx) / denominator;
        const double epsilon = 1e-9;
        if (firstPosition < -epsilon || firstPosition > 1 + epsilon ||
            secondPosition < -epsilon || secondPosition > 1 + epsilon) return false;

        intersection = new CadPoint(
            first.Start.X + firstPosition * firstDx,
            first.Start.Y + firstPosition * firstDy);
        return true;
    }

    private static CadPoint Midpoint(CadPoint first, CadPoint second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static bool TryGetArc(DrawingEntity entity, out CadArc arc)
    {
        arc = default;
        return entity.Kind == EntityKind.Arc &&
               entity.Points.Count > 2 &&
               ArcGeometry.TryCreate(entity.Points[0], entity.Points[1], entity.Points[2], out arc);
    }

    private static CadPoint ClosestPoint(CadPoint start, CadPoint end, CadPoint pointer)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-18) return start;
        var position = ((pointer.X - start.X) * dx + (pointer.Y - start.Y) * dy) / lengthSquared;
        position = Math.Clamp(position, 0, 1);
        return new CadPoint(start.X + position * dx, start.Y + position * dy);
    }

    private static double Distance(CadPoint first, CadPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private readonly record struct Segment(Guid EntityId, CadPoint Start, CadPoint End);
}
