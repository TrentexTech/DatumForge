using DatumForge.Core.Models;

namespace DatumForge.App.Controls;

public static class ViewportCoordinateDisplay
{
    public static string Format(CadPoint? point) => point is { } value
        ? FormattableString.Invariant($"X: {value.X:0.###}   Y: {value.Y:0.###}")
        : "X: —   Y: —";
}
