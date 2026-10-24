namespace DatumForge.Core.Models;

public enum EntityKind { Line, Polyline, Circle, Arc, Rectangle, Text, Dimension }

public sealed class DrawingEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntityKind Kind { get; set; }
    public Guid LayerId { get; set; }
    public List<CadPoint> Points { get; set; } = [];
    public double Radius { get; set; }
    public string? Text { get; set; }
}
