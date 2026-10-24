using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatumForge.Core.Models;
using DatumForge.Core.Services;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ProjectStore _store = new();

    public MainViewModel()
    {
        ProjectTabs.Add(ProjectTabViewModel.Welcome);
        SelectedProjectTab = ProjectTabs[0];
    }

    public ObservableCollection<ProjectTabViewModel> ProjectTabs { get; } = [];
    public ObservableCollection<DrawingTabViewModel> DrawingTabs { get; } = [];
    public ObservableCollection<LayoutDefinition> Layouts { get; } = [];
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];
    public IReadOnlyList<string> Tools { get; } = ["Select", "Line", "Polyline", "Circle", "Rectangle", "Text", "Dimension"];

    [ObservableProperty] public partial ProjectTabViewModel? SelectedProjectTab { get; set; }
    [ObservableProperty] public partial DrawingTabViewModel? SelectedDrawingTab { get; set; }
    [ObservableProperty] public partial LayoutDefinition? SelectedLayout { get; set; }
    [ObservableProperty] public partial string ActiveTool { get; set; } = "Select";
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string? WarningMessage { get; set; }
    [ObservableProperty] public partial string ProjectSummary { get; set; } = "No project open";

    public bool IsWelcomeVisible => SelectedProjectTab?.IsWelcome != false;
    public bool IsWorkspaceVisible => !IsWelcomeVisible;
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);
    public DrawingDocument? ActiveDrawing => SelectedDrawingTab?.Document;
    public string WindowTitle => SelectedProjectTab?.Workspace is { } workspace ? $"{workspace.Project.Name} — DatumForge" : "DatumForge";

    partial void OnSelectedProjectTabChanged(ProjectTabViewModel? value)
    {
        RefreshWorkspace(value?.Workspace);
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsWorkspaceVisible));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedDrawingTabChanged(DrawingTabViewModel? value)
    {
        RefreshLayers(value?.Document);
        if (value?.Document is { } drawing)
        {
            var matchingLayout = Layouts.FirstOrDefault(layout => layout.ActiveDrawingId == drawing.Id);
            if (matchingLayout is not null) SelectedLayout = matchingLayout;
        }
        OnPropertyChanged(nameof(ActiveDrawing));
    }

    partial void OnWarningMessageChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

    public async Task CreateProjectAsync(string directory, string name)
    {
        await OpenWorkspaceAsync(await _store.CreateAsync(directory, name));
        StatusText = $"Created {name}";
    }

    public async Task OpenProjectAsync(string path)
    {
        await OpenWorkspaceAsync(await _store.LoadProjectAsync(path));
        StatusText = $"Opened {Path.GetFileName(path)}";
    }

    public async Task OpenDrawingAsync(string path)
    {
        var drawing = await _store.LoadDrawingAsync(path);
        var activeProjectId = SelectedProjectTab?.Workspace?.Project.Id;
        var assessment = DrawingAssociationInspector.Assess(drawing, activeProjectId);
        WarningMessage = assessment.Message;
        if (SelectedProjectTab?.Workspace is null)
        {
            var looseTab = ProjectTabs.FirstOrDefault(tab => tab.IsLooseDrawings);
            if (looseTab is null)
            {
                looseTab = ProjectTabViewModel.LooseDrawings;
                ProjectTabs.Add(looseTab);
            }
            SelectedProjectTab = looseTab;
            ProjectSummary = "Opened independently from a project";
        }
        var tab = new DrawingTabViewModel(drawing, path, true);
        DrawingTabs.Add(tab);
        SelectedDrawingTab = tab;
        StatusText = assessment.RequiresWarning ? "Drawing opened outside its project" : $"Opened {drawing.Name}";
    }

    public void NotifyDrawingChanged()
    {
        OnPropertyChanged(nameof(SelectedDrawingTab));
        OnPropertyChanged(nameof(ActiveDrawing));
        StatusText = $"{ActiveDrawing?.Entities.Count ?? 0} entities · unsaved changes";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProjectTab?.Workspace is not { } workspace)
        {
            StatusText = "Open a project before saving";
            return;
        }

        await _store.SaveAsync(workspace);
        StatusText = $"Saved revision {workspace.Project.Revision} at {DateTime.Now:t}";
        ProjectSummary = BuildSummary(workspace);
    }

    [RelayCommand]
    private void SelectTool(string? tool)
    {
        if (!string.IsNullOrWhiteSpace(tool)) ActiveTool = tool;
        StatusText = ActiveTool == "Select" ? "Select entities" : $"{ActiveTool}: click in the viewport to draw";
    }

    [RelayCommand]
    private void AddLayout()
    {
        var workspace = SelectedProjectTab?.Workspace;
        if (workspace is null) return;
        var layout = new LayoutDefinition
        {
            Name = $"Layout {workspace.Project.Layouts.Count}",
            ActiveDrawingId = ActiveDrawing?.Id,
            ShowGrid = SelectedLayout?.ShowGrid ?? true,
            GridSpacing = SelectedLayout?.GridSpacing ?? 25
        };
        workspace.Project.Layouts.Add(layout);
        Layouts.Add(layout);
        SelectedLayout = layout;
        StatusText = $"Added {layout.Name}";
    }

    [RelayCommand]
    private void AddLayer()
    {
        if (ActiveDrawing is null) return;
        var layer = new DrawingLayer { Name = $"Layer {ActiveDrawing.Layers.Count}" };
        ActiveDrawing.Layers.Add(layer);
        Layers.Add(new LayerItemViewModel(layer));
        StatusText = $"Added {layer.Name}";
    }

    [RelayCommand]
    private void ToggleGrid()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.ShowGrid = !SelectedLayout.ShowGrid;
        SelectedLayout = SelectedLayout;
        OnPropertyChanged(nameof(SelectedLayout));
    }

    [RelayCommand]
    private void ToggleSnap()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.SnapToGrid = !SelectedLayout.SnapToGrid;
        OnPropertyChanged(nameof(SelectedLayout));
    }

    [RelayCommand]
    private void DismissWarning() => WarningMessage = null;

    private Task OpenWorkspaceAsync(ProjectWorkspace workspace)
    {
        var existing = ProjectTabs.FirstOrDefault(tab => tab.Workspace?.Project.Id == workspace.Project.Id);
        if (existing is not null)
        {
            SelectedProjectTab = existing;
            return Task.CompletedTask;
        }

        var tab = new ProjectTabViewModel(workspace.Project.Name, workspace);
        ProjectTabs.Add(tab);
        SelectedProjectTab = tab;
        return Task.CompletedTask;
    }

    private void RefreshWorkspace(ProjectWorkspace? workspace)
    {
        DrawingTabs.Clear();
        Layouts.Clear();
        Layers.Clear();
        SelectedDrawingTab = null;
        SelectedLayout = null;

        if (workspace is null)
        {
            ProjectSummary = SelectedProjectTab?.IsLooseDrawings == true ? "Opened independently from a project" : "No project open";
            return;
        }

        foreach (var layout in workspace.Project.Layouts) Layouts.Add(layout);
        foreach (var reference in workspace.Project.Drawings)
            if (workspace.Drawings.TryGetValue(reference.DrawingId, out var drawing))
                DrawingTabs.Add(new DrawingTabViewModel(drawing, reference.RelativePath, false));

        SelectedDrawingTab = DrawingTabs.FirstOrDefault();
        SelectedLayout = Layouts.FirstOrDefault();
        ProjectSummary = BuildSummary(workspace);
    }

    private void RefreshLayers(DrawingDocument? drawing)
    {
        Layers.Clear();
        if (drawing is null) return;
        foreach (var layer in drawing.Layers) Layers.Add(new LayerItemViewModel(layer));
    }

    private static string BuildSummary(ProjectWorkspace workspace) =>
        $"{workspace.Project.Drawings.Count} drawing(s) · {workspace.Project.Layouts.Count} layout(s) · revision {workspace.Project.Revision}";
}

public sealed class ProjectTabViewModel
{
    public static ProjectTabViewModel Welcome { get; } = new("Welcome", null, true, false);
    public static ProjectTabViewModel LooseDrawings { get; } = new("Standalone drawings", null, false, true);
    public ProjectTabViewModel(string title, ProjectWorkspace? workspace) : this(title, workspace, false, false) { }
    private ProjectTabViewModel(string title, ProjectWorkspace? workspace, bool isWelcome, bool isLooseDrawings)
    {
        Title = title; Workspace = workspace; IsWelcome = isWelcome; IsLooseDrawings = isLooseDrawings;
    }
    public string Title { get; }
    public ProjectWorkspace? Workspace { get; }
    public bool IsWelcome { get; }
    public bool IsLooseDrawings { get; }
}

public sealed class DrawingTabViewModel
{
    public DrawingTabViewModel(DrawingDocument document, string sourcePath, bool isStandalone)
    {
        Document = document; SourcePath = sourcePath; IsStandalone = isStandalone;
    }
    public DrawingDocument Document { get; }
    public string SourcePath { get; }
    public bool IsStandalone { get; }
    public string Title => IsStandalone ? $"{Document.Name}  ⚠" : Document.Name;
}

public partial class LayerItemViewModel : ViewModelBase
{
    public LayerItemViewModel(DrawingLayer layer) => Layer = layer;
    public DrawingLayer Layer { get; }
    public string Name => Layer.Name;
    public string Color => Layer.Color;
    public string LineType => Layer.LineType.ToString();
    public string LineWeight => $"{Layer.LineWeightMm:0.00} mm";
    public string Opacity => $"{Layer.Opacity:P0}";

    public bool IsVisible { get => Layer.IsVisible; set { if (Layer.IsVisible == value) return; Layer.IsVisible = value; OnPropertyChanged(); } }
    public bool IsLocked { get => Layer.IsLocked; set { if (Layer.IsLocked == value) return; Layer.IsLocked = value; OnPropertyChanged(); } }
}
