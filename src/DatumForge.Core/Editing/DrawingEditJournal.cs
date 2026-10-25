using DatumForge.Core.Models;

namespace DatumForge.Core.Editing;

public sealed class DrawingEditJournal
{
    private readonly Dictionary<Guid, JournalState> _states = [];

    public bool CanUndo(Guid drawingId) => StateFor(drawingId).Undo.Count > 0;
    public bool CanRedo(Guid drawingId) => StateFor(drawingId).Redo.Count > 0;
    public void Clear(Guid drawingId) => _states.Remove(drawingId);

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

    public int Move(DrawingDocument drawing, IEnumerable<Guid> entityIds, CadPoint reference, CadPoint target)
    {
        var dx = target.X - reference.X;
        var dy = target.Y - reference.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return 0;
        return Transform(drawing, entityIds, "Move", entity =>
            entity.Points = entity.Points.Select(point => new CadPoint(point.X + dx, point.Y + dy)).ToList());
    }

    public int Copy(DrawingDocument drawing, IEnumerable<Guid> entityIds, CadPoint reference, CadPoint target)
    {
        var dx = target.X - reference.X;
        var dy = target.Y - reference.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return 0;

        var source = EditableEntities(drawing, entityIds).ToArray();
        if (source.Length == 0) return 0;
        var copies = source.Select(entity =>
        {
            var copy = Clone(entity);
            RectangleGeometry.Normalize(copy);
            copy.Id = Guid.NewGuid();
            copy.Points = copy.Points.Select(point => new CadPoint(point.X + dx, point.Y + dy)).ToList();
            return copy;
        }).ToArray();
        drawing.Entities.AddRange(copies);
        Push(drawing.Id, new AddEntitiesEdit("Copy", copies.Select(Clone).ToArray()));
        return copies.Length;
    }

    public int Rotate(DrawingDocument drawing, IEnumerable<Guid> entityIds, CadPoint reference, CadPoint target)
    {
        var ids = EditableEntities(drawing, entityIds).Select(entity => entity.Id).ToArray();
        var center = EntityGeometry.SelectionCenter(drawing, ids);
        if (center is null) return 0;
        var angle = EntityGeometry.AngleBetween(center.Value, reference, target);
        if (!double.IsFinite(angle) || Math.Abs(angle) < 1e-9) return 0;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return Transform(drawing, ids, "Rotate", entity =>
        {
            entity.Points = entity.Points.Select(point =>
            {
                var x = point.X - center.Value.X;
                var y = point.Y - center.Value.Y;
                return new CadPoint(
                    center.Value.X + x * cosine - y * sine,
                    center.Value.Y + x * sine + y * cosine);
            }).ToList();
            if (entity.Kind == EntityKind.Text)
                entity.TextRotationDegrees = TextGeometry.NormalizeDegrees(
                    entity.TextRotationDegrees + angle * 180 / Math.PI);
        });
    }

    public int Scale(DrawingDocument drawing, IEnumerable<Guid> entityIds, CadPoint reference, CadPoint target)
    {
        var ids = EditableEntities(drawing, entityIds).Select(entity => entity.Id).ToArray();
        var center = EntityGeometry.SelectionCenter(drawing, ids);
        if (center is null) return 0;
        var factor = EntityGeometry.ScaleBetween(center.Value, reference, target);
        if (!double.IsFinite(factor) || factor <= 0 || Math.Abs(factor - 1) < 1e-9) return 0;
        return Transform(drawing, ids, "Scale", entity =>
        {
            entity.Points = entity.Points.Select(point => new CadPoint(
                center.Value.X + (point.X - center.Value.X) * factor,
                center.Value.Y + (point.Y - center.Value.Y) * factor)).ToList();
            entity.Radius *= factor;
            if (entity.Kind == EntityKind.Text)
                entity.TextHeight = TextGeometry.Height(entity) * factor;
        });
    }

    public int Mirror(DrawingDocument drawing, IEnumerable<Guid> entityIds, CadPoint axisStart, CadPoint axisEnd)
    {
        var axisX = axisEnd.X - axisStart.X;
        var axisY = axisEnd.Y - axisStart.Y;
        if (axisX * axisX + axisY * axisY < 1e-18) return 0;

        return Transform(drawing, entityIds, "Mirror", entity =>
        {
            if (entity.Kind == EntityKind.Text)
                entity.TextRotationDegrees = TextGeometry.MirroredRotation(
                    entity.TextRotationDegrees, axisStart, axisEnd);
            entity.Points = entity.Points
                .Select(point => EntityGeometry.ReflectAcrossLine(point, axisStart, axisEnd))
                .ToList();
        });
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

    private int Transform(DrawingDocument drawing, IEnumerable<Guid> entityIds, string description, Action<DrawingEntity> transform)
    {
        var entities = EditableEntities(drawing, entityIds).ToArray();
        if (entities.Length == 0) return 0;
        var before = entities.Select(Clone).ToArray();
        foreach (var entity in entities)
        {
            RectangleGeometry.Normalize(entity);
            transform(entity);
        }
        var after = entities.Select(Clone).ToArray();
        Push(drawing.Id, new TransformEntitiesEdit(description, before, after));
        return entities.Length;
    }

    private static IEnumerable<DrawingEntity> EditableEntities(DrawingDocument drawing, IEnumerable<Guid> entityIds)
    {
        var requestedIds = entityIds.ToHashSet();
        var lockedLayerIds = drawing.Layers.Where(layer => layer.IsLocked).Select(layer => layer.Id).ToHashSet();
        return drawing.Entities.Where(entity => requestedIds.Contains(entity.Id) && !lockedLayerIds.Contains(entity.LayerId));
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
        Text = source.Text,
        TextHeight = source.TextHeight,
        TextRotationDegrees = source.TextRotationDegrees
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

    private sealed record AddEntitiesEdit(string Action, IReadOnlyList<DrawingEntity> Entities) : IDrawingEdit
    {
        public string Description => $"{Action} {(Entities.Count == 1 ? "entity" : $"{Entities.Count} entities")}";

        public void Undo(DrawingDocument drawing)
        {
            var ids = Entities.Select(entity => entity.Id).ToHashSet();
            drawing.Entities.RemoveAll(entity => ids.Contains(entity.Id));
        }

        public void Redo(DrawingDocument drawing) => drawing.Entities.AddRange(Entities.Select(Clone));
    }

    private sealed record TransformEntitiesEdit(
        string Action,
        IReadOnlyList<DrawingEntity> Before,
        IReadOnlyList<DrawingEntity> After) : IDrawingEdit
    {
        public string Description => $"{Action} {(Before.Count == 1 ? "entity" : $"{Before.Count} entities")}";
        public void Undo(DrawingDocument drawing) => Apply(drawing, Before);
        public void Redo(DrawingDocument drawing) => Apply(drawing, After);

        private static void Apply(DrawingDocument drawing, IEnumerable<DrawingEntity> snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                var entity = drawing.Entities.FirstOrDefault(item => item.Id == snapshot.Id);
                if (entity is null) continue;
                entity.Kind = snapshot.Kind;
                entity.LayerId = snapshot.LayerId;
                entity.Points = [.. snapshot.Points];
                entity.Radius = snapshot.Radius;
                entity.Text = snapshot.Text;
                entity.TextHeight = snapshot.TextHeight;
                entity.TextRotationDegrees = snapshot.TextRotationDegrees;
            }
        }
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
