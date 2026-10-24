using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public sealed class DrawingEditJournal
{
    private readonly Dictionary<Guid, JournalState> _states = [];

    public bool CanUndo(Guid drawingId) => StateFor(drawingId).Undo.Count > 0;
    public bool CanRedo(Guid drawingId) => StateFor(drawingId).Redo.Count > 0;

    public void RecordEntityAdded(DrawingDocument drawing, DrawingEntity entity)
    {
        Push(drawing.Id, new AddEntityEdit(Clone(entity)));
    }

    public int Erase(DrawingDocument drawing, IEnumerable<Guid> entityIds)
    {
        var requestedIds = entityIds.ToHashSet();
        var lockedLayerIds = drawing.Layers.Where(layer => layer.IsLocked).Select(layer => layer.Id).ToHashSet();
        var removed = drawing.Entities
            .Select((entity, index) => new IndexedEntity(index, entity))
            .Where(item => requestedIds.Contains(item.Entity.Id) && !lockedLayerIds.Contains(item.Entity.LayerId))
            .Select(item => new IndexedEntity(item.Index, Clone(item.Entity)))
            .ToArray();

        if (removed.Length == 0) return 0;
        drawing.Entities.RemoveAll(entity => removed.Any(item => item.Entity.Id == entity.Id));
        Push(drawing.Id, new EraseEntitiesEdit(removed));
        return removed.Length;
    }

    public string? Undo(DrawingDocument drawing)
    {
        var state = StateFor(drawing.Id);
        if (state.Undo.Count == 0) return null;
        var edit = state.Undo.Pop();
        edit.Undo(drawing);
        state.Redo.Push(edit);
        return edit.Description;
    }

    public string? Redo(DrawingDocument drawing)
    {
        var state = StateFor(drawing.Id);
        if (state.Redo.Count == 0) return null;
        var edit = state.Redo.Pop();
        edit.Redo(drawing);
        state.Undo.Push(edit);
        return edit.Description;
    }

    private void Push(Guid drawingId, IDrawingEdit edit)
    {
        var state = StateFor(drawingId);
        state.Undo.Push(edit);
        state.Redo.Clear();
    }

    private JournalState StateFor(Guid drawingId)
    {
        if (!_states.TryGetValue(drawingId, out var state))
        {
            state = new JournalState();
            _states.Add(drawingId, state);
        }
        return state;
    }

    private static DrawingEntity Clone(DrawingEntity source) => new()
    {
        Id = source.Id,
        Kind = source.Kind,
        LayerId = source.LayerId,
        Points = [.. source.Points],
        Radius = source.Radius,
        Text = source.Text
    };

    private sealed class JournalState
    {
        public Stack<IDrawingEdit> Undo { get; } = [];
        public Stack<IDrawingEdit> Redo { get; } = [];
    }

    private interface IDrawingEdit
    {
        string Description { get; }
        void Undo(DrawingDocument drawing);
        void Redo(DrawingDocument drawing);
    }

    private sealed record IndexedEntity(int Index, DrawingEntity Entity);

    private sealed record AddEntityEdit(DrawingEntity Entity) : IDrawingEdit
    {
        public string Description => $"Create {Entity.Kind}";
        public void Undo(DrawingDocument drawing) => drawing.Entities.RemoveAll(item => item.Id == Entity.Id);
        public void Redo(DrawingDocument drawing) => drawing.Entities.Add(Clone(Entity));
    }

    private sealed record EraseEntitiesEdit(IReadOnlyList<IndexedEntity> Entities) : IDrawingEdit
    {
        public string Description => Entities.Count == 1 ? "Erase entity" : $"Erase {Entities.Count} entities";

        public void Undo(DrawingDocument drawing)
        {
            foreach (var item in Entities.OrderBy(item => item.Index))
                drawing.Entities.Insert(Math.Min(item.Index, drawing.Entities.Count), Clone(item.Entity));
        }

        public void Redo(DrawingDocument drawing)
        {
            var ids = Entities.Select(item => item.Entity.Id).ToHashSet();
            drawing.Entities.RemoveAll(item => ids.Contains(item.Id));
        }
    }
}
