using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class ArcGeometryTests
{
    [Fact]
    public void ThreePointsDefineTheExpectedArc()
    {
        Assert.True(ArcGeometry.TryCreate(new(-5, 0), new(0, 5), new(5, 0), out var arc));

        AssertPoint(new(0, 0), arc.Center);
        Assert.Equal(5, arc.Radius, 8);
        Assert.Equal(-Math.PI, arc.SweepAngle, 8);
        AssertPoint(new(0, 5), arc.PointAt(0.5));
    }

    [Fact]
    public void CollinearPointsDoNotDefineAnArc()
    {
        Assert.False(ArcGeometry.TryCreate(new(0, 0), new(5, 0), new(10, 0), out _));
    }

    [Fact]
    public void NearestPointStaysOnTheArcSweep()
    {
        Assert.True(ArcGeometry.TryCreate(new(-5, 0), new(0, 5), new(5, 0), out var arc));

        AssertPoint(new(0, 5), ArcGeometry.NearestPoint(arc, new(0, 7)));
        AssertPoint(new(-5, 0), ArcGeometry.NearestPoint(arc, new(0, -5)));
    }

    private static void AssertPoint(CadPoint expected, CadPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
