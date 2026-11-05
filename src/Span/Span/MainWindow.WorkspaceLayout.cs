using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.ViewModels;
using Span.Views;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Windows.Foundation;

namespace Span
{
    public sealed partial class MainWindow
    {
        private Grid? _workspaceHost;
        private Canvas? _workspaceCanvas;
        private ComboBox? _workspaceArrangement;
        private Models.TabItem? _workspaceTab;
        private bool _syncingWorkspace;
        private bool _workspaceDividerDragging;
        private WorkspaceLayoutState? _workspaceDividerState;
        private int _workspaceDividerColumns;
        private readonly Dictionary<WorkspacePaneState, WorkspaceFolderPane> _workspaceViews = new();
        private readonly Dictionary<WorkspacePaneState, NotifyCollectionChangedEventHandler> _workspaceColumnHandlers = new();
        private readonly Dictionary<WorkspacePaneState, System.ComponentModel.PropertyChangedEventHandler> _workspacePathHandlers = new();
        private readonly List<UIElement> _workspaceDividers = new();

        private WorkspaceFolderPane? ActiveWorkspaceView => ViewModel.IsFreeLayoutActive
            && _workspaceTab == ViewModel.ActiveTab && _workspaceTab?.WorkspaceLayout?.ActivePane is { } pane
            && _workspaceViews.TryGetValue(pane, out var view) ? view : null;

        private void EnsureWorkspaceHost()
        {
            if (_workspaceHost != null) return;
            _workspaceCanvas = new Canvas { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            _workspaceHost = new Grid { Visibility = Visibility.Collapsed };
            _workspaceHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _workspaceHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4) };
            var add = new Button { Content = _loc.Get("Workspace_AddPane"), Padding = new Thickness(8, 2, 8, 2) };
            add.Click += (_, _) => AddWorkspacePane();
            toolbar.Children.Add(add);
            _workspaceArrangement = new ComboBox { MinWidth = 100 };
            foreach (var key in new[] { "Workspace_Horizontal", "Workspace_Vertical", "Workspace_Grid" })
                _workspaceArrangement.Items.Add(new ComboBoxItem { Content = _loc.Get(key) });
            _workspaceArrangement.SelectionChanged += (_, _) =>
            {
                if (_syncingWorkspace || _workspaceArrangement.SelectedIndex < 0 || ViewModel.ActiveTab?.WorkspaceLayout is not { } state) return;
                state.Arrangement = (PaneTileArrangement)_workspaceArrangement.SelectedIndex;
                state.ColumnWeights = state.RowWeights = Array.Empty<double>();
                _settings.MultiPaneTileArrangement = _workspaceArrangement.SelectedIndex;
                ArrangeWorkspacePanes();
            };
            toolbar.Children.Add(_workspaceArrangement);
            _workspaceHost.Children.Add(toolbar);
            Grid.SetRow(_workspaceCanvas, 1);
            _workspaceHost.Children.Add(_workspaceCanvas);
            Grid.SetColumn(_workspaceHost, Grid.GetColumn(ClassicPaneHost));
            Grid.SetRow(_workspaceHost, Grid.GetRow(ClassicPaneHost));
            ((Grid)ClassicPaneHost.Parent).Children.Add(_workspaceHost);
            _workspaceCanvas.SizeChanged += (_, _) =>
            {
                _workspaceCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, _workspaceCanvas.ActualWidth, _workspaceCanvas.ActualHeight) };
                ArrangeWorkspacePanes();
            };
        }

        private void ToggleWorkspaceLayout()
        {
            var tab = ViewModel.ActiveTab;
            if (tab == null || tab.ViewMode is ViewMode.Settings or ViewMode.ActionLog or ViewMode.RecycleBin) return;
            if (tab.WorkspaceLayout?.Enabled == true)
            {
                ViewModel.LeaveWorkspacePaneBridge();
                tab.WorkspaceLayout.Enabled = false;
                ViewModel.IsSplitViewEnabled = false;
                tab.IsSplitEnabled = false;
                SyncWorkspaceLayoutForActiveTab();
                UpdateViewModeVisibility();
                return;
            }
            ViewModel.SaveActiveTabState();
            var state = tab.WorkspaceLayout ??= new WorkspaceLayoutState { ClassicSplitEnabled = tab.IsSplitEnabled };
            state.Enabled = true;
            tab.IsSplitEnabled = true;
            SyncWorkspaceLayoutForActiveTab();
        }

        private void SyncWorkspaceLayoutForActiveTab()
        {
            if (_syncingWorkspace || _isClosed || ViewModel.IsSwitchingTab) return;
            _syncingWorkspace = true;
            try
            {
                var tab = ViewModel.ActiveTab;
                var state = tab?.WorkspaceLayout;
                bool folderTab = tab != null && tab.ViewMode is not (ViewMode.Settings or ViewMode.ActionLog or ViewMode.RecycleBin);
                var kind = (PaneLayoutKind)Math.Clamp(_settings.MultiPaneLayoutMode, 0, 2);
                if (!folderTab || kind == PaneLayoutKind.Classic || (state?.Enabled != true && tab?.IsSplitEnabled != true))
                {
                    if (_workspaceHost != null) _workspaceHost.Visibility = Visibility.Collapsed;
                    ClassicPaneHost.Visibility = Visibility.Visible;
                    if (ViewModel.IsFreeLayoutActive)
                    {
                        ViewModel.LeaveWorkspacePaneBridge();
                        if (kind == PaneLayoutKind.Classic && state?.Enabled == true)
                        {
                            ViewModel.IsSplitViewEnabled = true;
                            tab!.IsSplitEnabled = true;
                        }
                        ApplyClassicPaneSizing();
                    }
                    _workspaceTab = null;
                    return;
                }
                EnsureWorkspaceHost();
                state ??= tab!.WorkspaceLayout = new WorkspaceLayoutState
                {
                    Enabled = true, ClassicSplitEnabled = tab.IsSplitEnabled,
                    Arrangement = (PaneTileArrangement)Math.Clamp(_settings.MultiPaneTileArrangement, 0, 2)
                };
                state.Enabled = true;
                state.Layout = kind;
                if (state.Panes.Count == 0)
                {
                    string path = tab!.Explorer?.CurrentPath ?? "";
                    if (string.IsNullOrWhiteSpace(path) || path == "PC") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var first = tab.Explorer?.Columns.Count > 0 && tab.Explorer.CurrentPath != "PC"
                        ? tab.Explorer : ViewModel.CreateWorkspaceExplorer(path);
                    state.Add(first!, NormalizeWorkspaceMode(tab.ViewMode));
                    var second = tab.SplitRightExplorer?.Columns.Count > 0 && tab.SplitRightExplorer.CurrentPath != "PC"
                        ? tab.SplitRightExplorer : ViewModel.CreateWorkspaceExplorer(path);
                    state.Add(second!, NormalizeWorkspaceMode(tab.SplitRightViewMode)).Bounds = new PaneBounds(48, 48, 420, 300);
                }
                _workspaceTab = tab;
                foreach (var cached in _workspaceViews)
                    cached.Value.Visibility = state.Panes.Contains(cached.Key) ? Visibility.Visible : Visibility.Collapsed;
                foreach (var pane in state.Panes)
                {
                    var view = GetOrCreateWorkspacePane(pane);
                    view.Visibility = Visibility.Visible;
                    if (!_workspaceCanvas!.Children.Contains(view)) _workspaceCanvas.Children.Add(view);
                }
                ClassicPaneHost.Visibility = Visibility.Collapsed;
                _workspaceHost!.Visibility = Visibility.Visible;
                _workspaceArrangement!.Visibility = kind == PaneLayoutKind.Tiled ? Visibility.Visible : Visibility.Collapsed;
                _workspaceArrangement.SelectedIndex = (int)state.Arrangement;
                ActivateWorkspaceView(state.ActivePane ?? state.Panes[0]);
                ArrangeWorkspacePanes();
                RemoveClosedWorkspaceViews();
            }
            finally { _syncingWorkspace = false; }
        }

        private static ViewMode NormalizeWorkspaceMode(ViewMode mode)
            => mode is ViewMode.Settings or ViewMode.ActionLog or ViewMode.RecycleBin ? ViewMode.MillerColumns : mode;

        private WorkspaceFolderPane GetOrCreateWorkspacePane(WorkspacePaneState pane)
        {
            if (_workspaceViews.TryGetValue(pane, out var existing)) return existing;
            var view = new WorkspaceFolderPane(pane.Explorer, MillerColumnsControl.ItemTemplate,
                MillerColumnsControl.ItemsPanel, _contextMenuService, this, _hwnd, _settings);
            _workspaceViews.Add(pane, view);
            view.SetMode(pane.Mode);
            view.Activated += _ => ActivateWorkspaceView(pane);
            view.CloseRequested += _ => CloseWorkspacePane(pane);
            view.ModeChanged += _ =>
            {
                if (_syncingWorkspace) return;
                ActivateWorkspaceView(pane);
                ViewModel.CurrentViewMode = view.Mode;
                pane.Mode = view.Mode;
                UpdateToolbarButtonStates();
            };
            view.MillerItems.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnMillerKeyDown), true);
            view.MillerItems.AddHandler(UIElement.CharacterReceivedEvent,
                new Windows.Foundation.TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(OnMillerCharacterReceived), true);
            view.MillerScroller.BringIntoViewRequested += OnMillerBringIntoViewRequested;
            view.MillerScroller.ViewChanged += OnMillerViewChangedTrimSpacer;
            view.MillerItems.Loaded += OnWorkspaceMillerItemsLoaded;
            NotifyCollectionChangedEventHandler columnsChanged = (_, args) => QueueWorkspaceColumnLayout(pane, view, args);
            pane.Explorer.Columns.CollectionChanged += columnsChanged;
            _workspaceColumnHandlers.Add(pane, columnsChanged);
            System.ComponentModel.PropertyChangedEventHandler pathChanged = (_, _) => view.UpdatePath();
            pane.Explorer.PropertyChanged += pathChanged;
            _workspacePathHandlers.Add(pane, pathChanged);
            view.AllowDrop = true;
            view.DragOver += OnPaneDragOver;
            view.Drop += OnPaneDrop;
            view.DragLeave += OnPaneDragLeave;
            WireWorkspaceDrag(pane, view);
            return view;
        }

        private void ActivateWorkspaceView(WorkspacePaneState pane)
        {
            var group = ViewModel.ActiveTab?.WorkspaceLayout;
            if (group?.Enabled != true || !group.Panes.Contains(pane) || !_workspaceViews.TryGetValue(pane, out var view)) return;
            bool activeChanged = !ViewModel.IsFreeLayoutActive || group.ActivePane != pane || ViewModel.LeftExplorer != pane.Explorer;
            if (activeChanged)
            {
                bool previousSuppression = _suppressFocusOnViewModeChange;
                _suppressFocusOnViewModeChange = true;
                try { ViewModel.ActivateWorkspacePane(pane); }
                finally { _suppressFocusOnViewModeChange = previousSuppression; }
                if (!_syncingWorkspace) SetViewModeVisibility(ViewModel.CurrentViewMode);
            }
            bool refresh = activeChanged || _syncingWorkspace;
            if (refresh)
                foreach (var item in group.Panes)
                    if (_workspaceViews.TryGetValue(item, out var itemView)) itemView.SetActive(item == pane);
            if (group.Layout == PaneLayoutKind.Floating && _workspaceCanvas != null)
            {
                var ordered = _workspaceViews.Values.Where(v => v != view && v.Visibility == Visibility.Visible && _workspaceCanvas.Children.Contains(v))
                    .OrderBy(Canvas.GetZIndex).Append(view).ToArray();
                for (int i = 0; i < ordered.Length; i++) Canvas.SetZIndex(ordered[i], i);
            }
            // Pointer/focus events bubble through several controls in the same pane.
            if (!refresh) return;
            UpdateToolbarButtonStates();
            UpdateFileSystemWatcherPaths();
        }

        private void AddWorkspacePane()
        {
            var state = ViewModel.ActiveTab?.WorkspaceLayout;
            if (state?.Enabled != true) return;
            var path = state.ActivePane?.Explorer.CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || path == "PC") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var pane = state.Add(ViewModel.CreateWorkspaceExplorer(path), ViewMode.MillerColumns);
            pane.Bounds = new PaneBounds(24 + (state.Panes.Count % 6) * 24, 24 + (state.Panes.Count % 6) * 24, 420, 300);
            var view = GetOrCreateWorkspacePane(pane);
            _workspaceCanvas!.Children.Add(view);
            ActivateWorkspaceView(pane);
            ArrangeWorkspacePanes();
        }

        private void CloseWorkspacePane(WorkspacePaneState pane)
        {
            var state = ViewModel.ActiveTab?.WorkspaceLayout;
            if (state == null || !state.Remove(pane)) return;
            DisposeWorkspaceView(pane);
            // A classic slot may retain the same explorer for switching layouts.
            if (!ViewModel.EnumerateOwnedExplorers().Contains(pane.Explorer)) pane.Explorer.Cleanup();
            if (state.ActivePane != null) ActivateWorkspaceView(state.ActivePane);
            ArrangeWorkspacePanes();
        }

        private void ArrangeWorkspacePanes()
        {
            var state = ViewModel.ActiveTab?.WorkspaceLayout;
            if (_workspaceCanvas == null || state?.Enabled != true || _workspaceTab != ViewModel.ActiveTab) return;
            double width = _workspaceCanvas.ActualWidth, height = _workspaceCanvas.ActualHeight;
            if (width < 1 || height < 1) return;
            int columns = state.Arrangement switch
            {
                PaneTileArrangement.Horizontal => state.Panes.Count,
                PaneTileArrangement.Vertical => 1,
                _ => (int)Math.Ceiling(Math.Sqrt(state.Panes.Count))
            };
            int rows = (state.Panes.Count + columns - 1) / columns;
            int dividerCount = state.Layout == PaneLayoutKind.Tiled ? columns + rows - 2 : 0;
            if (!_workspaceDividerDragging && (_workspaceDividerState != state
                || _workspaceDividerColumns != columns || _workspaceDividers.Count != dividerCount))
            {
                foreach (var divider in _workspaceDividers) _workspaceCanvas.Children.Remove(divider);
                _workspaceDividers.Clear();
                _workspaceDividerState = state;
                _workspaceDividerColumns = columns;
            }
            var tiled = PaneLayoutGeometry.Tile(state.Panes.Count, columns, width, height, state.ColumnWeights, state.RowWeights);
            for (int i = 0; i < state.Panes.Count; i++)
            {
                var pane = state.Panes[i];
                var view = GetOrCreateWorkspacePane(pane);
                var bounds = state.Layout == PaneLayoutKind.Floating
                    ? PaneLayoutGeometry.ConstrainFloating(pane.Bounds, width, height) : tiled[i];
                if (state.Layout == PaneLayoutKind.Floating) pane.Bounds = bounds;
                Canvas.SetLeft(view, bounds.X);
                Canvas.SetTop(view, bounds.Y);
                view.MinWidth = Math.Min(150, bounds.Width);
                view.MinHeight = Math.Min(120, bounds.Height);
                view.Width = bounds.Width;
                view.Height = bounds.Height;
                view.ResizeGrip.Visibility = state.Layout == PaneLayoutKind.Floating ? Visibility.Visible : Visibility.Collapsed;
            }
            if (state.Layout == PaneLayoutKind.Tiled && !_workspaceDividerDragging)
            {
                if (_workspaceDividers.Count == 0)
                {
                    for (int i = 1; i < columns; i++) AddWorkspaceDivider(state, true, i - 1, tiled[i].X, height, columns);
                    for (int i = 1; i < rows; i++) AddWorkspaceDivider(state, false, i - 1, tiled[i * columns].Y, width, rows);
                }
                for (int i = 1; i < columns; i++)
                {
                    var divider = (Border)_workspaceDividers[i - 1];
                    Canvas.SetLeft(divider, tiled[i].X); divider.Height = height;
                }
                for (int i = 1; i < rows; i++)
                {
                    var divider = (Border)_workspaceDividers[columns - 2 + i];
                    Canvas.SetTop(divider, tiled[i * columns].Y); divider.Width = width;
                }
            }
        }

        private void AddWorkspaceDivider(WorkspaceLayoutState state, bool vertical, int index, double position, double length, int count)
        {
            var divider = new Border
            {
                Width = vertical ? 1 : length, Height = vertical ? length : 1,
                Background = GetThemeBrush("SpanAccentDimBrush")
            };
            Canvas.SetLeft(divider, vertical ? position : 0);
            Canvas.SetTop(divider, vertical ? 0 : position);
            Canvas.SetZIndex(divider, 1_000_000);
            double[] start = Array.Empty<double>();
            double startPosition = 0;
            bool dragging = false;
            divider.PointerEntered += (_, _) => Helpers.CursorHelper.SetCursor(divider, vertical
                ? Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast
                : Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth);
            divider.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed) return;
                dragging = divider.CapturePointer(e.Pointer);
                if (!dragging) return;
                _workspaceDividerDragging = true;
                position = vertical ? Canvas.GetLeft(divider) : Canvas.GetTop(divider);
                var point = e.GetCurrentPoint(_workspaceCanvas).Position;
                startPosition = vertical ? point.X : point.Y;
                var weights = vertical ? state.ColumnWeights : state.RowWeights;
                start = weights.Length == count ? weights.ToArray() : Enumerable.Repeat(1.0, count).ToArray();
                e.Handled = true;
            };
            divider.PointerMoved += (_, e) =>
            {
                if (!dragging || start.Length != count) return;
                double available = vertical ? _workspaceCanvas!.ActualWidth : _workspaceCanvas!.ActualHeight;
                if (available < 1) return;
                double sum = start.Sum();
                double pair = start[index] + start[index + 1];
                var point = e.GetCurrentPoint(_workspaceCanvas).Position;
                double movement = (vertical ? point.X : point.Y) - startPosition;
                double delta = movement * sum / available;
                double min = Math.Min((vertical ? 150 : 120) * sum / available, pair / 2);
                var weights = start.ToArray();
                weights[index] = Math.Clamp(start[index] + delta, min, pair - min);
                weights[index + 1] = pair - weights[index];
                if (vertical) state.ColumnWeights = weights; else state.RowWeights = weights;
                if (vertical) Canvas.SetLeft(divider, position + (weights[index] - start[index]) * available / sum);
                else Canvas.SetTop(divider, position + (weights[index] - start[index]) * available / sum);
                ArrangeWorkspacePanes();
                e.Handled = true;
            };
            divider.PointerReleased += (_, e) =>
            {
                if (!dragging) return;
                dragging = false;
                _workspaceDividerDragging = false;
                divider.ReleasePointerCapture(e.Pointer);
                ArrangeWorkspacePanes();
                e.Handled = true;
            };
            divider.PointerCaptureLost += (_, _) =>
            {
                dragging = false;
                _workspaceDividerDragging = false;
                ArrangeWorkspacePanes();
            };
            _workspaceCanvas!.Children.Add(divider);
            _workspaceDividers.Add(divider);
        }

        private void WireWorkspaceDrag(WorkspacePaneState pane, WorkspaceFolderPane view)
        {
            void Wire(FrameworkElement grip, bool resize)
            {
                bool dragging = false;
                Point start = default;
                PaneBounds original = default;
                grip.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
                    ActivateWorkspaceView(pane);
                    dragging = grip.CapturePointer(e.Pointer);
                    start = e.GetCurrentPoint(_workspaceCanvas).Position;
                    original = new PaneBounds(Canvas.GetLeft(view), Canvas.GetTop(view), view.ActualWidth, view.ActualHeight);
                    e.Handled = true;
                };
                grip.PointerMoved += (_, e) =>
                {
                    if (!dragging || _workspaceCanvas == null || ViewModel.ActiveTab?.WorkspaceLayout is not { } state) return;
                    var point = e.GetCurrentPoint(_workspaceCanvas).Position;
                    if (state.Layout == PaneLayoutKind.Floating)
                    {
                        var requested = resize ? original with { Width = original.Width + point.X - start.X, Height = original.Height + point.Y - start.Y }
                            : original with { X = original.X + point.X - start.X, Y = original.Y + point.Y - start.Y };
                        pane.Bounds = PaneLayoutGeometry.ConstrainFloating(requested, _workspaceCanvas.ActualWidth, _workspaceCanvas.ActualHeight);
                        ArrangeWorkspacePanes();
                    }
                    e.Handled = true;
                };
                grip.PointerReleased += (_, e) =>
                {
                    if (!dragging) return;
                    dragging = false;
                    var state = ViewModel.ActiveTab?.WorkspaceLayout;
                    if (!resize && state?.Layout == PaneLayoutKind.Tiled && _workspaceCanvas != null)
                    {
                        var point = e.GetCurrentPoint(_workspaceCanvas).Position;
                        var target = state.Panes.FirstOrDefault(p => p != pane && _workspaceViews.TryGetValue(p, out var targetView)
                            && new Rect(Canvas.GetLeft(targetView), Canvas.GetTop(targetView), targetView.ActualWidth, targetView.ActualHeight).Contains(point));
                        if (target != null) state.MoveBefore(pane, target);
                        ArrangeWorkspacePanes();
                    }
                    grip.ReleasePointerCapture(e.Pointer);
                    e.Handled = true;
                };
                grip.PointerCaptureLost += (_, _) => dragging = false;
            }
            Wire(view.MoveGrip, false);
            Wire(view.ResizeGrip, true);
        }

        private void DisposeWorkspaceView(WorkspacePaneState pane)
        {
            if (!_workspaceViews.Remove(pane, out var view)) return;
            if (_workspaceColumnHandlers.Remove(pane, out var changed)) pane.Explorer.Columns.CollectionChanged -= changed;
            if (_workspacePathHandlers.Remove(pane, out var pathChanged)) pane.Explorer.PropertyChanged -= pathChanged;
            view.MillerItems.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnMillerKeyDown));
            view.MillerItems.RemoveHandler(UIElement.CharacterReceivedEvent,
                new Windows.Foundation.TypedEventHandler<UIElement, CharacterReceivedRoutedEventArgs>(OnMillerCharacterReceived));
            view.MillerScroller.BringIntoViewRequested -= OnMillerBringIntoViewRequested;
            view.MillerScroller.ViewChanged -= OnMillerViewChangedTrimSpacer;
            view.MillerItems.Loaded -= OnWorkspaceMillerItemsLoaded;
            _workspaceCanvas?.Children.Remove(view);
            view.Cleanup();
        }

        private void RemoveClosedWorkspaceViews()
        {
            var retained = ViewModel.Tabs.Where(t => t.WorkspaceLayout != null).SelectMany(t => t.WorkspaceLayout!.Panes).ToHashSet();
            foreach (var pane in _workspaceViews.Keys.Where(p => !retained.Contains(p)).ToArray()) DisposeWorkspaceView(pane);
        }

        private void DisposeWorkspaceLayouts()
        {
            foreach (var pane in _workspaceViews.Keys.ToArray()) DisposeWorkspaceView(pane);
        }
    }
}
