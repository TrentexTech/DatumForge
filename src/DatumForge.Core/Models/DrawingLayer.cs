namespace DatumForge.Core.Models;

public enum CadLineType { Continuous, Dashed, Dotted, DashDot, Center }

public sealed class DrawingLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer 0";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public bool IsPrintable { get; set; } = true;
    public string Color { get; set; } = "#E8EDF2";
    public CadLineType LineType { get; set; } = CadLineType.Continuous;
    public double LineWeightMm { get; set; } = 0.25;
    public double Opacity { get; set; } = 1;
    public string? Group { get; set; }
}
