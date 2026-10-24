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
        var determinant = 2 * (start.X * (through.Y - end.Y) +
                               through.X * (end.Y - start.Y) +
                               end.X * (start.Y - through.Y));
        if (Math.Abs(determinant) < 1e-9) return false;

        var startSquared = start.X * start.X + start.Y * start.Y;
        var throughSquared = through.X * through.X + through.Y * through.Y;
        var endSquared = end.X * end.X + end.Y * end.Y;
        var center = new CadPoint(
            (startSquared * (through.Y - end.Y) +
             throughSquared * (end.Y - start.Y) +
             endSquared * (start.Y - through.Y)) / determinant,
            (startSquared * (end.X - through.X) +
             throughSquared * (start.X - end.X) +
             endSquared * (through.X - start.X)) / determinant);
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
        var angle = Angle(arc.Center, point);
        if (arc.ContainsAngle(angle))
            return new CadPoint(
                arc.Center.X + arc.Radius * Math.Cos(angle),
                arc.Center.Y + arc.Radius * Math.Sin(angle));

        return Distance(point, arc.Start) <= Distance(point, arc.End) ? arc.Start : arc.End;
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
}
