using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class RectangleGeometry
{
    public static CadPoint[] FromOppositeCorners(CadPoint first, CadPoint second) =>
    [
        new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new(Math.Max(first.X, second.X), Math.Min(first.Y, second.Y)),
        new(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)),
        new(Math.Min(first.X, second.X), Math.Max(first.Y, second.Y))
    ];

    public static CadPoint[] Corners(IReadOnlyList<CadPoint> points)
    {
        if (points.Count >= 4) return [points[0], points[1], points[2], points[3]];
        return points.Count >= 2 ? FromOppositeCorners(points[0], points[1]) : [];
    }

    public static CadPoint? Center(IReadOnlyList<CadPoint> points)
    {
        var corners = Corners(points);
        if (corners.Length == 0) return null;
        return new CadPoint(corners.Average(point => point.X), corners.Average(point => point.Y));
    }

    public static void Normalize(DrawingEntity entity)
    {
        if (entity.Kind == EntityKind.Rectangle && entity.Points.Count is >= 2 and < 4)
            entity.Points = [.. FromOppositeCorners(entity.Points[0], entity.Points[1])];
    }
}
