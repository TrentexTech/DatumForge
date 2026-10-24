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
