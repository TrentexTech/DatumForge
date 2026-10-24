namespace DatumForge.Core.Models;

public sealed class ProjectDocument
{
    public const int CurrentFormatVersion = 1;
    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Project";
    public ProjectSettings Settings { get; set; } = new();
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LayoutDefinition> Layouts { get; set; } = [];
    public List<DrawingReference> Drawings { get; set; } = [];
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectSettings
{
    public string Author { get; set; } = Environment.UserName;
    public DrawingUnits DefaultUnits { get; set; } = DrawingUnits.Millimeters;
    public bool CreateHistoryOnSave { get; set; } = true;
    public int HistoryLimit { get; set; } = 100;
}

public sealed class DrawingReference
{
    public Guid DrawingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
}

public sealed class LayoutDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Model";
    public Guid? ActiveDrawingId { get; set; }
    public string BackgroundColor { get; set; } = "#101419";
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public bool ShowLineWeights { get; set; } = true;
    public double GridSpacing { get; set; } = 25;
    public double Zoom { get; set; } = 1;
    public CadPoint Pan { get; set; }
}
