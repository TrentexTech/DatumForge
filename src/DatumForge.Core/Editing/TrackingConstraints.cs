using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class TrackingConstraints
{
    public static CadPoint ApplyOrthographic(CadPoint origin, CadPoint target)
    {
        var deltaX = target.X - origin.X;
        var deltaY = target.Y - origin.Y;
        return Math.Abs(deltaX) >= Math.Abs(deltaY)
            ? new CadPoint(target.X, origin.Y)
            : new CadPoint(origin.X, target.Y);
    }
}
