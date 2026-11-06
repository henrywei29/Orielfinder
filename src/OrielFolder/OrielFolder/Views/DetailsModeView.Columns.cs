using CommunityToolkit.WinUI.Controls;
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

    private void RebuildColumnHeader()
    {
        _headerWidthsDirty = true;
        foreach (var splitter in HeaderGrid.Children.OfType<GridSplitter>().ToList())
            HeaderGrid.Children.Remove(splitter);
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
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
            var splitter = new GridSplitter
            {
                Width = 2,
                Padding = new Thickness(0),
                ResizeDirection = GridSplitter.GridResizeDirection.Columns,
                ResizeBehavior = GridSplitter.GridResizeBehavior.PreviousAndNext,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Opacity = 0.3
            };
            Grid.SetColumn(splitter, HeaderGrid.ColumnDefinitions.Count - 1);
            HeaderGrid.Children.Add(splitter);
        }
        UpdateAllVisibleContainerWidths();
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
