using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class ViewportNavigationTests
{
    [Fact]
    public void PanConvertsScreenMovementIntoWorldOffset()
    {
        var pan = ViewportNavigation.PanByPixels(new CadPoint(10, 20), 40, -20, 2);

        Assert.Equal(new CadPoint(30, 30), pan);
    }

    [Fact]
    public void ZoomKeepsWorldPointUnderPointerStationary()
    {
        var originalPan = new CadPoint(12, -8);
        const double originalZoom = 2;
        const double pointerX = 760;
        const double pointerY = 240;
        const double width = 1000;
        const double height = 700;
        var worldBefore = ScreenToWorld(pointerX, pointerY, width, height, originalZoom, originalPan);

        var view = ViewportNavigation.ZoomAt(originalPan, originalZoom, 1, pointerX, pointerY, width, height);
        var worldAfter = ScreenToWorld(pointerX, pointerY, width, height, view.Zoom, view.Pan);

        Assert.True(view.Zoom > originalZoom);
        Assert.Equal(worldBefore.X, worldAfter.X, 10);
        Assert.Equal(worldBefore.Y, worldAfter.Y, 10);
    }

    [Fact]
    public void ZoomIsClampedToSupportedRange()
    {
        var zoomedIn = ViewportNavigation.ZoomAt(default, 99, 100, 0, 0, 100, 100);
        var zoomedOut = ViewportNavigation.ZoomAt(default, 0.051, -100, 0, 0, 100, 100);

        Assert.Equal(ViewportNavigation.MaximumZoom, zoomedIn.Zoom);
        Assert.Equal(ViewportNavigation.MinimumZoom, zoomedOut.Zoom);
    }

    private static CadPoint ScreenToWorld(
        double x,
        double y,
        double width,
        double height,
        double zoom,
        CadPoint pan) =>
        new((x - width / 2) / zoom - pan.X, -(y - height / 2) / zoom - pan.Y);
}
