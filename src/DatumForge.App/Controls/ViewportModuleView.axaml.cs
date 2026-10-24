using Avalonia.Controls;
using Avalonia.Threading;
using DatumForge.App.ViewModels;

namespace DatumForge.App.Controls;

public partial class ViewportModuleView : UserControl
{
    private readonly DispatcherTimer _viewSettingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public ViewportModuleView()
    {
        InitializeComponent();
        _viewSettingsSaveTimer.Tick += ViewSettingsSaveTimer_OnTick;
    }

    public ViewportModuleItemViewModel? ViewModel => DataContext as ViewportModuleItemViewModel;

    public CoordinateSubmissionResult SubmitCoordinate(string? input) => DrawingSurface.SubmitCoordinate(input);

    private void Viewport_OnEntityCreated(object? sender, EntityCreatedEventArgs e) => ViewModel?.Owner.RecordEntityCreated(e.Entity);

    private void Viewport_OnSelectionChanged(object? sender, EntitySelectionChangedEventArgs e) =>
        ViewModel?.Owner.UpdateSelection(e.EntityIds);

    private void Viewport_OnTransformRequested(object? sender, EntityTransformRequestedEventArgs e) =>
        ViewModel?.Owner.ApplyTransform(e.Kind, e.EntityIds, e.Reference, e.Target);

    private void Viewport_OnViewChanged(object? sender, EventArgs e)
    {
        ViewModel?.Owner.NotifyViewportChanged();
        _viewSettingsSaveTimer.Stop();
        _viewSettingsSaveTimer.Start();
    }

    private void ViewSettingsSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _viewSettingsSaveTimer.Stop();
        if (ViewModel?.Owner is not { } owner) return;
        try { owner.SaveUserSettings(); }
        catch (Exception exception) { owner.StatusText = $"Could not save viewport settings: {exception.Message}"; }
    }
}
