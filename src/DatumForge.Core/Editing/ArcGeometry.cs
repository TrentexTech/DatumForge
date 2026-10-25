using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public readonly record struct CadArc(CadPoint Center, double Radius, double StartAngle, double SweepAngle)
{
    public CadPoint Start => PointAt(0);
    public CadPoint End => PointAt(1);

    public CadPoint PointAt(double fraction)
    {
        var angle = StartAngle + SweepAngle * fraction;
        return new CadPoint(
            Center.X + Radius * Math.Cos(angle),
            Center.Y + Radius * Math.Sin(angle));
    }

    public bool ContainsAngle(double angle, double tolerance = 1e-9)
    {
        var distance = SweepAngle >= 0
            ? ArcGeometry.NormalizePositive(angle - StartAngle)
            : ArcGeometry.NormalizePositive(StartAngle - angle);
        return distance <= Math.Abs(SweepAngle) + tolerance;
    }
}

public static class ArcGeometry
{
    private const double FullTurn = Math.PI * 2;

    public static bool TryCreate(CadPoint start, CadPoint through, CadPoint end, out CadArc arc)
    {
        arc = default;
        var throughX = through.X - start.X;
        var throughY = through.Y - start.Y;
        var endX = end.X - start.X;
        var endY = end.Y - start.Y;
        var throughSquared = throughX * throughX + throughY * throughY;
        var endSquared = endX * endX + endY * endY;
        var determinant = 2 * (throughX * endY - throughY * endX);
        var determinantTolerance = Math.Max(1, Math.Max(throughSquared, endSquared)) * 1e-12;
        if (Math.Abs(determinant) <= determinantTolerance) return false;

        var center = new CadPoint(
            start.X + (throughSquared * endY - endSquared * throughY) / determinant,
            start.Y + (endSquared * throughX - throughSquared * endX) / determinant);
        var radius = Distance(center, start);
        if (!double.IsFinite(radius) || radius < 1e-9) return false;

        var startAngle = Angle(center, start);
        var throughAngle = Angle(center, through);
        var endAngle = Angle(center, end);
        var counterClockwiseSweep = NormalizePositive(endAngle - startAngle);
        var counterClockwiseThrough = NormalizePositive(throughAngle - startAngle);
        var sweep = counterClockwiseThrough <= counterClockwiseSweep + 1e-9
            ? counterClockwiseSweep
            : counterClockwiseSweep - FullTurn;
        if (Math.Abs(sweep) < 1e-9) return false;

        arc = new CadArc(center, radius, startAngle, sweep);
        return true;
    }

    public static CadPoint NearestPoint(CadArc arc, CadPoint point)
    {
        if (DistanceSquared(arc.Center, point) <= Math.Max(1, arc.Radius * arc.Radius) * 1e-24)
            return arc.Start;

        var angle = Angle(arc.Center, point);
        if (arc.ContainsAngle(angle))
            return new CadPoint(
                arc.Center.X + arc.Radius * Math.Cos(angle),
                arc.Center.Y + arc.Radius * Math.Sin(angle));

        var startDistanceSquared = DistanceSquared(point, arc.Start);
        var endDistanceSquared = DistanceSquared(point, arc.End);
        var tieTolerance = Math.Max(1, Math.Max(startDistanceSquared, endDistanceSquared)) * 1e-12;
        return startDistanceSquared <= endDistanceSquared + tieTolerance ? arc.Start : arc.End;
    }

    public static double DistanceTo(CadArc arc, CadPoint point) => Distance(point, NearestPoint(arc, point));

    public static IEnumerable<CadPoint> Quadrants(CadArc arc)
    {
        foreach (var angle in new[] { 0d, Math.PI / 2, Math.PI, Math.PI * 1.5 })
            if (arc.ContainsAngle(angle))
                yield return new CadPoint(
                    arc.Center.X + arc.Radius * Math.Cos(angle),
                    arc.Center.Y + arc.Radius * Math.Sin(angle));
    }

    public static IEnumerable<CadPoint> BoundsPoints(CadArc arc)
    {
        yield return arc.Start;
        yield return arc.End;
        foreach (var quadrant in Quadrants(arc)) yield return quadrant;
    }

    public static IEnumerable<CadPoint> Sample(CadArc arc, int segmentCount)
    {
        segmentCount = Math.Max(1, segmentCount);
        for (var index = 0; index <= segmentCount; index++)
            yield return arc.PointAt((double)index / segmentCount);
    }

    internal static double NormalizePositive(double angle)
    {
        angle %= FullTurn;
        return angle < 0 ? angle + FullTurn : angle;
    }

    private static double Angle(CadPoint center, CadPoint point) =>
        Math.Atan2(point.Y - center.Y, point.X - center.X);

    private static double Distance(CadPoint first, CadPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private static double DistanceSquared(CadPoint first, CadPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
