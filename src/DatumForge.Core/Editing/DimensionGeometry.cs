using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public readonly record struct CadDimension(
    CadPoint Start,
    CadPoint End,
    CadPoint Midpoint,
    double Length);

public static class DimensionGeometry
{
    public static bool TryCreate(CadPoint start, CadPoint end, out CadDimension dimension)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9)
        {
            dimension = default;
            return false;
        }

        dimension = new CadDimension(
            start,
            end,
            new CadPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2),
            length);
        return true;
    }

    public static bool TryCreate(IReadOnlyList<CadPoint> points, out CadDimension dimension)
    {
        if (points.Count > 1) return TryCreate(points[0], points[1], out dimension);
        dimension = default;
        return false;
    }

    public static string Label(CadDimension dimension, DrawingUnits units) =>
        $"{FormatLength(dimension.Length)} {UnitSymbol(units)}";

    private static string FormatLength(double length) =>
        FormattableString.Invariant($"{length:0.###}");

    private static string UnitSymbol(DrawingUnits units) => units switch
    {
        DrawingUnits.Millimeters => "mm",
        DrawingUnits.Centimeters => "cm",
        DrawingUnits.Meters => "m",
        DrawingUnits.Inches => "in",
        DrawingUnits.Feet => "ft",
        _ => string.Empty
    };
}
