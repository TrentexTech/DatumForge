using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public enum EntityTransformKind { Move, Copy, Rotate, Scale }

public static class EntityGeometry
{
    public static CadPoint? SelectionCenter(DrawingDocument drawing, IEnumerable<Guid> entityIds)
    {
        var ids = entityIds.ToHashSet();
        var bounds = drawing.Entities
            .Where(entity => ids.Contains(entity.Id))
            .SelectMany(BoundsPoints)
            .ToArray();

        if (bounds.Length == 0) return null;
        return new CadPoint(
            (bounds.Min(point => point.X) + bounds.Max(point => point.X)) / 2,
            (bounds.Min(point => point.Y) + bounds.Max(point => point.Y)) / 2);
    }

    public static double AngleBetween(CadPoint center, CadPoint reference, CadPoint target)
    {
        if (Distance(center, reference) < 1e-9 || Distance(center, target) < 1e-9) return double.NaN;
        return Math.Atan2(target.Y - center.Y, target.X - center.X) -
            Math.Atan2(reference.Y - center.Y, reference.X - center.X);
    }

    public static double ScaleBetween(CadPoint center, CadPoint reference, CadPoint target)
    {
        var referenceDistance = Distance(center, reference);
        if (referenceDistance < 1e-9) return double.NaN;
        return Distance(center, target) / referenceDistance;
    }

    private static IEnumerable<CadPoint> BoundsPoints(DrawingEntity entity)
    {
        if (entity.Kind == EntityKind.Circle && entity.Points.Count > 0)
        {
            var center = entity.Points[0];
            yield return new CadPoint(center.X - entity.Radius, center.Y - entity.Radius);
            yield return new CadPoint(center.X + entity.Radius, center.Y + entity.Radius);
            yield break;
        }

        foreach (var point in entity.Points) yield return point;
    }

    private static double Distance(CadPoint first, CadPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
