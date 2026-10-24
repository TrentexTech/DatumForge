using Avalonia.Controls;
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
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private void Viewport_OnEntityCreated(object? sender, EventArgs e) => ViewModel.NotifyDrawingChanged();

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
