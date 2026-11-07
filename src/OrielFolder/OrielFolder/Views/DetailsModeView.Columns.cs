
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OrielFolder.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Storage;

namespace OrielFolder.Views;

public sealed partial class DetailsModeView
{
    private static event Action<string>? DetailsColumnOrderChanged;
    private string[] _columnOrder = DetailsColumnOrder.Parse(null);
    private readonly Dictionary<string, (ColumnDefinition Definition, FrameworkElement Header, Button Button)> _detailColumns = new();
    private readonly Dictionary<string, double> _detailColumnWidths = new();
    private readonly Dictionary<ColumnDefinition, long> _columnWidthCallbacks = new();
    private bool _showLocationColumn;
    private GridLength _savedLocationWidth = new(180);
    private long _suppressHeaderClickUntil;
    private string? _pressedColumn;
    private Point _columnPressPoint;
    private bool _isColumnDragging;

    private void InitializeDetailsColumns()
    {
        _detailColumns.Add("Name", (NameColumnDef, NameHeaderContainer, NameHeaderButton));
        _detailColumns.Add("CloudStatus", (CloudStatusColumnDef, CloudStatusHeaderContainer, CloudStatusHeaderButton));
        _detailColumns.Add("ColorTag", (ColorTagColumnDef, ColorTagHeaderContainer, ColorTagHeaderButton));
        _detailColumns.Add("Location", (LocationColumnDef, LocationHeaderContainer, LocationHeaderButton));
        _detailColumns.Add("DateModified", (DateColumnDef, DateHeaderContainer, DateHeaderButton));
        _detailColumns.Add("Type", (TypeColumnDef, TypeHeaderContainer, TypeHeaderButton));
        _detailColumns.Add("Size", (SizeColumnDef, SizeHeaderContainer, SizeHeaderButton));
        _detailColumns.Add("Git", (GitColumnDef, GitHeaderContainer, GitHeaderButton));
        foreach (var pair in _detailColumns)
        {
            var header = (Grid)pair.Value.Header;
            header.Tag = pair.Key;
            header.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            // ButtonBase handles pointer activation. Observe that route explicitly so
            // a click still sorts while a drag can start over either text or whitespace.
            header.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnColumnPointerPressed), true);
            header.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnColumnPointerMoved), true);
            header.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnColumnPointerReleased), true);
            header.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnColumnPointerCanceled), true);
        }
        RestoreDetailsColumnOrder();
    }

    private bool IsDetailColumnVisible(string id) => id switch
    {
        "Name" => _nameColumnVisible,
        "CloudStatus" => _cloudStatusColumnVisible,
        "ColorTag" => _colorTagColumnVisible,
        "Location" => _showLocationColumn,
        "DateModified" => _dateColumnVisible,
        "Type" => _typeColumnVisible,
        "Size" => _sizeColumnVisible,
        "Git" => _gitColumnVisible,
        _ => true
    };

    private void RestoreDetailsColumnOrder()
    {
        string? saved = null;
        if (_localSettings?.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite
            && composite.TryGetValue("ColumnOrder", out var value)) saved = value as string;
        _columnOrder = DetailsColumnOrder.Parse(saved);
        RebuildColumnHeader();
    }

    // Empty column after the last visible column. It takes the free space once Name has a
    // fixed width, so resizing a column moves every column to its right (Explorer style).
    private readonly ColumnDefinition _fillerColumn = new() { Width = new GridLength(0) };

    private void RebuildColumnHeader()
    {
        _headerWidthsDirty = true;
        foreach (var separator in HeaderGrid.Children.OfType<Grid>().Where(g => g.Tag is ColumnSeparatorTag).ToList())
            HeaderGrid.Children.Remove(separator);
        HeaderGrid.ColumnDefinitions.Clear();
        var visible = _columnOrder.Where(IsDetailColumnVisible).ToArray();
        foreach (var id in _columnOrder)
            _detailColumns[id].Header.Visibility = IsDetailColumnVisible(id) ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < visible.Length; i++)
        {
            var column = _detailColumns[visible[i]];
            Grid.SetColumn(column.Header, HeaderGrid.ColumnDefinitions.Count);
            HeaderGrid.ColumnDefinitions.Add(column.Definition);
            if (i == visible.Length - 1) continue;
            // 2 px gap column (rows reserve the same 2 px as a right margin on each cell).
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            var separator = CreateColumnSeparator(visible[i]);
            Grid.SetColumn(separator, HeaderGrid.ColumnDefinitions.Count - 1);
            HeaderGrid.Children.Add(separator);
        }
        HeaderGrid.ColumnDefinitions.Add(_fillerColumn);
        UpdateFillerColumn();
        UpdateAllVisibleContainerWidths();
    }

    private sealed record ColumnSeparatorTag(string LeftColumn);

    /// <summary>
    /// A visible 1 px line with an 8 px drag area centered on the 2 px gap column.
    /// Dragging it resizes only the column on its left.
    /// </summary>
    private Grid CreateColumnSeparator(string leftColumn)
    {
        var line = new Border { Style = (Style)Resources["ColumnSeparatorLineStyle"] };
        var separator = new Grid
        {
            Tag = new ColumnSeparatorTag(leftColumn),
            Margin = new Thickness(-3, 0, -3, 0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { line }
        };
        Canvas.SetZIndex(separator, 1);
        CursorHelper.SetCursor(separator, Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        separator.PointerEntered += (_, _) => line.Opacity = 1;
        separator.PointerExited += (_, _) => { if (!ReferenceEquals(_resizeSeparator, separator)) line.Opacity = 0.55; };
        separator.PointerPressed += OnSeparatorPointerPressed;
        separator.PointerMoved += OnSeparatorPointerMoved;
        separator.PointerReleased += OnSeparatorPointerReleased;
        separator.PointerCaptureLost += OnSeparatorPointerCaptureLost;
        return separator;
    }

    private Grid? _resizeSeparator;
    private ColumnDefinition? _resizeColumn;
    private double _resizeStartX;
    private double _resizeStartWidth;

    private void OnSeparatorPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not Grid { Tag: ColumnSeparatorTag tag } separator
            || !args.GetCurrentPoint(separator).Properties.IsLeftButtonPressed
            || !_detailColumns.TryGetValue(tag.LeftColumn, out var column)) return;
        // From now on every column keeps its own width; free space goes to the filler.
        FixNameColumnWidth();
        _resizeSeparator = separator;
        _resizeColumn = column.Definition;
        _resizeStartX = args.GetCurrentPoint(this).Position.X;
        _resizeStartWidth = column.Definition.ActualWidth;
        separator.CapturePointer(args.Pointer);
        args.Handled = true;
    }

    private void OnSeparatorPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_resizeColumn == null || !ReferenceEquals(sender, _resizeSeparator)) return;
        double delta = args.GetCurrentPoint(this).Position.X - _resizeStartX;
        double width = DetailsColumnLayout.ResizeWidth(_resizeStartWidth, delta, _resizeColumn.MinWidth);
        if (!_resizeColumn.Width.IsAbsolute || Math.Abs(_resizeColumn.Width.Value - width) > 0.5)
            _resizeColumn.Width = new GridLength(width);
        args.Handled = true;
    }

    private void OnSeparatorPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!ReferenceEquals(sender, _resizeSeparator)) return;
        ((UIElement)sender).ReleasePointerCapture(args.Pointer);
        EndColumnResize();
        args.Handled = true;
    }

    private void OnSeparatorPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _resizeSeparator)) EndColumnResize();
    }

    private void EndColumnResize()
    {
        if (_resizeSeparator?.Children.FirstOrDefault() is Border line) line.Opacity = 0.55;
        bool resized = _resizeColumn != null;
        _resizeSeparator = null;
        _resizeColumn = null;
        if (resized) SaveColumnWidths();
    }

    /// <summary>Turns the stretching Name column into a fixed width (its current width).</summary>
    private void FixNameColumnWidth()
    {
        if (!NameColumnDef.Width.IsStar) return;
        double width = NameColumnDef.ActualWidth > 0 ? NameColumnDef.ActualWidth : NameColumnDef.MinWidth;
        NameColumnDef.Width = new GridLength(Math.Round(Math.Max(width, NameColumnDef.MinWidth)));
        UpdateFillerColumn();
    }

    private void UpdateFillerColumn()
    {
        var width = DetailsColumnLayout.NeedsFiller(IsDetailColumnVisible("Name"), NameColumnDef.Width.IsStar)
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (_fillerColumn.Width != width) _fillerColumn.Width = width;
    }

    // ── Saved widths (shared by every Details view, like the column order) ──

    private static event Action<string>? DetailsColumnWidthsChanged;

    private void SaveColumnWidths()
    {
        var widths = _detailColumns
            .Where(pair => DetailsColumnLayout.IsToggleable(pair.Key) && pair.Value.Definition.Width.IsAbsolute)
            .Select(pair => new KeyValuePair<string, double>(pair.Key, pair.Value.Definition.Width.Value));
        string saved = DetailsColumnLayout.SerializeWidths(widths);
        if (_localSettings != null)
        {
            var composite = _localSettings.Values["DetailsViewSort"] as ApplicationDataCompositeValue
                ?? new ApplicationDataCompositeValue();
            composite["ColumnWidths"] = saved;
            _localSettings.Values["DetailsViewSort"] = composite;
        }
        DebugLogger.Log($"[DetailsColumns] Widths {saved}");
        DetailsColumnWidthsChanged?.Invoke(saved);
    }

    private void RestoreColumnWidths()
    {
        string? saved = null;
        if (_localSettings?.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite
            && composite.TryGetValue("ColumnWidths", out var value)) saved = value as string;
        ApplyColumnWidths(saved);
    }

    private void OnDetailsColumnWidthsChanged(string saved)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnDetailsColumnWidthsChanged(saved));
            return;
        }
        if (_isCleanedUp || _resizeColumn != null) return;
        ApplyColumnWidths(saved);
    }

    private void ApplyColumnWidths(string? saved)
    {
        foreach (var pair in DetailsColumnLayout.ParseWidths(saved))
        {
            if (!_detailColumns.TryGetValue(pair.Key, out var column)) continue;
            double width = Math.Max(pair.Value, column.Definition.MinWidth);
            if (!column.Definition.Width.IsAbsolute || column.Definition.Width.Value != width)
                column.Definition.Width = new GridLength(width);
        }
        UpdateFillerColumn();
    }

    // ── Horizontal scrolling: the header follows the list ──

    private ScrollViewer? _listScrollViewer;

    private void OnHeaderScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The header fills at least the visible width; wider columns make it (and the rows) scroll.
        if (HeaderGrid.MinWidth != e.NewSize.Width) HeaderGrid.MinWidth = e.NewSize.Width;
        AttachListScrollViewer();
    }

    private void AttachListScrollViewer()
    {
        if (_listScrollViewer != null) return;
        _listScrollViewer = VisualTreeHelpers.FindChild<ScrollViewer>(DetailsListView);
        if (_listScrollViewer == null) return;
        _listScrollViewer.ViewChanging += OnListViewChanging;
        _listScrollViewer.ViewChanged += OnListViewChanged;
    }

    private void DetachListScrollViewer()
    {
        if (_listScrollViewer == null) return;
        _listScrollViewer.ViewChanging -= OnListViewChanging;
        _listScrollViewer.ViewChanged -= OnListViewChanged;
        _listScrollViewer = null;
    }

    private void OnListViewChanging(object? sender, ScrollViewerViewChangingEventArgs e)
        => SyncHeaderOffset(e.NextView.HorizontalOffset);

    private void OnListViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_listScrollViewer != null) SyncHeaderOffset(_listScrollViewer.HorizontalOffset);
    }

    private void SyncHeaderOffset(double offset)
    {
        if (Math.Abs(HeaderScroller.HorizontalOffset - offset) > 0.5)
            HeaderScroller.ChangeView(offset, null, null, true);
    }

    private void RegisterColumnWidthCallbacks()
    {
        foreach (var column in _detailColumns.Values)
            if (!_columnWidthCallbacks.ContainsKey(column.Definition))
                _columnWidthCallbacks.Add(column.Definition, column.Definition.RegisterPropertyChangedCallback(
                    ColumnDefinition.WidthProperty, OnColumnWidthChanged));
    }

    private bool _headerWidthsDirty = true;
    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e) => _headerWidthsDirty = true;

    private void OnHeaderLayoutUpdated(object? sender, object e)
    {
        if (!_headerWidthsDirty) return;
        _headerWidthsDirty = false;
        RefreshColumnWidths();
    }

    private void RefreshColumnWidths()
    {
        bool changed = false;
        foreach (var pair in _detailColumns)
        {
            double width = IsDetailColumnVisible(pair.Key) ? pair.Value.Definition.ActualWidth : 0;
            if (!double.IsFinite(width) || width < 0) continue;
            if (!_detailColumnWidths.TryGetValue(pair.Key, out var previous) || Math.Abs(width - previous) > 0.01)
            {
                _detailColumnWidths[pair.Key] = width;
                changed = true;
            }
        }
        if (changed) UpdateAllVisibleContainerWidths();
    }

    // Border widths can safely be applied when ListView recycles a row during measure.
    // Each cell keeps a stable identity; its position comes from the shared order.
    private void ApplyCellWidths(Grid grid)
    {
        string? lastVisible = _columnOrder.LastOrDefault(IsDetailColumnVisible);
        foreach (var cell in grid.Children.OfType<Border>())
        {
            if (cell.Tag is not string id || !_detailColumns.TryGetValue(id, out var column)) continue;
            int position = Array.IndexOf(_columnOrder, id);
            if (Grid.GetColumn(cell) != position) Grid.SetColumn(cell, position);
            bool visible = IsDetailColumnVisible(id);
            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (cell.Visibility != visibility) cell.Visibility = visibility;
            double fallback = column.Definition.Width.IsAbsolute ? column.Definition.Width.Value : column.Definition.MinWidth;
            double desiredWidth = visible ? (_detailColumnWidths.TryGetValue(id, out var width) && width > 0 ? width : fallback) : 0;
            if (cell.Width != desiredWidth) cell.Width = desiredWidth;
            var margin = visible && id != lastVisible ? new Thickness(0, 0, 2, 0) : new Thickness(0);
            if (cell.Margin != margin) cell.Margin = margin;
        }
    }

    private void OnColumnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string id } header
            || !args.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
        _pressedColumn = id;
        _columnPressPoint = args.GetCurrentPoint(HeaderGrid).Position;
        _isColumnDragging = false;
        if (_detailColumns[id].Button.PointerCaptures?.Any(pointer => pointer.PointerId == args.Pointer.PointerId) != true)
            header.CapturePointer(args.Pointer);
        DebugLogger.Log($"[DetailsColumns] Press {id}");
    }

    private void OnColumnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_pressedColumn == null) return;
        var point = args.GetCurrentPoint(HeaderGrid);
        if (!point.Properties.IsLeftButtonPressed) { ClearColumnDrag(); return; }
        if (!_isColumnDragging && Math.Abs(point.Position.X - _columnPressPoint.X) < 6
            && Math.Abs(point.Position.Y - _columnPressPoint.Y) < 6) return;
        if (!_isColumnDragging)
        {
            DebugLogger.Log($"[DetailsColumns] Drag {_pressedColumn}");
            // Transfer the Button's capture only after the drag threshold, preserving clicks.
            _detailColumns[_pressedColumn].Header.CapturePointer(args.Pointer);
        }
        _isColumnDragging = true;
        _suppressHeaderClickUntil = Environment.TickCount64 + 500;
        _detailColumns[_pressedColumn].Header.Opacity = 0.6;
        args.Handled = true;
    }

    private void OnColumnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pressedColumn == null) return;
        string source = _pressedColumn;
        bool dragging = _isColumnDragging;
        var point = args.GetCurrentPoint(HeaderGrid).Position;
        ClearColumnDrag();
        if (sender is UIElement header) header.ReleasePointerCapture(args.Pointer);
        if (!dragging) return;
        _suppressHeaderClickUntil = Environment.TickCount64 + 500;
        args.Handled = true;
        if (point.Y < 0 || point.Y > HeaderGrid.ActualHeight) return;
        foreach (var id in _columnOrder.Where(IsDetailColumnVisible))
        {
            var target = _detailColumns[id].Header;
            double left = target.TransformToVisual(HeaderGrid).TransformPoint(new Point()).X;
            if (point.X < left || point.X > left + target.ActualWidth) continue;
            DebugLogger.Log($"[DetailsColumns] Drop {source} -> {id}");
            MoveDetailColumn(source, id, point.X >= left + target.ActualWidth / 2);
            return;
        }
    }

    private void OnColumnPointerCanceled(object sender, PointerRoutedEventArgs args) => ClearColumnDrag();

    private void ClearColumnDrag()
    {
        if (_pressedColumn != null) _detailColumns[_pressedColumn].Header.Opacity = 1;
        _pressedColumn = null;
        _isColumnDragging = false;
    }

    private void MoveDetailColumn(string source, string target, bool after)
    {
        var order = DetailsColumnOrder.Move(_columnOrder, source, target, after);
        if (_columnOrder.SequenceEqual(order)) return;
        string saved = DetailsColumnOrder.Serialize(order);
        if (_localSettings != null)
        {
            var composite = _localSettings.Values["DetailsViewSort"] as ApplicationDataCompositeValue
                ?? new ApplicationDataCompositeValue();
            composite["ColumnOrder"] = saved;
            _localSettings.Values["DetailsViewSort"] = composite;
        }
        OnDetailsColumnOrderChanged(saved);
        DetailsColumnOrderChanged?.Invoke(saved);
    }

    private void OnDetailsColumnOrderChanged(string saved)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnDetailsColumnOrderChanged(saved));
            return;
        }
        var order = DetailsColumnOrder.Parse(saved);
        if (_columnOrder.SequenceEqual(order)) return;
        _columnOrder = order;
        RebuildColumnHeader();
    }
}
