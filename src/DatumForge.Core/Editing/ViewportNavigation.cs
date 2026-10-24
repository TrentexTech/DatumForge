using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public static class ViewportNavigation
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 100;
    private const double ZoomStep = 1.15;

    public static CadPoint PanByPixels(CadPoint pan, double deltaX, double deltaY, double zoom)
    {
        var normalizedZoom = Math.Max(MinimumZoom, zoom);
        return new CadPoint(
            pan.X + deltaX / normalizedZoom,
            pan.Y - deltaY / normalizedZoom);
    }

    public static (double Zoom, CadPoint Pan) ZoomAt(
        CadPoint pan,
        double zoom,
        double wheelDelta,
        double pointerX,
        double pointerY,
        double viewportWidth,
        double viewportHeight)
    {
        var currentZoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        var nextZoom = Math.Clamp(currentZoom * Math.Pow(ZoomStep, wheelDelta), MinimumZoom, MaximumZoom);
        var worldX = (pointerX - viewportWidth / 2) / currentZoom - pan.X;
        var worldY = -(pointerY - viewportHeight / 2) / currentZoom - pan.Y;
        var nextPan = new CadPoint(
            (pointerX - viewportWidth / 2) / nextZoom - worldX,
            -(pointerY - viewportHeight / 2) / nextZoom - worldY);
        return (nextZoom, nextPan);
    }
}
