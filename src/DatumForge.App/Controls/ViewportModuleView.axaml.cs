using Avalonia.Controls;
using DatumForge.App.ViewModels;

namespace DatumForge.App.Controls;

public partial class ViewportModuleView : UserControl
{
    public ViewportModuleView() => InitializeComponent();

    public ViewportModuleItemViewModel? ViewModel => DataContext as ViewportModuleItemViewModel;

    public CoordinateSubmissionResult SubmitCoordinate(string? input) => DrawingSurface.SubmitCoordinate(input);

    private void Viewport_OnEntityCreated(object? sender, EntityCreatedEventArgs e) => ViewModel?.Owner.RecordEntityCreated(e.Entity);

    private void Viewport_OnSelectionChanged(object? sender, EntitySelectionChangedEventArgs e) =>
        ViewModel?.Owner.UpdateSelection(e.EntityIds);

    private void Viewport_OnTransformRequested(object? sender, EntityTransformRequestedEventArgs e) =>
        ViewModel?.Owner.ApplyTransform(e.Kind, e.EntityIds, e.Reference, e.Target);
}
