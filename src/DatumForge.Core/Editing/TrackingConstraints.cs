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

    public static CadPoint ApplyPolar(CadPoint origin, CadPoint target, double incrementDegrees)
    {
        if (!double.IsFinite(incrementDegrees) || incrementDegrees <= 0 || incrementDegrees > 180) return target;
        var deltaX = target.X - origin.X;
        var deltaY = target.Y - origin.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (distance < 1e-9) return target;

        var increment = incrementDegrees * Math.PI / 180;
        var angle = Math.Atan2(deltaY, deltaX);
        var constrainedAngle = Math.Round(angle / increment, MidpointRounding.AwayFromZero) * increment;
        return new CadPoint(
            origin.X + distance * Math.Cos(constrainedAngle),
            origin.Y + distance * Math.Sin(constrainedAngle));
    }
}
