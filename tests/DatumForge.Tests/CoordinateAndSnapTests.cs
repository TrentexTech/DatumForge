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
}
