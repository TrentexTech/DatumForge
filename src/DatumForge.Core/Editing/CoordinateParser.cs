using System.Globalization;
using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class CoordinateParser
{
    public static bool TryResolve(string? input, CadPoint? reference, out CadPoint point, out string error)
    {
        point = default;
        error = string.Empty;
        var text = input?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a coordinate such as 100,50, @25,-10, or @50<30.";
            return false;
        }

        if (text.StartsWith('@'))
        {
            if (reference is null)
            {
                error = "Relative coordinates require an existing reference point.";
                return false;
            }

            var relative = text[1..].Trim();
            var angleSeparator = relative.IndexOf('<');
            if (angleSeparator >= 0)
                return TryResolvePolar(relative, angleSeparator, reference.Value, out point, out error);

            if (!TryParsePair(relative, out var dx, out var dy))
            {
                error = "Relative coordinates use @dx,dy with dot decimals.";
                return false;
            }
            point = new CadPoint(reference.Value.X + dx, reference.Value.Y + dy);
            return true;
        }

        if (!TryParsePair(text, out var x, out var y))
        {
            error = "Absolute coordinates use x,y with dot decimals.";
            return false;
        }
        point = new CadPoint(x, y);
        return true;
    }

    private static bool TryResolvePolar(
        string text,
        int separator,
        CadPoint reference,
        out CadPoint point,
        out string error)
    {
        point = default;
        error = string.Empty;
        if (!TryParseNumber(text[..separator], out var distance) || distance < 0 ||
            !TryParseNumber(text[(separator + 1)..], out var angleDegrees))
        {
            error = "Polar coordinates use @distance<angle, for example @50<30.";
            return false;
        }

        var angleRadians = angleDegrees * Math.PI / 180;
        point = new CadPoint(
            reference.X + distance * Math.Cos(angleRadians),
            reference.Y + distance * Math.Sin(angleRadians));
        return true;
    }

    private static bool TryParsePair(string text, out double x, out double y)
    {
        x = y = 0;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TryParseNumber(parts[0], out x) && TryParseNumber(parts[1], out y);
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
