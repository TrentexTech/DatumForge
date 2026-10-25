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

public sealed class ViewportSelectionBehaviorTests
{
    [Theory]
    [InlineData(100, 160, false)]
    [InlineData(160, 100, true)]
    [InlineData(100, 100, false)]
    public void HorizontalDirectionDeterminesCrossingMode(double startX, double currentX, bool expected)
    {
        Assert.Equal(expected, ViewportSelectionBehavior.IsCrossing(startX, currentX));
    }

    [Theory]
    [InlineData(3, 2, false)]
    [InlineData(4, 0, true)]
    [InlineData(-3, -3, true)]
    public void MinimumMovementDistinguishesClickFromDrag(double deltaX, double deltaY, bool expected)
    {
        Assert.Equal(expected, ViewportSelectionBehavior.IsDrag(deltaX, deltaY));
    }
}

public sealed class ViewportCoordinateDisplayTests
{
    [Fact]
    public void CoordinateReadoutUsesCompactInvariantFormatting()
    {
        Assert.Equal("X: 12.346   Y: -7.5", ViewportCoordinateDisplay.Format(new CadPoint(12.3456, -7.5)));
    }

    [Fact]
    public void CoordinateReadoutShowsPlaceholdersOutsideViewer()
    {
        Assert.Equal("X: —   Y: —", ViewportCoordinateDisplay.Format(null));
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

public sealed class DrawingViewportDimensionTests
{
    [Fact]
    public void CoordinateInputCreatesDimensionFromTwoDistinctPoints()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer], Units = DrawingUnits.Millimeters };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Dimension" };

        Assert.False(viewport.SubmitCoordinate("0,0").Completed);
        var result = viewport.SubmitCoordinate("3,4");

        Assert.True(result.Success);
        Assert.True(result.Completed);
        var dimension = Assert.Single(drawing.Entities);
        Assert.Equal(EntityKind.Dimension, dimension.Kind);
        Assert.Equal([new CadPoint(0, 0), new CadPoint(3, 4)], dimension.Points);
    }

    [Fact]
    public void CoordinateInputRejectsZeroLengthDimensionAndKeepsFirstPoint()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer] };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Dimension" };

        viewport.SubmitCoordinate("2,2");
        var rejected = viewport.SubmitCoordinate("2,2");
        var completed = viewport.SubmitCoordinate("5,6");

        Assert.False(rejected.Success);
        Assert.Contains("distinct", rejected.Message, StringComparison.Ordinal);
        Assert.True(completed.Completed);
        Assert.Equal([new CadPoint(2, 2), new CadPoint(5, 6)], Assert.Single(drawing.Entities).Points);
    }
}

public sealed class DrawingViewportTextTests
{
    [Fact]
    public void CoordinateInputSetsInsertionPointThenCreatesLiteralText()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer] };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Text" };
        var textRequests = 0;
        viewport.TextEntryRequested += (_, _) => textRequests++;

        var insertion = viewport.SubmitCoordinate("10,20");
        var completed = viewport.SubmitCoordinate("line");

        Assert.True(insertion.Success);
        Assert.False(insertion.Completed);
        Assert.Equal(1, textRequests);
        Assert.False(viewport.IsAwaitingTextEntry);
        Assert.True(completed.Completed);
        var text = Assert.Single(drawing.Entities);
        Assert.Equal(EntityKind.Text, text.Kind);
        Assert.Equal("line", text.Text);
        Assert.Equal(TextGeometry.DefaultHeight, text.TextHeight);
        Assert.Equal(new CadPoint(10, 20), Assert.Single(text.Points));
    }

    [Fact]
    public void BlankTextIsRejectedWithoutLosingInsertionPoint()
    {
        var layer = new DrawingLayer();
        var drawing = new DrawingDocument { Layers = [layer] };
        var viewport = new DrawingViewport { Drawing = drawing, ActiveTool = "Text" };

        viewport.SubmitCoordinate("2,3");
        var rejected = viewport.SubmitCoordinate("  ");

        Assert.False(rejected.Success);
        Assert.True(viewport.IsAwaitingTextEntry);
        Assert.Empty(drawing.Entities);
        Assert.True(viewport.HandleEscape());
        Assert.False(viewport.IsAwaitingTextEntry);
    }
}
