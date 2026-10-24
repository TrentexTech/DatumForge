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

    private static DrawingDocument DrawingWithLayer(out DrawingLayer layer)
    {
        layer = new DrawingLayer();
        return new DrawingDocument { Layers = [layer] };
    }
}
