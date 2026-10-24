using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DatumForge.App.ViewModels;
using DatumForge.App.Controls;
using DatumForge.Infrastructure.Persistence;

namespace DatumForge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => RestoreWindowSettings();
        SizeChanged += (_, _) => CaptureNormalWindowSize();
        Closing += (_, _) => SaveWindowSettings();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void Viewport_OnEntityCreated(object? sender, EntityCreatedEventArgs e) => ViewModel.RecordEntityCreated(e.Entity);
    private void Viewport_OnSelectionChanged(object? sender, EntitySelectionChangedEventArgs e) => ViewModel.UpdateSelection(e.EntityIds);
    private void Viewport_OnDeleteRequested(object? sender, EventArgs e) => ViewModel.EraseCommand.Execute(null);
    private void Viewport_OnUndoRequested(object? sender, EventArgs e) => ViewModel.UndoCommand.Execute(null);
    private void Viewport_OnRedoRequested(object? sender, EventArgs e) => ViewModel.RedoCommand.Execute(null);
    private void Viewport_OnTransformRequested(object? sender, EntityTransformRequestedEventArgs e) =>
        ViewModel.ApplyTransform(e.Kind, e.EntityIds, e.Reference, e.Target);

    private void Minimize_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestore_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void RestoreWindowSettings()
    {
        if (DataContext is not MainViewModel viewModel) return;
        var settings = viewModel.UserSettings.Window;
        if (double.IsFinite(settings.Width)) Width = Math.Clamp(settings.Width, MinWidth, 7680);
        if (double.IsFinite(settings.Height)) Height = Math.Clamp(settings.Height, MinHeight, 4320);
        if (settings.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void CaptureNormalWindowSize()
    {
        if (DataContext is not MainViewModel viewModel || WindowState != WindowState.Normal) return;
        viewModel.UserSettings.Window.Width = Math.Max(MinWidth, ClientSize.Width);
        viewModel.UserSettings.Window.Height = Math.Max(MinHeight, ClientSize.Height);
    }

    private void SaveWindowSettings()
    {
        if (DataContext is not MainViewModel viewModel) return;
        CaptureNormalWindowSize();
        viewModel.UserSettings.Window.IsMaximized = WindowState == WindowState.Maximized;
        try { viewModel.SaveUserSettings(); }
        catch (Exception exception) { viewModel.StatusText = $"Could not save user settings: {exception.Message}"; }
    }

    private void CoordinateInput_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SubmitCoordinate();
        e.Handled = true;
    }

    private void CoordinateSubmit_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SubmitCoordinate();

    private void SubmitCoordinate()
    {
        if (ViewModel.TryExecuteTextCommand(ViewModel.CoordinateInputText))
        {
            CoordinateInput.Focus();
            return;
        }
        var result = DrawingSurface.SubmitCoordinate(ViewModel.CoordinateInputText);
        ViewModel.ReportCoordinateSubmission(result.Success, result.Completed, result.Message);
        if (result.Success) CoordinateInput.Focus();
    }

    private async void NewProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a parent folder for the new DatumForge project",
            AllowMultiple = false
        });
        var parent = folders.FirstOrDefault()?.TryGetLocalPath();
        if (parent is null) return;

        const string baseName = "DatumForge Project";
        var directory = Path.Combine(parent, baseName);
        var suffix = 2;
        while (Directory.Exists(directory)) directory = Path.Combine(parent, $"{baseName} {suffix++}");

        try
        {
            await ViewModel.CreateProjectAsync(directory, Path.GetFileName(directory));
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = $"Could not create project: {exception.Message}";
        }
    }

    private async void OpenProject_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open DatumForge project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("DatumForge project") { Patterns = [$"*{ProjectStore.ProjectExtension}"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try { await ViewModel.OpenProjectAsync(path); }
        catch (Exception exception) { ViewModel.StatusText = $"Could not open project: {exception.Message}"; }
    }

    private async void OpenDrawing_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open DatumForge drawing",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("DatumForge drawing") { Patterns = [$"*{ProjectStore.DrawingExtension}"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try { await ViewModel.OpenDrawingAsync(path); }
        catch (Exception exception) { ViewModel.StatusText = $"Could not open drawing: {exception.Message}"; }
    }
}
