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

    [Fact]
    public void NearestPointStillChoosesTheStrictlyCloserEndpoint()
    {
        Assert.True(ArcGeometry.TryCreate(new(-5, 0), new(0, 5), new(5, 0), out var arc));

        AssertPoint(new(5, 0), ArcGeometry.NearestPoint(arc, new(4, -3)));
    }

    [Fact]
    public void NumericallyEquivalentEndpointDistancesPreferArcStart()
    {
        const double tinyAngleError = 1e-13;
        var arc = new CadArc(default, 5, Math.PI - tinyAngleError, -Math.PI + tinyAngleError);

        AssertPoint(arc.Start, ArcGeometry.NearestPoint(arc, new CadPoint(0, -5)));
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(1_000_000, -2_000_000, 0.001)]
    [InlineData(-250.25, 999.5, 1_000_000)]
    public void EndpointTieBreakIsStableAcrossTranslationsAndScales(double centerX, double centerY, double radius)
    {
        var center = new CadPoint(centerX, centerY);
        var arc = new CadArc(center, radius, Math.PI, -Math.PI);
        var query = new CadPoint(centerX, centerY - radius);

        AssertPoint(arc.Start, ArcGeometry.NearestPoint(arc, query));
    }

    [Fact]
    public void ArcCenterDeterministicallyReturnsStartPoint()
    {
        Assert.True(ArcGeometry.TryCreate(new(-5, 0), new(0, 5), new(5, 0), out var arc));

        AssertPoint(arc.Start, ArcGeometry.NearestPoint(arc, arc.Center));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void RadialProjectionRemainsOnArcSweep(double fraction)
    {
        Assert.True(ArcGeometry.TryCreate(new(-5, 0), new(0, 5), new(5, 0), out var arc));
        var expected = arc.PointAt(fraction);
        var query = new CadPoint(
            arc.Center.X + (expected.X - arc.Center.X) * 3,
            arc.Center.Y + (expected.Y - arc.Center.Y) * 3);

        var nearest = ArcGeometry.NearestPoint(arc, query);

        AssertPoint(expected, nearest);
        Assert.Equal(arc.Radius, Distance(arc.Center, nearest), 8);
        Assert.True(arc.ContainsAngle(Math.Atan2(nearest.Y - arc.Center.Y, nearest.X - arc.Center.X)));
    }

    [Theory]
    [InlineData(0, 0, 5)]
    [InlineData(1_000_000, -2_000_000, 5)]
    [InlineData(-1_000_000_000, 1_000_000_000, 250)]
    public void ThreePointConstructionIsTranslationInvariant(double centerX, double centerY, double radius)
    {
        Assert.True(ArcGeometry.TryCreate(
            new CadPoint(centerX - radius, centerY),
            new CadPoint(centerX, centerY + radius),
            new CadPoint(centerX + radius, centerY),
            out var arc));

        AssertPoint(new CadPoint(centerX, centerY), arc.Center, 6);
        Assert.Equal(radius, arc.Radius, 6);
        Assert.Equal(-Math.PI, arc.SweepAngle, 8);
    }

    private static void AssertPoint(CadPoint expected, CadPoint actual, int precision = 8)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }

    private static double Distance(CadPoint first, CadPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
}
