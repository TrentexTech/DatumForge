using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DatumForge.Core.Editing;
using DatumForge.Core.Models;
using DatumForge.Core.Services;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ProjectStore _store;
    private readonly UserSettingsStore _userSettingsStore;
    private readonly DrawingEditJournal _editJournal = new();
    private readonly HashSet<Guid> _selectedEntityIds = [];
    private readonly HashSet<Guid> _dirtyProjectIds = [];
    private readonly Dictionary<Guid, DrawingTabViewModel> _dirtyStandaloneDrawings = [];

    public MainViewModel() : this(new ProjectStore(), new UserSettingsStore()) { }

    public MainViewModel(ProjectStore store, UserSettingsStore userSettingsStore)
    {
        _store = store;
        _userSettingsStore = userSettingsStore;
        UserSettings = _userSettingsStore.Load();
        ProjectTabs.Add(ProjectTabViewModel.Welcome);
        SelectedProjectTab = ProjectTabs[0];
        RefreshRecentItems();
        if (!string.IsNullOrWhiteSpace(_userSettingsStore.RecoveryNotice))
            StatusText = _userSettingsStore.RecoveryNotice;
    }

    public UserSettings UserSettings { get; }
    public string UserDataDirectory => _userSettingsStore.DataDirectoryPath;
    public ObservableCollection<ProjectTabViewModel> ProjectTabs { get; } = [];
    public ObservableCollection<DrawingTabViewModel> DrawingTabs { get; } = [];
    public ObservableCollection<LayoutDefinition> Layouts { get; } = [];
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];
    public ObservableCollection<ViewportModuleItemViewModel> ViewportModules { get; } = [];
    public ObservableCollection<RecentFileEntry> RecentProjects { get; } = [];
    public ObservableCollection<RecentFileEntry> RecentDrawings { get; } = [];
    public IReadOnlyList<string> DrawingTools { get; } = ["Select", "Line", "Polyline", "Circle", "Rectangle", "Text", "Dimension"];
    public IReadOnlyList<string> ModifyTools { get; } = ["Move", "Copy", "Rotate", "Scale"];

    [ObservableProperty] public partial ProjectTabViewModel? SelectedProjectTab { get; set; }
    [ObservableProperty] public partial DrawingTabViewModel? SelectedDrawingTab { get; set; }
    [ObservableProperty] public partial LayoutDefinition? SelectedLayout { get; set; }
    [ObservableProperty] public partial string ActiveTool { get; set; } = "Select";
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string? WarningMessage { get; set; }
    [ObservableProperty] public partial string ProjectSummary { get; set; } = "No project open";
    [ObservableProperty] public partial int EntityCount { get; set; }
    [ObservableProperty] public partial int SelectedEntityCount { get; set; }
    [ObservableProperty] public partial int ViewportRevision { get; set; }
    [ObservableProperty] public partial string CoordinateInputText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SelectedRibbonTab { get; set; } = "File";

    public bool IsWelcomeVisible => SelectedProjectTab?.IsWelcome != false;
    public bool IsWorkspaceVisible => !IsWelcomeVisible;
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningMessage);
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasRecentDrawings => RecentDrawings.Count > 0;
    public bool HasNoRecentItems => !HasRecentProjects && !HasRecentDrawings;
    public bool HasUnsavedWork => _dirtyProjectIds.Count > 0 || _dirtyStandaloneDrawings.Count > 0;
    public bool CanAddLayersModule => CanAddViewportModule(ViewportModuleKind.Layers);
    public bool CanAddViewerModule => CanAddViewportModule(ViewportModuleKind.Viewer);
    public bool CanAddPropertiesModule => CanAddViewportModule(ViewportModuleKind.Properties);
    public bool IsFileRibbonTab => SelectedRibbonTab == "File";
    public bool IsInsertRibbonTab => SelectedRibbonTab == "Insert";
    public bool IsModifyRibbonTab => SelectedRibbonTab == "Modify";
    public bool IsViewRibbonTab => SelectedRibbonTab == "View";
    public string UnsavedWorkSummary
    {
        get
        {
            var names = ProjectTabs
                .Where(tab => tab.Workspace is { } workspace && _dirtyProjectIds.Contains(workspace.Project.Id))
                .Select(tab => tab.Title)
                .Concat(_dirtyStandaloneDrawings.Values.Select(tab => tab.Document.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return names.Length == 1
                ? $"{names[0]} has unsaved changes."
                : $"{names.Length} items have unsaved changes: {string.Join(", ", names)}.";
        }
    }
    public DrawingDocument? ActiveDrawing => SelectedDrawingTab?.Document;
    public string WindowTitle => SelectedProjectTab?.Workspace is { } workspace ? $"{workspace.Project.Name} — DatumForge" : "DatumForge";
    public string GridStatus => SelectedLayout?.ShowGrid == true ? "Grid on" : "Grid off";
    public string OrthoStatus => SelectedLayout?.OrthographicTracking == true ? "Ortho on" : "Ortho off";
    public string PolarStatus => SelectedLayout?.PolarTracking == true ? "Polar on" : "Polar off";
    public string SnapStatus => SelectedLayout?.SnapToGrid == true ? "Grid snap on" : "Grid snap off";
    public string ObjectSnapStatus => SelectedLayout?.SnapToObjects == true ? "Object snap on" : "Object snap off";
    public string ToolStatus => $"TOOL: {ActiveTool.ToUpperInvariant()}";
    public string SaveToolTip => BuildShortcutToolTip("Save", KeyBindingDefaults.Save);
    public string UndoToolTip => BuildShortcutToolTip("Undo", KeyBindingDefaults.Undo);
    public string RedoToolTip => BuildShortcutToolTip("Redo", KeyBindingDefaults.Redo);
    public string EraseToolTip => BuildShortcutToolTip("Erase selected entities", KeyBindingDefaults.Erase);
    public bool SnapEndpointEnabled { get => HasSnapMode(ObjectSnapMode.Endpoint); set => SetSnapMode(ObjectSnapMode.Endpoint, value); }
    public bool SnapMidpointEnabled { get => HasSnapMode(ObjectSnapMode.Midpoint); set => SetSnapMode(ObjectSnapMode.Midpoint, value); }
    public bool SnapCenterEnabled { get => HasSnapMode(ObjectSnapMode.Center); set => SetSnapMode(ObjectSnapMode.Center, value); }
    public bool SnapIntersectionEnabled { get => HasSnapMode(ObjectSnapMode.Intersection); set => SetSnapMode(ObjectSnapMode.Intersection, value); }

    partial void OnSelectedProjectTabChanged(ProjectTabViewModel? value)
    {
        RefreshWorkspace(value?.Workspace);
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsWorkspaceVisible));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedDrawingTabChanged(DrawingTabViewModel? value)
    {
        ResetSelection();
        EntityCount = value?.Document.Entities.Count ?? 0;
        RefreshLayers(value?.Document);
        if (value?.Document is { } drawing)
        {
            var matchingLayout = Layouts.FirstOrDefault(layout => layout.ActiveDrawingId == drawing.Id);
            if (matchingLayout is null && value.IsStandalone)
            {
                matchingLayout = new LayoutDefinition { Name = drawing.Name, ActiveDrawingId = drawing.Id };
                Layouts.Add(matchingLayout);
            }
            if (matchingLayout is not null) SelectedLayout = matchingLayout;
        }
        OnPropertyChanged(nameof(ActiveDrawing));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnWarningMessageChanged(string? value) => OnPropertyChanged(nameof(HasWarning));

    partial void OnActiveToolChanged(string value) => OnPropertyChanged(nameof(ToolStatus));

    partial void OnSelectedRibbonTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsFileRibbonTab));
        OnPropertyChanged(nameof(IsInsertRibbonTab));
        OnPropertyChanged(nameof(IsModifyRibbonTab));
        OnPropertyChanged(nameof(IsViewRibbonTab));
    }

    partial void OnSelectedLayoutChanged(LayoutDefinition? value)
    {
        RefreshViewportModules();
        RefreshLayoutState();
    }

    public async Task CreateProjectAsync(string directory, string name)
    {
        var workspace = await _store.CreateAsync(directory, name);
        await OpenWorkspaceAsync(workspace);
        await RememberProjectAsync(workspace.ProjectFilePath, workspace.Project.Name);
        StatusText = $"Created {name}";
    }

    public async Task OpenProjectAsync(string path)
    {
        var workspace = await _store.LoadProjectAsync(path);
        await OpenWorkspaceAsync(workspace);
        await RememberProjectAsync(workspace.ProjectFilePath, workspace.Project.Name);
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
        await RememberDrawingAsync(path, drawing.Name);
        StatusText = assessment.RequiresWarning ? "Drawing opened outside its project" : $"Opened {drawing.Name}";
    }

    public Task SaveUserSettingsAsync() => _userSettingsStore.SaveAsync(UserSettings);

    public void SaveUserSettings() => _userSettingsStore.Save(UserSettings);

    public async Task UpdateKeyBindingsAsync(IReadOnlyDictionary<string, string> keyBindings)
    {
        UserSettings.KeyBindings = new Dictionary<string, string>(keyBindings, StringComparer.OrdinalIgnoreCase);
        UserSettings.Normalize();
        await SaveUserSettingsAsync();
        OnPropertyChanged(nameof(SaveToolTip));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
        OnPropertyChanged(nameof(EraseToolTip));
    }

    public async Task<bool> SaveAllUnsavedWorkAsync()
    {
        try
        {
            foreach (var tab in ProjectTabs.Where(tab => tab.Workspace is { } workspace && _dirtyProjectIds.Contains(workspace.Project.Id)).ToArray())
            {
                await _store.SaveAsync(tab.Workspace!);
                _dirtyProjectIds.Remove(tab.Workspace!.Project.Id);
            }

            foreach (var tab in _dirtyStandaloneDrawings.Values.ToArray())
            {
                await _store.SaveDrawingAsync(tab.SourcePath, tab.Document);
                _dirtyStandaloneDrawings.Remove(tab.Document.Id);
            }

            NotifyUnsavedWorkChanged();
            StatusText = $"Saved all work at {DateTime.Now:t}";
            return true;
        }
        catch (Exception exception)
        {
            NotifyUnsavedWorkChanged();
            StatusText = $"Could not save all work: {exception.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(RecentFileEntry? entry)
    {
        if (entry is null) return;
        if (!File.Exists(entry.Path))
        {
            UserSettings.ForgetProject(entry.Path);
            RefreshRecentItems();
            await SaveUserSettingsAsync();
            StatusText = $"Removed missing project: {entry.DisplayName}";
            return;
        }

        try { await OpenProjectAsync(entry.Path); }
        catch (Exception exception) { StatusText = $"Could not open recent project: {exception.Message}"; }
    }

    [RelayCommand]
    private async Task OpenRecentDrawingAsync(RecentFileEntry? entry)
    {
        if (entry is null) return;
        if (!File.Exists(entry.Path))
        {
            UserSettings.ForgetDrawing(entry.Path);
            RefreshRecentItems();
            await SaveUserSettingsAsync();
            StatusText = $"Removed missing drawing: {entry.DisplayName}";
            return;
        }

        try { await OpenDrawingAsync(entry.Path); }
        catch (Exception exception) { StatusText = $"Could not open recent drawing: {exception.Message}"; }
    }

    public void RecordEntityCreated(DrawingEntity entity)
    {
        if (ActiveDrawing is null) return;
        _editJournal.RecordEntityAdded(ActiveDrawing, entity);
        NotifyDrawingChanged($"Created {entity.Kind}");
    }

    [RelayCommand]
    private void SelectRibbonTab(string? tab)
    {
        if (tab is "File" or "Insert" or "Modify" or "View") SelectedRibbonTab = tab;
    }

    public void UpdateSelection(IEnumerable<Guid> entityIds)
    {
        _selectedEntityIds.Clear();
        _selectedEntityIds.UnionWith(entityIds);
        SelectedEntityCount = _selectedEntityIds.Count;
        EraseCommand.NotifyCanExecuteChanged();
        StatusText = SelectedEntityCount == 0 ? "No selection" : $"{SelectedEntityCount} selected";
    }

    public void ApplyTransform(EntityTransformKind kind, IEnumerable<Guid> entityIds, CadPoint reference, CadPoint target)
    {
        if (ActiveDrawing is null) return;
        var count = kind switch
        {
            EntityTransformKind.Move => _editJournal.Move(ActiveDrawing, entityIds, reference, target),
            EntityTransformKind.Copy => _editJournal.Copy(ActiveDrawing, entityIds, reference, target),
            EntityTransformKind.Rotate => _editJournal.Rotate(ActiveDrawing, entityIds, reference, target),
            EntityTransformKind.Scale => _editJournal.Scale(ActiveDrawing, entityIds, reference, target),
            _ => 0
        };
        if (count == 0)
        {
            StatusText = "Transform needs editable entities and two distinct reference points";
            return;
        }

        ActiveTool = "Select";
        ResetSelection();
        NotifyDrawingChanged($"{PastTense(kind)} {count} {(count == 1 ? "entity" : "entities")}");
    }

    public void ReportCoordinateSubmission(bool success, bool completed, string message)
    {
        if (success) CoordinateInputText = string.Empty;
        if (!completed) StatusText = message;
    }

    public void NotifyViewportChanged()
    {
        if (SelectedLayout is null) return;
        if (SelectedProjectTab?.Workspace?.Project is { } project)
            UserSettings.RememberProjectView(project.Id, SelectedLayout.Id, SelectedLayout.Zoom, SelectedLayout.Pan);
        OnPropertyChanged(nameof(SelectedLayout));
        StatusText = $"View updated · {SelectedLayout.Zoom:P0} zoom";
    }

    public bool TryExecuteTextCommand(string? input)
    {
        var command = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(command)) return false;
        var tool = command switch
        {
            "select" or "sel" => "Select",
            "line" or "l" => "Line",
            "polyline" or "pl" => "Polyline",
            "circle" or "c" => "Circle",
            "rectangle" or "rect" or "rec" => "Rectangle",
            "text" or "t" => "Text",
            "dimension" or "dim" => "Dimension",
            "move" or "m" => "Move",
            "copy" or "co" => "Copy",
            "rotate" or "ro" => "Rotate",
            "scale" or "sc" => "Scale",
            _ => null
        };
        if (tool is not null)
        {
            SelectTool(tool);
            CoordinateInputText = string.Empty;
            return true;
        }

        switch (command)
        {
            case "undo" or "u":
                if (UndoCommand.CanExecute(null)) UndoCommand.Execute(null);
                else StatusText = "Nothing to undo";
                break;
            case "redo":
                if (RedoCommand.CanExecute(null)) RedoCommand.Execute(null);
                else StatusText = "Nothing to redo";
                break;
            case "erase" or "delete" or "e":
                if (EraseCommand.CanExecute(null)) EraseCommand.Execute(null);
                else StatusText = "Select editable entities before erasing";
                break;
            case "grid":
                ToggleGrid();
                StatusText = GridStatus;
                break;
            case "snap":
                ToggleSnap();
                StatusText = SnapStatus;
                break;
            case "ortho":
                ToggleOrtho();
                StatusText = OrthoStatus;
                break;
            case "polar":
                TogglePolar();
                StatusText = PolarStatus;
                break;
            case "osnap":
                ToggleObjectSnap();
                StatusText = ObjectSnapStatus;
                break;
            default:
                return false;
        }

        CoordinateInputText = string.Empty;
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedDrawingTab is { IsStandalone: true } standaloneTab)
        {
            await _store.SaveDrawingAsync(standaloneTab.SourcePath, standaloneTab.Document);
            _dirtyStandaloneDrawings.Remove(standaloneTab.Document.Id);
            NotifyUnsavedWorkChanged();
            StatusText = $"Saved {standaloneTab.Document.Name} revision {standaloneTab.Document.Revision} at {DateTime.Now:t}";
            return;
        }

        if (SelectedProjectTab?.Workspace is not { } workspace)
        {
            StatusText = "Open a project before saving";
            return;
        }

        await _store.SaveAsync(workspace);
        _dirtyProjectIds.Remove(workspace.Project.Id);
        NotifyUnsavedWorkChanged();
        StatusText = $"Saved revision {workspace.Project.Revision} at {DateTime.Now:t}";
        ProjectSummary = BuildSummary(workspace);
    }

    [RelayCommand]
    private async Task CloseProjectTabAsync(ProjectTabViewModel? tab)
    {
        if (tab?.Workspace is not { } workspace || !ProjectTabs.Contains(tab)) return;

        try
        {
            await _store.SaveAsync(workspace);
            _dirtyProjectIds.Remove(workspace.Project.Id);
            NotifyUnsavedWorkChanged();
            foreach (var drawing in workspace.Drawings.Values)
                _editJournal.Clear(drawing.Id);

            var wasSelected = ReferenceEquals(SelectedProjectTab, tab);
            var tabIndex = ProjectTabs.IndexOf(tab);
            ProjectTabs.Remove(tab);
            if (wasSelected)
                SelectedProjectTab = ProjectTabs[Math.Max(0, tabIndex - 1)];
            StatusText = $"Saved and closed {tab.Title}";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not close {tab.Title}: {exception.Message}";
        }
    }

    [RelayCommand]
    private void SelectTool(string? tool)
    {
        if (string.IsNullOrWhiteSpace(tool)) return;
        if (Enum.TryParse<EntityTransformKind>(tool, out _) && _selectedEntityIds.Count == 0)
        {
            ActiveTool = "Select";
            StatusText = $"Select one or more editable entities before choosing {tool}";
            return;
        }

        ActiveTool = tool;
        StatusText = ActiveTool switch
        {
            "Select" => "Select entities",
            "Move" or "Copy" => $"{ActiveTool}: choose a base point, then a target point",
            "Rotate" or "Scale" => $"{ActiveTool}: choose reference and target points around the selection center",
            _ => $"{ActiveTool}: click in the viewport to draw"
        };
    }

    [RelayCommand(CanExecute = nameof(CanErase))]
    private void Erase()
    {
        if (ActiveDrawing is null) return;
        var requestedCount = _selectedEntityIds.Count;
        var erasedCount = _editJournal.Erase(ActiveDrawing, _selectedEntityIds);
        ResetSelection();
        if (erasedCount == 0 && requestedCount > 0)
        {
            StatusText = "Selection is on a locked layer";
            return;
        }
        NotifyDrawingChanged($"Erased {erasedCount} {(erasedCount == 1 ? "entity" : "entities")}");
    }

    private bool CanErase() => ActiveDrawing is not null && _selectedEntityIds.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (ActiveDrawing is null) return;
        var description = _editJournal.Undo(ActiveDrawing);
        if (description is null) return;
        ResetSelection();
        NotifyDrawingChanged($"Undid: {description}");
    }

    private bool CanUndo() => ActiveDrawing is not null && _editJournal.CanUndo(ActiveDrawing.Id);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (ActiveDrawing is null) return;
        var description = _editJournal.Redo(ActiveDrawing);
        if (description is null) return;
        ResetSelection();
        NotifyDrawingChanged($"Redid: {description}");
    }

    private bool CanRedo() => ActiveDrawing is not null && _editJournal.CanRedo(ActiveDrawing.Id);

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
            SnapToGrid = SelectedLayout?.SnapToGrid ?? true,
            SnapToObjects = SelectedLayout?.SnapToObjects ?? true,
            ObjectSnaps = SelectedLayout?.ObjectSnaps ??
                (ObjectSnapMode.Endpoint | ObjectSnapMode.Midpoint | ObjectSnapMode.Center | ObjectSnapMode.Intersection),
            GridSpacing = SelectedLayout?.GridSpacing ?? 25,
            ViewportModules = SelectedLayout?.ViewportModules
                .Select(module => new ViewportModuleDefinition { Kind = module.Kind, Size = module.Size })
                .ToList() ?? ViewportModuleDefinition.CreateDefault()
        };
        workspace.Project.Layouts.Add(layout);
        Layouts.Add(layout);
        SelectedLayout = layout;
        MarkActiveWorkDirty();
        ProjectSummary = BuildSummary(workspace);
        StatusText = $"Added {layout.Name}";
    }

    [RelayCommand]
    private void AddLayer()
    {
        if (ActiveDrawing is null) return;
        var layer = new DrawingLayer { Name = $"Layer {ActiveDrawing.Layers.Count}" };
        ActiveDrawing.Layers.Add(layer);
        Layers.Add(new LayerItemViewModel(layer, OnLayerChanged));
        MarkActiveWorkDirty();
        StatusText = $"Added {layer.Name}";
    }

    [RelayCommand]
    private void AddViewportModule(string? kindName)
    {
        if (SelectedLayout is null || !Enum.TryParse<ViewportModuleKind>(kindName, true, out var kind) || !CanAddViewportModule(kind))
            return;

        var definition = new ViewportModuleDefinition { Kind = kind, Size = ViewportModuleDefinition.DefaultSize(kind) };
        SelectedLayout.ViewportModules.Add(definition);
        ViewportModules.Add(new ViewportModuleItemViewModel(this, definition));
        UpdateViewportModulePositions();
        NotifyViewportModuleAvailabilityChanged();
        MarkActiveWorkDirty();
        StatusText = $"Added {kind} module to {SelectedLayout.Name}";
    }

    [RelayCommand]
    private void RemoveViewportModule(ViewportModuleItemViewModel? module)
    {
        if (SelectedLayout is null || module is null || !ViewportModules.Remove(module)) return;
        SelectedLayout.ViewportModules.Remove(module.Definition);
        UpdateViewportModulePositions();
        NotifyViewportModuleAvailabilityChanged();
        MarkActiveWorkDirty();
        StatusText = $"Removed {module.Title} module from {SelectedLayout.Name}";
    }

    [RelayCommand]
    private void MoveViewportModuleLeft(ViewportModuleItemViewModel? module) => MoveViewportModule(module, -1);

    [RelayCommand]
    private void MoveViewportModuleRight(ViewportModuleItemViewModel? module) => MoveViewportModule(module, 1);

    public void PersistViewportModuleSizes(IEnumerable<(ViewportModuleItemViewModel Module, double Size)> sizes)
    {
        if (SelectedLayout is null) return;
        var changed = false;
        foreach (var (module, size) in sizes)
        {
            var minimum = module.IsViewer ? 320 : 160;
            var normalized = Math.Clamp(size, minimum, 1600);
            if (Math.Abs(module.Definition.Size - normalized) < 0.5) continue;
            module.Definition.Size = normalized;
            changed = true;
        }

        if (!changed) return;
        MarkActiveWorkDirty();
        StatusText = $"Resized modules in {SelectedLayout.Name}";
    }

    [RelayCommand]
    private void ToggleGrid()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.ShowGrid = !SelectedLayout.ShowGrid;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    [RelayCommand]
    private void ToggleOrtho()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.OrthographicTracking = !SelectedLayout.OrthographicTracking;
        if (SelectedLayout.OrthographicTracking) SelectedLayout.PolarTracking = false;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    [RelayCommand]
    private void TogglePolar()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.PolarTracking = !SelectedLayout.PolarTracking;
        if (SelectedLayout.PolarTracking) SelectedLayout.OrthographicTracking = false;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    [RelayCommand]
    private void ToggleSnap()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.SnapToGrid = !SelectedLayout.SnapToGrid;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    [RelayCommand]
    private void ToggleObjectSnap()
    {
        if (SelectedLayout is null) return;
        SelectedLayout.SnapToObjects = !SelectedLayout.SnapToObjects;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    [RelayCommand]
    private void DismissWarning() => WarningMessage = null;

    private void NotifyDrawingChanged(string status)
    {
        MarkActiveWorkDirty();
        ViewportRevision++;
        EntityCount = ActiveDrawing?.Entities.Count ?? 0;
        OnPropertyChanged(nameof(SelectedDrawingTab));
        OnPropertyChanged(nameof(ActiveDrawing));
        StatusText = $"{status} · {ActiveDrawing?.Entities.Count ?? 0} entities · unsaved";
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        EraseCommand.NotifyCanExecuteChanged();
    }

    private void ResetSelection()
    {
        _selectedEntityIds.Clear();
        SelectedEntityCount = 0;
        EraseCommand.NotifyCanExecuteChanged();
    }

    private void RefreshLayoutState()
    {
        ViewportRevision++;
        OnPropertyChanged(nameof(GridStatus));
        OnPropertyChanged(nameof(OrthoStatus));
        OnPropertyChanged(nameof(PolarStatus));
        OnPropertyChanged(nameof(SnapStatus));
        OnPropertyChanged(nameof(ObjectSnapStatus));
        OnPropertyChanged(nameof(SnapEndpointEnabled));
        OnPropertyChanged(nameof(SnapMidpointEnabled));
        OnPropertyChanged(nameof(SnapCenterEnabled));
        OnPropertyChanged(nameof(SnapIntersectionEnabled));
        OnPropertyChanged(nameof(SelectedLayout));
    }

    private bool HasSnapMode(ObjectSnapMode mode) => SelectedLayout?.ObjectSnaps.HasFlag(mode) == true;

    private void SetSnapMode(ObjectSnapMode mode, bool enabled)
    {
        if (SelectedLayout is null || HasSnapMode(mode) == enabled) return;
        if (enabled) SelectedLayout.ObjectSnaps |= mode;
        else SelectedLayout.ObjectSnaps &= ~mode;
        MarkActiveWorkDirty();
        RefreshLayoutState();
    }

    private static string PastTense(EntityTransformKind kind) => kind switch
    {
        EntityTransformKind.Move => "Moved",
        EntityTransformKind.Copy => "Copied",
        EntityTransformKind.Rotate => "Rotated",
        EntityTransformKind.Scale => "Scaled",
        _ => "Transformed"
    };

    private Task OpenWorkspaceAsync(ProjectWorkspace workspace)
    {
        var existing = ProjectTabs.FirstOrDefault(tab => tab.Workspace?.Project.Id == workspace.Project.Id);
        if (existing is not null)
        {
            SelectedProjectTab = existing;
            return Task.CompletedTask;
        }

        RestoreProjectViews(workspace.Project);

        var tab = new ProjectTabViewModel(workspace.Project.Name, workspace);
        ProjectTabs.Add(tab);
        SelectedProjectTab = tab;
        return Task.CompletedTask;
    }

    private void RestoreProjectViews(ProjectDocument project)
    {
        foreach (var layout in project.Layouts)
        {
            if (!UserSettings.TryGetProjectView(project.Id, layout.Id, out var view)) continue;
            layout.Zoom = view.Zoom;
            layout.Pan = view.Pan;
        }
    }

    private void RefreshWorkspace(ProjectWorkspace? workspace)
    {
        DrawingTabs.Clear();
        Layouts.Clear();
        Layers.Clear();
        ViewportModules.Clear();
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
        foreach (var layer in drawing.Layers) Layers.Add(new LayerItemViewModel(layer, OnLayerChanged));
    }

    private void RefreshViewportModules()
    {
        ViewportModules.Clear();
        if (SelectedLayout is not { } layout)
        {
            NotifyViewportModuleAvailabilityChanged();
            return;
        }

        layout.NormalizeViewportModules();
        foreach (var definition in layout.ViewportModules)
            ViewportModules.Add(new ViewportModuleItemViewModel(this, definition));
        UpdateViewportModulePositions();
        NotifyViewportModuleAvailabilityChanged();
    }

    private bool CanAddViewportModule(ViewportModuleKind kind) =>
        SelectedLayout is not null && ViewportModules.All(module => module.Definition.Kind != kind);

    private void MoveViewportModule(ViewportModuleItemViewModel? module, int offset)
    {
        if (SelectedLayout is null || module is null) return;
        var currentIndex = ViewportModules.IndexOf(module);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ViewportModules.Count) return;

        ViewportModules.Move(currentIndex, targetIndex);
        SelectedLayout.ViewportModules.RemoveAt(currentIndex);
        SelectedLayout.ViewportModules.Insert(targetIndex, module.Definition);
        UpdateViewportModulePositions();
        MarkActiveWorkDirty();
        StatusText = $"Moved {module.Title} module in {SelectedLayout.Name}";
    }

    private void UpdateViewportModulePositions()
    {
        for (var index = 0; index < ViewportModules.Count; index++)
            ViewportModules[index].UpdatePosition(index, ViewportModules.Count);
    }

    private void NotifyViewportModuleAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanAddLayersModule));
        OnPropertyChanged(nameof(CanAddViewerModule));
        OnPropertyChanged(nameof(CanAddPropertiesModule));
    }

    private void OnLayerChanged()
    {
        MarkActiveWorkDirty();
        ViewportRevision++;
    }

    private void MarkActiveWorkDirty()
    {
        if (SelectedDrawingTab is { IsStandalone: true } standaloneTab)
            _dirtyStandaloneDrawings[standaloneTab.Document.Id] = standaloneTab;
        else if (SelectedProjectTab?.Workspace is { } workspace)
            _dirtyProjectIds.Add(workspace.Project.Id);
        else
            return;

        NotifyUnsavedWorkChanged();
    }

    private void NotifyUnsavedWorkChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedWork));
        OnPropertyChanged(nameof(UnsavedWorkSummary));
    }

    private async Task RememberProjectAsync(string path, string displayName)
    {
        UserSettings.RememberProject(path, displayName);
        RefreshRecentItems();
        await SaveUserSettingsAsync();
    }

    private async Task RememberDrawingAsync(string path, string displayName)
    {
        UserSettings.RememberDrawing(path, displayName);
        RefreshRecentItems();
        await SaveUserSettingsAsync();
    }

    private void RefreshRecentItems()
    {
        RecentProjects.Clear();
        foreach (var entry in UserSettings.RecentProjects) RecentProjects.Add(entry);
        RecentDrawings.Clear();
        foreach (var entry in UserSettings.RecentDrawings) RecentDrawings.Add(entry);
        OnPropertyChanged(nameof(HasRecentProjects));
        OnPropertyChanged(nameof(HasRecentDrawings));
        OnPropertyChanged(nameof(HasNoRecentItems));
    }

    private static string BuildSummary(ProjectWorkspace workspace) =>
        $"{workspace.Project.Drawings.Count} drawing(s) · {workspace.Project.Layouts.Count} layout(s) · revision {workspace.Project.Revision}";

    private string BuildShortcutToolTip(string label, string actionId)
    {
        var gesture = UserSettings.KeyBindings.GetValueOrDefault(actionId, string.Empty);
        return string.IsNullOrWhiteSpace(gesture) ? label : $"{label} ({gesture})";
    }
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
    public bool CanClose => Workspace is not null;
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
    private readonly Action? _onChanged;

    public LayerItemViewModel(DrawingLayer layer, Action? onChanged = null)
    {
        Layer = layer;
        _onChanged = onChanged;
    }
    public DrawingLayer Layer { get; }
    public string Name => Layer.Name;
    public string Color => Layer.Color;
    public string LineType => Layer.LineType.ToString();
    public string LineWeight => $"{Layer.LineWeightMm:0.00} mm";
    public string Opacity => $"{Layer.Opacity:P0}";

    public bool IsVisible { get => Layer.IsVisible; set { if (Layer.IsVisible == value) return; Layer.IsVisible = value; OnPropertyChanged(); _onChanged?.Invoke(); } }
    public bool IsLocked { get => Layer.IsLocked; set { if (Layer.IsLocked == value) return; Layer.IsLocked = value; OnPropertyChanged(); _onChanged?.Invoke(); } }
}

public sealed class ViewportModuleItemViewModel : ViewModelBase
{
    public ViewportModuleItemViewModel(MainViewModel owner, ViewportModuleDefinition definition)
    {
        Owner = owner;
        Definition = definition;
    }

    public MainViewModel Owner { get; }
    public ViewportModuleDefinition Definition { get; }
    public string Title => Definition.Kind.ToString().ToUpperInvariant();
    public bool IsLayers => Definition.Kind == ViewportModuleKind.Layers;
    public bool IsViewer => Definition.Kind == ViewportModuleKind.Viewer;
    public bool IsProperties => Definition.Kind == ViewportModuleKind.Properties;
    public bool CanMoveLeft { get; private set; }
    public bool CanMoveRight { get; private set; }

    public void UpdatePosition(int index, int count)
    {
        CanMoveLeft = index > 0;
        CanMoveRight = index < count - 1;
        OnPropertyChanged(nameof(CanMoveLeft));
        OnPropertyChanged(nameof(CanMoveRight));
    }
}
