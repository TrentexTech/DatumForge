using System.Text.Json.Serialization;

namespace DatumForge.Core.Models;

[Flags]
public enum ObjectSnapMode
{
    None = 0,
    Endpoint = 1,
    Midpoint = 2,
    Center = 4,
    Intersection = 8
}

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
    public bool SnapToObjects { get; set; } = true;
    public bool OrthographicTracking { get; set; }
    public ObjectSnapMode ObjectSnaps { get; set; } =
        ObjectSnapMode.Endpoint | ObjectSnapMode.Midpoint | ObjectSnapMode.Center | ObjectSnapMode.Intersection;
    public bool ShowLineWeights { get; set; } = true;
    public double GridSpacing { get; set; } = 25;
    [JsonIgnore]
    public double Zoom { get; set; } = 1;
    [JsonIgnore]
    public CadPoint Pan { get; set; }
    public List<ViewportModuleDefinition> ViewportModules { get; set; } = ViewportModuleDefinition.CreateDefault();

    public void NormalizeViewportModules()
    {
        if (ViewportModules is null)
        {
            ViewportModules = ViewportModuleDefinition.CreateDefault();
            return;
        }

        var seen = new HashSet<ViewportModuleKind>();
        ViewportModules = ViewportModules
            .Where(module => seen.Add(module.Kind))
            .Select(module =>
            {
                module.Size = double.IsFinite(module.Size)
                    ? Math.Clamp(module.Size, module.Kind == ViewportModuleKind.Viewer ? 320 : 160, 1600)
                    : ViewportModuleDefinition.DefaultSize(module.Kind);
                return module;
            })
            .ToList();

    }
}

public enum ViewportModuleKind
{
    Layers,
    Viewer,
    Properties
}

public sealed class ViewportModuleDefinition
{
    public ViewportModuleKind Kind { get; set; }
    public double Size { get; set; }

    public static double DefaultSize(ViewportModuleKind kind) => kind switch
    {
        ViewportModuleKind.Layers => 230,
        ViewportModuleKind.Properties => 270,
        _ => 800
    };

    public static List<ViewportModuleDefinition> CreateDefault() =>
    [
        new() { Kind = ViewportModuleKind.Layers, Size = DefaultSize(ViewportModuleKind.Layers) },
        new() { Kind = ViewportModuleKind.Viewer, Size = DefaultSize(ViewportModuleKind.Viewer) },
        new() { Kind = ViewportModuleKind.Properties, Size = DefaultSize(ViewportModuleKind.Properties) }
    ];
}
