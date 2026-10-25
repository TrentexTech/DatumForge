using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class TextGeometry
{
    public const double DefaultHeight = 12;
    private const double CharacterWidthFactor = 0.6;

    public static double Height(DrawingEntity entity) =>
        double.IsFinite(entity.TextHeight) && entity.TextHeight > 0
            ? entity.TextHeight
            : DefaultHeight;

    public static void Normalize(DrawingEntity entity)
    {
        if (entity.Kind != EntityKind.Text) return;
        entity.TextHeight = Height(entity);
        entity.TextRotationDegrees = NormalizeDegrees(entity.TextRotationDegrees);
    }

    public static double Width(DrawingEntity entity)
    {
        var length = Math.Max(1, entity.Text?.Length ?? 0);
        return Height(entity) * CharacterWidthFactor * length;
    }

    public static CadPoint[] Corners(DrawingEntity entity)
    {
        if (entity.Points.Count == 0) return [];
        var insertion = entity.Points[0];
        var radians = DegreesToRadians(entity.TextRotationDegrees);
        var baseline = new CadPoint(Math.Cos(radians), Math.Sin(radians));
        var vertical = new CadPoint(-baseline.Y, baseline.X);
        var width = Width(entity);
        var height = Height(entity);
        var lowerRight = Offset(insertion, baseline, width);
        var upperLeft = Offset(insertion, vertical, height);
        return
        [
            insertion,
            lowerRight,
            Offset(lowerRight, vertical, height),
            upperLeft
        ];
    }

    public static bool Contains(DrawingEntity entity, CadPoint point, double tolerance)
    {
        var corners = Corners(entity);
        if (corners.Length == 0) return false;
        var insertion = corners[0];
        var radians = DegreesToRadians(entity.TextRotationDegrees);
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var dx = point.X - insertion.X;
        var dy = point.Y - insertion.Y;
        var localX = dx * cosine + dy * sine;
        var localY = -dx * sine + dy * cosine;
        return localX >= -tolerance && localX <= Width(entity) + tolerance &&
               localY >= -tolerance && localY <= Height(entity) + tolerance;
    }

    public static double NormalizeDegrees(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0;
        var normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public static double MirroredRotation(double degrees, CadPoint axisStart, CadPoint axisEnd)
    {
        var axisAngle = Math.Atan2(axisEnd.Y - axisStart.Y, axisEnd.X - axisStart.X);
        var reflected = 2 * axisAngle - DegreesToRadians(degrees);
        return NormalizeDegrees(reflected * 180 / Math.PI);
    }

    private static CadPoint Offset(CadPoint point, CadPoint direction, double distance) =>
        new(point.X + direction.X * distance, point.Y + direction.Y * distance);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
