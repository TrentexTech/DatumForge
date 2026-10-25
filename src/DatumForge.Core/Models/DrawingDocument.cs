namespace DatumForge.Core.Models;

public enum DrawingUnits { Millimeters, Centimeters, Meters, Inches, Feet }

public sealed class DrawingDocument
{
    public const int CurrentFormatVersion = 4;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProjectId { get; set; }
    public string Name { get; set; } = "Drawing 1";
    public DrawingUnits Units { get; set; } = DrawingUnits.Millimeters;
    public List<DrawingLayer> Layers { get; set; } = [];
    public List<DrawingEntity> Entities { get; set; } = [];
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
