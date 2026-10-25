using DatumForge.Core.Editing;
using DatumForge.Core.Models;

namespace DatumForge.Tests;

public sealed class EntityHitTesterTests
{
    [Fact]
    public void HitTestChoosesTopmostVisibleEntity()
    {
        var drawing = DrawingWithLayer(out var layer);
        var bottom = Line(layer.Id, new(0, 0), new(10, 0));
        var top = Line(layer.Id, new(0, 0), new(10, 0));
        drawing.Entities.AddRange([bottom, top]);

        Assert.Same(top, EntityHitTester.HitTest(drawing, new(5, 0.5), 1));
    }

    [Fact]
    public void HitTestIgnoresEntitiesOnHiddenLayers()
    {
        var drawing = DrawingWithLayer(out var layer);
        layer.IsVisible = false;
        drawing.Entities.Add(Line(layer.Id, new(0, 0), new(10, 0)));

        Assert.Null(EntityHitTester.HitTest(drawing, new(5, 0), 1));
    }

    [Fact]
    public void HitTestFindsCircleEdgeButNotItsEmptyCenter()
    {
        var drawing = DrawingWithLayer(out var layer);
        var circle = new DrawingEntity { Kind = EntityKind.Circle, LayerId = layer.Id, Points = [new(10, 10)], Radius = 5 };
        drawing.Entities.Add(circle);

        Assert.Same(circle, EntityHitTester.HitTest(drawing, new(15.5, 10), 1));
        Assert.Null(EntityHitTester.HitTest(drawing, new(10, 10), 1));
    }

    [Fact]
    public void HitTestFindsArcSweepButNotTheOppositeSideOfItsCircle()
    {
        var drawing = DrawingWithLayer(out var layer);
        var arc = new DrawingEntity
        {
            Kind = EntityKind.Arc,
            LayerId = layer.Id,
            Points = [new(-5, 0), new(0, 5), new(5, 0)]
        };
        drawing.Entities.Add(arc);

        Assert.Same(arc, EntityHitTester.HitTest(drawing, new(0, 5.2), 0.5));
        Assert.Null(EntityHitTester.HitTest(drawing, new(0, -5), 0.5));
    }

    [Fact]
    public void HitTestFindsEveryPolylineSegment()
    {
        var drawing = DrawingWithLayer(out var layer);
        var polyline = new DrawingEntity
        {
            Kind = EntityKind.Polyline,
            LayerId = layer.Id,
            Points = [new(0, 0), new(10, 0), new(10, 10)]
        };
        drawing.Entities.Add(polyline);

        Assert.Same(polyline, EntityHitTester.HitTest(drawing, new(5, 0.2), 0.5));
        Assert.Same(polyline, EntityHitTester.HitTest(drawing, new(9.8, 7), 0.5));
    }

    [Fact]
    public void HitTestFollowsRotatedRectangleEdges()
    {
        var drawing = DrawingWithLayer(out var layer);
        var rectangle = new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(0, 0), new(2, 2), new(0, 4), new(-2, 2)]
        };
        drawing.Entities.Add(rectangle);

        Assert.Same(rectangle, EntityHitTester.HitTest(drawing, new(1, 1.2), 0.25));
        Assert.Null(EntityHitTester.HitTest(drawing, new(2, 0), 0.25));
    }

    [Fact]
    public void HitTestFindsDimensionLine()
    {
        var drawing = DrawingWithLayer(out var layer);
        var dimension = new DrawingEntity
        {
            Kind = EntityKind.Dimension,
            LayerId = layer.Id,
            Points = [new(0, 0), new(10, 0)]
        };
        drawing.Entities.Add(dimension);

        Assert.Same(dimension, EntityHitTester.HitTest(drawing, new(5, 0.2), 0.5));
        Assert.Null(EntityHitTester.HitTest(drawing, new(5, 2), 0.5));
    }

    [Fact]
    public void HitTestUsesTextBoundsInsteadOfOnlyItsInsertionPoint()
    {
        var drawing = DrawingWithLayer(out var layer);
        var text = new DrawingEntity
        {
            Kind = EntityKind.Text,
            LayerId = layer.Id,
            Points = [new(0, 0)],
            Text = "TEST",
            TextHeight = 10
        };
        drawing.Entities.Add(text);

        Assert.Same(text, EntityHitTester.HitTest(drawing, new(12, 5), 0.5));
        Assert.Null(EntityHitTester.HitTest(drawing, new(30, 5), 0.5));
    }

    private static DrawingDocument DrawingWithLayer(out DrawingLayer layer)
    {
        layer = new DrawingLayer();
        return new DrawingDocument { Layers = [layer] };
    }

    private static DrawingEntity Line(Guid layerId, CadPoint start, CadPoint end) =>
        new() { Kind = EntityKind.Line, LayerId = layerId, Points = [start, end] };
}

public sealed class EntityRectangleSelectorTests
{
    [Fact]
    public void WindowSelectionRequiresCompleteContainmentWhileCrossingSelectionOnlyRequiresContact()
    {
        var drawing = DrawingWithLayer(out var layer);
        var enclosed = Line(layer.Id, new(2, 2), new(8, 2));
        var crossing = Line(layer.Id, new(-5, 5), new(5, 5));
        var outside = Line(layer.Id, new(12, 2), new(15, 2));
        drawing.Entities.AddRange([enclosed, crossing, outside]);

        var window = EntityRectangleSelector.Select(drawing, new(0, 0), new(10, 10), crossing: false);
        var crossingWindow = EntityRectangleSelector.Select(drawing, new(0, 0), new(10, 10), crossing: true);

        Assert.Equal([enclosed.Id], window);
        Assert.Equal([enclosed.Id, crossing.Id], crossingWindow);
    }

    [Fact]
    public void SelectionRectangleHandlesCirclesArcsAndRotatedRectangles()
    {
        var drawing = DrawingWithLayer(out var layer);
        var circle = new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(5, 5)],
            Radius = 2
        };
        var surroundingCircle = new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(5, 5)],
            Radius = 20
        };
        var arc = new DrawingEntity
        {
            Kind = EntityKind.Arc,
            LayerId = layer.Id,
            Points = [new(-5, 0), new(0, 5), new(5, 0)]
        };
        var rectangle = new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(0, 0), new(2, 2), new(0, 4), new(-2, 2)]
        };
        drawing.Entities.AddRange([circle, surroundingCircle, arc, rectangle]);

        var circleWindow = EntityRectangleSelector.Select(drawing, new(2, 2), new(8, 8), crossing: false);
        var insideLargeCircle = EntityRectangleSelector.Select(drawing, new(4, 4), new(6, 6), crossing: true);
        var arcCrossing = EntityRectangleSelector.Select(drawing, new(-1, 4), new(1, 6), crossing: true);
        var rectangleCrossing = EntityRectangleSelector.Select(drawing, new(1, 0.5), new(3, 2), crossing: true);

        Assert.Contains(circle.Id, circleWindow);
        Assert.DoesNotContain(surroundingCircle.Id, insideLargeCircle);
        Assert.Contains(arc.Id, arcCrossing);
        Assert.Contains(rectangle.Id, rectangleCrossing);
    }

    [Fact]
    public void SelectionRectangleIgnoresHiddenLayerGeometry()
    {
        var drawing = DrawingWithLayer(out var layer);
        layer.IsVisible = false;
        drawing.Entities.Add(Line(layer.Id, new(2, 2), new(8, 2)));

        Assert.Empty(EntityRectangleSelector.Select(drawing, new(0, 0), new(10, 10), crossing: true));
    }

    [Fact]
    public void SelectionRectangleUsesCompleteRotatedTextBounds()
    {
        var drawing = DrawingWithLayer(out var layer);
        var text = new DrawingEntity
        {
            Kind = EntityKind.Text,
            LayerId = layer.Id,
            Points = [new(10, 20)],
            Text = "AB",
            TextHeight = 10,
            TextRotationDegrees = 90
        };
        drawing.Entities.Add(text);

        Assert.Contains(text.Id, EntityRectangleSelector.Select(
            drawing, new(-1, 19), new(11, 33), crossing: false));
        Assert.DoesNotContain(text.Id, EntityRectangleSelector.Select(
            drawing, new(1, 19), new(11, 33), crossing: false));
        Assert.Contains(text.Id, EntityRectangleSelector.Select(
            drawing, new(-1, 25), new(1, 27), crossing: true));
    }

    private static DrawingDocument DrawingWithLayer(out DrawingLayer layer)
    {
        layer = new DrawingLayer();
        return new DrawingDocument { Layers = [layer] };
    }

    private static DrawingEntity Line(Guid layerId, CadPoint start, CadPoint end) =>
        new() { Kind = EntityKind.Line, LayerId = layerId, Points = [start, end] };
}

public sealed class DrawingEditJournalTests
{
    [Fact]
    public void ClearRemovesUndoAndRedoHistory()
    {
        var drawing = DrawingWithLayer(out var layer);
        var entity = new DrawingEntity { Kind = EntityKind.Line, LayerId = layer.Id };
        drawing.Entities.Add(entity);
        var journal = new DrawingEditJournal();
        journal.RecordEntityAdded(drawing, entity);
        journal.Undo(drawing);

        journal.Clear(drawing.Id);

        Assert.False(journal.CanUndo(drawing.Id));
        Assert.False(journal.CanRedo(drawing.Id));
    }

    [Fact]
    public void CreatedEntityCanBeUndoneAndRedone()
    {
        var drawing = DrawingWithLayer(out var layer);
        var entity = new DrawingEntity { Kind = EntityKind.Line, LayerId = layer.Id, Points = [new(0, 0), new(10, 0)] };
        drawing.Entities.Add(entity);
        var journal = new DrawingEditJournal();
        journal.RecordEntityAdded(drawing, entity);

        Assert.Equal("Create Line", journal.Undo(drawing));
        Assert.Empty(drawing.Entities);
        Assert.Equal("Create Line", journal.Redo(drawing));
        Assert.Equal(entity.Id, Assert.Single(drawing.Entities).Id);
    }

    [Fact]
    public void ErasePreservesLockedLayerEntitiesAndCanBeUndone()
    {
        var drawing = DrawingWithLayer(out var editableLayer);
        var lockedLayer = new DrawingLayer { IsLocked = true };
        drawing.Layers.Add(lockedLayer);
        var editable = new DrawingEntity { LayerId = editableLayer.Id };
        var locked = new DrawingEntity { LayerId = lockedLayer.Id };
        drawing.Entities.AddRange([editable, locked]);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Erase(drawing, [editable.Id, locked.Id]));
        Assert.Same(locked, Assert.Single(drawing.Entities));
        Assert.Equal("Erase entity", journal.Undo(drawing));
        Assert.Equal([editable.Id, locked.Id], drawing.Entities.Select(item => item.Id));
    }

    [Fact]
    public void NewEditClearsRedoHistory()
    {
        var drawing = DrawingWithLayer(out var layer);
        var first = new DrawingEntity { LayerId = layer.Id };
        drawing.Entities.Add(first);
        var journal = new DrawingEditJournal();
        journal.RecordEntityAdded(drawing, first);
        journal.Undo(drawing);
        var second = new DrawingEntity { LayerId = layer.Id };
        drawing.Entities.Add(second);
        journal.RecordEntityAdded(drawing, second);

        Assert.False(journal.CanRedo(drawing.Id));
    }

    [Fact]
    public void MoveChangesOnlyEditableEntitiesAndRoundTripsThroughJournal()
    {
        var drawing = DrawingWithLayer(out var editableLayer);
        var lockedLayer = new DrawingLayer { IsLocked = true };
        drawing.Layers.Add(lockedLayer);
        var editable = new DrawingEntity { LayerId = editableLayer.Id, Points = [new(1, 2), new(3, 4)] };
        var locked = new DrawingEntity { LayerId = lockedLayer.Id, Points = [new(5, 6)] };
        drawing.Entities.AddRange([editable, locked]);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Move(drawing, [editable.Id, locked.Id], new(0, 0), new(10, -5)));
        Assert.Equal(new CadPoint(11, -3), editable.Points[0]);
        Assert.Equal(new CadPoint(5, 6), locked.Points[0]);
        Assert.Equal("Move entity", journal.Undo(drawing));
        Assert.Equal(new CadPoint(1, 2), editable.Points[0]);
        Assert.Equal("Move entity", journal.Redo(drawing));
        Assert.Equal(new CadPoint(13, -1), editable.Points[1]);
    }

    [Fact]
    public void CopyCreatesNewIdentityAndCanBeUndoneAndRedone()
    {
        var drawing = DrawingWithLayer(out var layer);
        var source = new DrawingEntity { LayerId = layer.Id, Points = [new(2, 3)] };
        drawing.Entities.Add(source);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Copy(drawing, [source.Id], new(0, 0), new(5, 7)));
        var copy = Assert.Single(drawing.Entities, entity => entity.Id != source.Id);
        Assert.Equal(new CadPoint(7, 10), Assert.Single(copy.Points));
        Assert.Equal(layer.Id, copy.LayerId);
        Assert.Equal("Copy entity", journal.Undo(drawing));
        Assert.Single(drawing.Entities);
        Assert.Equal("Copy entity", journal.Redo(drawing));
        Assert.Contains(drawing.Entities, entity => entity.Id == copy.Id);
    }

    [Fact]
    public void RotateUsesSelectionCenterAndPreservesEntityIdentity()
    {
        var drawing = DrawingWithLayer(out var layer);
        var line = new DrawingEntity { LayerId = layer.Id, Points = [new(0, 0), new(10, 0)] };
        drawing.Entities.Add(line);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Rotate(drawing, [line.Id], new(10, 0), new(5, 5)));
        AssertPoint(new(5, -5), line.Points[0]);
        AssertPoint(new(5, 5), line.Points[1]);
        Assert.Equal(line.Id, Assert.Single(drawing.Entities).Id);
    }

    [Fact]
    public void TextRotationAndHeightRoundTripThroughTransformsAndUndo()
    {
        var drawing = DrawingWithLayer(out var layer);
        var text = new DrawingEntity
        {
            Kind = EntityKind.Text,
            LayerId = layer.Id,
            Points = [new(0, 0)],
            Text = "AB",
            TextHeight = 10
        };
        drawing.Entities.Add(text);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Rotate(drawing, [text.Id], new(7, 5), new(6, 6)));
        Assert.Equal(90, text.TextRotationDegrees, 8);
        Assert.Equal(1, journal.Scale(drawing, [text.Id], new(6, 6), new(6, 7)));
        Assert.Equal(20, text.TextHeight, 8);
        Assert.Equal("Scale entity", journal.Undo(drawing));
        Assert.Equal(10, text.TextHeight, 8);
        Assert.Equal(90, text.TextRotationDegrees, 8);
        Assert.Equal("Rotate entity", journal.Undo(drawing));
        Assert.Equal(0, text.TextRotationDegrees, 8);
        Assert.Equal(new CadPoint(0, 0), Assert.Single(text.Points));
    }

    [Fact]
    public void RotateExpandsLegacyRectangleAndPreservesAllFourCorners()
    {
        var drawing = DrawingWithLayer(out var layer);
        var rectangle = new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(0, 0), new(4, 2)]
        };
        drawing.Entities.Add(rectangle);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Rotate(drawing, [rectangle.Id], new(4, 1), new(2, 3)));

        Assert.Equal(4, rectangle.Points.Count);
        AssertPoint(new(3, -1), rectangle.Points[0]);
        AssertPoint(new(3, 3), rectangle.Points[1]);
        AssertPoint(new(1, 3), rectangle.Points[2]);
        AssertPoint(new(1, -1), rectangle.Points[3]);
        Assert.Equal("Rotate entity", journal.Undo(drawing));
        Assert.Equal([new CadPoint(0, 0), new CadPoint(4, 2)], rectangle.Points);
        Assert.Equal("Rotate entity", journal.Redo(drawing));
        Assert.Equal(4, rectangle.Points.Count);
    }

    [Fact]
    public void ScaleUpdatesPointsAndCircleRadius()
    {
        var drawing = DrawingWithLayer(out var layer);
        var circle = new DrawingEntity { Kind = EntityKind.Circle, LayerId = layer.Id, Points = [new(4, 6)], Radius = 5 };
        drawing.Entities.Add(circle);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Scale(drawing, [circle.Id], new(9, 6), new(14, 6)));
        AssertPoint(new(4, 6), Assert.Single(circle.Points));
        Assert.Equal(10, circle.Radius, 8);
        Assert.Equal("Scale entity", journal.Undo(drawing));
        Assert.Equal(5, circle.Radius, 8);
    }

    [Fact]
    public void MirrorReflectsGeometryAcrossAxisAndRoundTripsThroughJournal()
    {
        var drawing = DrawingWithLayer(out var layer);
        var line = new DrawingEntity
        {
            Kind = EntityKind.Line,
            LayerId = layer.Id,
            Points = [new(1, 2), new(4, -1)]
        };
        var circle = new DrawingEntity
        {
            Kind = EntityKind.Circle,
            LayerId = layer.Id,
            Points = [new(2, 3)],
            Radius = 4
        };
        drawing.Entities.AddRange([line, circle]);
        var journal = new DrawingEditJournal();

        Assert.Equal(2, journal.Mirror(drawing, [line.Id, circle.Id], new(5, -10), new(5, 10)));
        AssertPoint(new(9, 2), line.Points[0]);
        AssertPoint(new(6, -1), line.Points[1]);
        AssertPoint(new(8, 3), circle.Points[0]);
        Assert.Equal(4, circle.Radius);
        Assert.Equal("Mirror 2 entities", journal.Undo(drawing));
        AssertPoint(new(1, 2), line.Points[0]);
        Assert.Equal("Mirror 2 entities", journal.Redo(drawing));
        AssertPoint(new(8, 3), circle.Points[0]);
    }

    [Fact]
    public void MirrorRejectsDegenerateAxis()
    {
        var drawing = DrawingWithLayer(out var layer);
        var line = new DrawingEntity { LayerId = layer.Id, Points = [new(1, 2), new(3, 4)] };
        drawing.Entities.Add(line);
        var journal = new DrawingEditJournal();

        Assert.Equal(0, journal.Mirror(drawing, [line.Id], new(5, 5), new(5, 5)));
        Assert.False(journal.CanUndo(drawing.Id));
        Assert.Equal([new CadPoint(1, 2), new CadPoint(3, 4)], line.Points);
    }

    [Fact]
    public void MirrorPreservesRectangleAcrossDiagonalAxis()
    {
        var drawing = DrawingWithLayer(out var layer);
        var rectangle = new DrawingEntity
        {
            Kind = EntityKind.Rectangle,
            LayerId = layer.Id,
            Points = [new(0, 0), new(4, 0), new(4, 2), new(0, 2)]
        };
        drawing.Entities.Add(rectangle);
        var journal = new DrawingEditJournal();

        Assert.Equal(1, journal.Mirror(drawing, [rectangle.Id], new(0, 0), new(5, 5)));

        AssertPoint(new(0, 0), rectangle.Points[0]);
        AssertPoint(new(0, 4), rectangle.Points[1]);
        AssertPoint(new(2, 4), rectangle.Points[2]);
        AssertPoint(new(2, 0), rectangle.Points[3]);
    }

    [Fact]
    public void RotateRejectsReferenceAtSelectionCenter()
    {
        var drawing = DrawingWithLayer(out var layer);
        var line = new DrawingEntity { LayerId = layer.Id, Points = [new(0, 0), new(10, 0)] };
        drawing.Entities.Add(line);
        var journal = new DrawingEditJournal();

        Assert.Equal(0, journal.Rotate(drawing, [line.Id], new(5, 0), new(5, 5)));
        Assert.False(journal.CanUndo(drawing.Id));
        Assert.Equal([new CadPoint(0, 0), new CadPoint(10, 0)], line.Points);
    }

    private static void AssertPoint(CadPoint expected, CadPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }

    private static DrawingDocument DrawingWithLayer(out DrawingLayer layer)
    {
        layer = new DrawingLayer();
        return new DrawingDocument { Layers = [layer] };
    }
}
