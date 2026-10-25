using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class CoordinateParserTests
{
    [Fact]
    public void AbsoluteCoordinateUsesInvariantDecimals()
    {
        Assert.True(CoordinateParser.TryResolve("10.5,-2.25", null, out var point, out _));
        Assert.Equal(new CadPoint(10.5, -2.25), point);
    }

    [Fact]
    public void RelativeCartesianCoordinateUsesReferencePoint()
    {
        Assert.True(CoordinateParser.TryResolve("@2,-3", new CadPoint(5, 8), out var point, out _));
        Assert.Equal(new CadPoint(7, 5), point);
    }

    [Fact]
    public void RelativePolarCoordinateUsesDegrees()
    {
        Assert.True(CoordinateParser.TryResolve("@10<90", new CadPoint(5, 5), out var point, out _));
        Assert.Equal(5, point.X, 8);
        Assert.Equal(15, point.Y, 8);
    }

    [Fact]
    public void RelativeCoordinateWithoutReferenceExplainsFailure()
    {
        Assert.False(CoordinateParser.TryResolve("@2,3", null, out _, out var error));
        Assert.Contains("reference", error, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ObjectSnapperTests
{
    [Fact]
    public void FindsNearestEndpointAndMidpoint()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 0)));

        var endpoint = ObjectSnapper.FindNearest(drawing, new(0.4, 0.2), 1, ObjectSnapMode.Endpoint);
        var midpoint = ObjectSnapper.FindNearest(drawing, new(5.3, -0.2), 1, ObjectSnapMode.Midpoint);

        Assert.Equal(ObjectSnapKind.Endpoint, endpoint?.Kind);
        Assert.Equal(new CadPoint(0, 0), endpoint?.Point);
        Assert.Equal(ObjectSnapKind.Midpoint, midpoint?.Kind);
        Assert.Equal(new CadPoint(5, 0), midpoint?.Point);
    }

    [Fact]
    public void FindsPolylineVerticesAndSegmentMidpoints()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Polyline,
            LayerId = layer.Id,
            Points = [new(0, 0), new(10, 0), new(10, 10)]
        });

        var vertex = ObjectSnapper.FindNearest(drawing, new(10.2, 0.1), 1, ObjectSnapMode.Endpoint);
        var midpoint = ObjectSnapper.FindNearest(drawing, new(10.2, 5.1), 1, ObjectSnapMode.Midpoint);

        Assert.Equal(new CadPoint(10, 0), vertex?.Point);
        Assert.Equal(new CadPoint(10, 5), midpoint?.Point);
    }

    [Fact]
    public void FindsCircleAndRectangleCenters()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(10, 20)],
            Radius = 5
        });
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(20, 20), new(30, 40)]
        });

        Assert.Equal(new CadPoint(10, 20), ObjectSnapper.FindNearest(
            drawing, new(10.2, 20), 1, ObjectSnapMode.Center)?.Point);
        Assert.Equal(new CadPoint(25, 30), ObjectSnapper.FindNearest(
            drawing, new(24.8, 30), 1, ObjectSnapMode.Center)?.Point);
    }

    [Fact]
    public void FindsRotatedRectangleCornersMidpointsAndCenter()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(0, 0), new(2, 2), new(0, 4), new(-2, 2)]
        });

        Assert.Equal(new CadPoint(2, 2), ObjectSnapper.FindNearest(
            drawing, new(2.1, 2), 1, ObjectSnapMode.Endpoint)?.Point);
        Assert.Equal(new CadPoint(1, 1), ObjectSnapper.FindNearest(
            drawing, new(1.1, 1), 1, ObjectSnapMode.Midpoint)?.Point);
        Assert.Equal(new CadPoint(0, 2), ObjectSnapper.FindNearest(
            drawing, new(0.1, 2), 1, ObjectSnapMode.Center)?.Point);
    }

    [Fact]
    public void FindsCircleQuadrants()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(10, 20)],
            Radius = 5
        });

        var result = ObjectSnapper.FindNearest(drawing, new(10.2, 25.4), 1, ObjectSnapMode.Quadrant);

        Assert.Equal(ObjectSnapKind.Quadrant, result?.Kind);
        Assert.Equal(new CadPoint(10, 25), result?.Point);
    }

    [Fact]
    public void FindsArcEndpointsMidpointCenterQuadrantAndNearestPoint()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Arc,
            LayerId = layer.Id,
            Points = [new(-5, 0), new(0, 5), new(5, 0)]
        });

        AssertPoint(new CadPoint(-5, 0), ObjectSnapper.FindNearest(
            drawing, new(-5.2, 0), 1, ObjectSnapMode.Endpoint)?.Point);
        AssertPoint(new CadPoint(0, 5), ObjectSnapper.FindNearest(
            drawing, new(0, 5.2), 1, ObjectSnapMode.Midpoint)?.Point);
        AssertPoint(new CadPoint(0, 0), ObjectSnapper.FindNearest(
            drawing, new(0.2, 0), 1, ObjectSnapMode.Center)?.Point);
        Assert.Equal(ObjectSnapKind.Quadrant, ObjectSnapper.FindNearest(
            drawing, new(0, 5.2), 1, ObjectSnapMode.Quadrant)?.Kind);
        AssertPoint(new CadPoint(0, 5), ObjectSnapper.FindNearest(
            drawing, new(0, 5.2), 1, ObjectSnapMode.Nearest)?.Point);
    }

    [Fact]
    public void FindsNearestPointOnSegmentsAndCircles()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 0)));
        drawing.Entities.Add(new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(20, 20)],
            Radius = 5
        });

        var lineResult = ObjectSnapper.FindNearest(drawing, new(3, 0.7), 1, ObjectSnapMode.Nearest);
        var circleResult = ObjectSnapper.FindNearest(drawing, new(26, 28), 6, ObjectSnapMode.Nearest);

        Assert.Equal(ObjectSnapKind.Nearest, lineResult?.Kind);
        Assert.Equal(new CadPoint(3, 0), lineResult?.Point);
        Assert.Equal(ObjectSnapKind.Nearest, circleResult?.Kind);
        Assert.Equal(23, circleResult!.Point.X, 10);
        Assert.Equal(24, circleResult.Point.Y, 10);
    }

    [Fact]
    public void SpecializedSnapTakesPriorityOverNearestPoint()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 0)));

        var result = ObjectSnapper.FindNearest(
            drawing,
            new CadPoint(0.4, 0.1),
            1,
            ObjectSnapMode.Endpoint | ObjectSnapMode.Nearest);

        Assert.Equal(ObjectSnapKind.Endpoint, result?.Kind);
        Assert.Equal(new CadPoint(0, 0), result?.Point);
    }

    [Fact]
    public void FindsIntersectionOfVisibleSegments()
    {
        var drawing = DrawingWithLayer(out var layer);
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 10)));
        drawing.Entities.Add(Line(layer.Id, new(0, 10), new(10, 0)));

        var result = ObjectSnapper.FindNearest(drawing, new(5.2, 4.9), 1, ObjectSnapMode.Intersection);

        Assert.Equal(ObjectSnapKind.Intersection, result?.Kind);
        Assert.Equal(new CadPoint(5, 5), result?.Point);
    }

    [Fact]
    public void IgnoresGeometryOnHiddenLayers()
    {
        var drawing = DrawingWithLayer(out var layer);
        layer.IsVisible = false;
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 0)));

        Assert.Null(ObjectSnapper.FindNearest(drawing, new(0, 0), 1, ObjectSnapMode.Endpoint));
    }

    private static DrawingDocument DrawingWithLayer(out DrawingLayer layer)
    {
        layer = new DrawingLayer();
        return new DrawingDocument { Layers = [layer] };
    }

    private static void AssertPoint(CadPoint expected, CadPoint? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.X, actual.Value.X, 8);
        Assert.Equal(expected.Y, actual.Value.Y, 8);
    }

    private static DrawingEntity Line(Guid layerId, CadPoint start, CadPoint end) =>
        new() { Kind = EntityKind.Line, LayerId = layerId, Points = [start, end] };
}

public sealed class TrackingConstraintsTests
{
    [Theory]
    [InlineData(10, 3, 10, 0)]
    [InlineData(-10, 3, -10, 0)]
    [InlineData(3, 10, 0, 10)]
    [InlineData(3, -10, 0, -10)]
    [InlineData(5, 5, 5, 0)]
    public void OrthographicTrackingKeepsTheDominantAxis(
        double targetX,
        double targetY,
        double expectedX,
        double expectedY)
    {
        var constrained = TrackingConstraints.ApplyOrthographic(default, new CadPoint(targetX, targetY));

        Assert.Equal(new CadPoint(expectedX, expectedY), constrained);
    }

    [Theory]
    [InlineData(10, 2, 10.198039027185569, 0)]
    [InlineData(2, 10, 0, 10.198039027185569)]
    [InlineData(8, 7, 7.516648189186454, 7.516648189186453)]
    [InlineData(8, -7, 7.516648189186454, -7.516648189186453)]
    public void PolarTrackingLocksToNearestIncrement(
        double targetX,
        double targetY,
        double expectedX,
        double expectedY)
    {
        var constrained = TrackingConstraints.ApplyPolar(default, new CadPoint(targetX, targetY), 45);

        Assert.Equal(expectedX, constrained.X, 10);
        Assert.Equal(expectedY, constrained.Y, 10);
    }
}
