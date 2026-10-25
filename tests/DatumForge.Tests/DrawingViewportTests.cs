using DatumForge.App.Controls;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class ViewportEscapeBehaviorTests
{
    [Theory]
    [InlineData(false, false, "Line", ViewportEscapeAction.SelectTool)]
    [InlineData(false, false, "Select", ViewportEscapeAction.None)]
    [InlineData(true, true, "Line", ViewportEscapeAction.CancelInteraction)]
    [InlineData(true, false, "Line", ViewportEscapeAction.SelectTool)]
    [InlineData(true, false, "Select", ViewportEscapeAction.ClearSelection)]
    public void EscapeActionReflectsDrawingAndInteractionState(
        bool hasDrawing,
        bool hasActiveOperation,
        string activeTool,
        ViewportEscapeAction expected)
    {
        Assert.Equal(expected, ViewportEscapeBehavior.Decide(hasDrawing, hasActiveOperation, activeTool));
    }
}

public sealed class PolylineConstructionTests
{
    [Fact]
    public void ConstructionCollectsMultipleVerticesAndCompletesAsOnePolyline()
    {
        var construction = new PolylineConstruction();

        Assert.True(construction.TryAddVertex(new CadPoint(0, 0)));
        Assert.True(construction.TryAddVertex(new CadPoint(10, 0)));
        Assert.True(construction.TryAddVertex(new CadPoint(10, 5)));
        Assert.True(construction.TryComplete(out var vertices));

        Assert.Equal([new CadPoint(0, 0), new CadPoint(10, 0), new CadPoint(10, 5)], vertices);
        Assert.False(construction.IsActive);
    }

    [Fact]
    public void ConstructionRejectsRepeatedVertexAndCannotFinishWithOneVertex()
    {
        var construction = new PolylineConstruction();
        var vertex = new CadPoint(4, 2);

        Assert.True(construction.TryAddVertex(vertex));
        Assert.False(construction.TryAddVertex(vertex));
        Assert.False(construction.TryComplete(out var vertices));

        Assert.Empty(vertices);
        Assert.Equal([vertex], construction.Vertices);
    }

    [Fact]
    public void ResetDiscardsAnUnfinishedPolyline()
    {
        var construction = new PolylineConstruction();
        construction.TryAddVertex(new CadPoint(1, 1));
        construction.TryAddVertex(new CadPoint(2, 3));

        construction.Reset();

        Assert.Empty(construction.Vertices);
        Assert.False(construction.CanComplete);
    }
}

public sealed class DrawingViewportPolylineTests
{
    [Fact]
    public void CoordinateInputCreatesOneMultiVertexPolylineWhenFinished()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer] };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Polyline" };

        Assert.False(viewport.SubmitCoordinate("0,0").Completed);
        Assert.False(viewport.SubmitCoordinate("10,0").Completed);
        Assert.False(viewport.SubmitCoordinate("@0,5").Completed);
        Assert.Empty(drawing.Entities);

        var result = viewport.SubmitCoordinate(string.Empty);

        Assert.True(result.Success);
        Assert.True(result.Completed);
        var polyline = Assert.Single(drawing.Entities);
        Assert.Equal(EntityKind.Polyline, polyline.Kind);
        Assert.Equal([new CadPoint(0, 0), new CadPoint(10, 0), new CadPoint(10, 5)], polyline.Points);
    }
}

public sealed class DrawingViewportRectangleTests
{
    [Fact]
    public void CoordinateInputCreatesRectangleWithFourOrderedCorners()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer] };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Rectangle" };

        Assert.False(viewport.SubmitCoordinate("0,0").Completed);
        Assert.True(viewport.SubmitCoordinate("4,2").Completed);

        var rectangle = Assert.Single(drawing.Entities);
        Assert.Equal(EntityKind.Rectangle, rectangle.Kind);
        Assert.Equal(
            [new CadPoint(0, 0), new CadPoint(4, 0), new CadPoint(4, 2), new CadPoint(0, 2)],
            rectangle.Points);
    }
}
