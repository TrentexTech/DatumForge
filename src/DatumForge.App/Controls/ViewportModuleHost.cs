using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DatumForge.App.ViewModels;

namespace DatumForge.App.Controls;

public sealed class ViewportModuleHost : Grid
{
    public static readonly StyledProperty<IEnumerable<ViewportModuleItemViewModel>?> ItemsSourceProperty =
        AvaloniaProperty.Register<ViewportModuleHost, IEnumerable<ViewportModuleItemViewModel>?>(nameof(ItemsSource));

    private INotifyCollectionChanged? _observableItems;
    private readonly List<ViewportModuleView> _moduleViews = [];
    private readonly List<ColumnDefinition> _moduleColumns = [];
    private List<ViewportModuleItemViewModel> _items = [];
    private int _flexibleModuleIndex = -1;

    static ViewportModuleHost()
    {
        ItemsSourceProperty.Changed.AddClassHandler<ViewportModuleHost>((host, args) =>
            host.OnItemsSourceChanged(args.OldValue as IEnumerable<ViewportModuleItemViewModel>,
                args.NewValue as IEnumerable<ViewportModuleItemViewModel>));
    }

    public IEnumerable<ViewportModuleItemViewModel>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public CoordinateSubmissionResult SubmitCoordinate(string? input)
    {
        var viewer = _moduleViews.FirstOrDefault(view => view.ViewModel?.IsViewer == true);
        return viewer?.SubmitCoordinate(input)
               ?? new CoordinateSubmissionResult(false, false, "Add the Viewer module before entering coordinates.");
    }

    public bool HandleEscape()
    {
        var viewer = _moduleViews.FirstOrDefault(view => view.ViewModel?.IsViewer == true);
        return viewer?.HandleEscape() == true;
    }

    private void OnItemsSourceChanged(IEnumerable<ViewportModuleItemViewModel>? oldValue,
        IEnumerable<ViewportModuleItemViewModel>? newValue)
    {
        if (_observableItems is not null) _observableItems.CollectionChanged -= Items_CollectionChanged;
        _observableItems = newValue as INotifyCollectionChanged;
        if (_observableItems is not null) _observableItems.CollectionChanged += Items_CollectionChanged;
        Rebuild(newValue);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild(ItemsSource);

    private void Rebuild(IEnumerable<ViewportModuleItemViewModel>? items)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        _moduleViews.Clear();
        _moduleColumns.Clear();
        _items = items?.ToList() ?? [];
        _flexibleModuleIndex = _items.FindIndex(item => item.IsViewer);
        if (_flexibleModuleIndex < 0 && _items.Count > 0) _flexibleModuleIndex = _items.Count - 1;

        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var isFlexible = index == _flexibleModuleIndex;
            var width = isFlexible
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(item.Definition.Size, GridUnitType.Pixel);
            var column = new ColumnDefinition(width)
            {
                MinWidth = item.IsViewer ? 320 : 160
            };
            ColumnDefinitions.Add(column);
            _moduleColumns.Add(column);

            var view = new ViewportModuleView { DataContext = item };
            SetColumn(view, index * 2);
            Children.Add(view);
            _moduleViews.Add(view);

            if (index == _items.Count - 1) continue;
            ColumnDefinitions.Add(new ColumnDefinition(new GridLength(5, GridUnitType.Pixel)));
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = Brush.Parse("#253139")
            };
            splitter.DragCompleted += (_, _) => CaptureSizes();
            SetColumn(splitter, index * 2 + 1);
            Children.Add(splitter);
        }
    }

    private void CaptureSizes()
    {
        if (_items.Count == 0) return;
        var sizes = new List<(ViewportModuleItemViewModel Module, double Size)>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (index == _flexibleModuleIndex) continue;
            sizes.Add((_items[index], _moduleColumns[index].ActualWidth));
        }

        _items[0].Owner.PersistViewportModuleSizes(sizes);
    }
}
