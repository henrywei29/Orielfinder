using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.ViewModels;
using Span.Views;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace Span
{
    public sealed partial class MainWindow
    {
        private WorkspaceFolderPane? ResolveWorkspacePane(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is WorkspaceFolderPane pane) return pane;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private ExplorerViewModel? ResolvePaneExplorer(DependencyObject? element)
        {
            if (ResolveWorkspacePane(element) is { } pane) return pane.Explorer;
            if (element == LeftPaneContainer || (element != null && IsDescendant(LeftPaneContainer, element))) return ViewModel.LeftExplorer;
            if (element == RightPaneContainer || (element != null && IsDescendant(RightPaneContainer, element))) return ViewModel.RightExplorer;
            return null;
        }

        private void ActivateWorkspacePaneFromElement(DependencyObject? element)
        {
            var view = ResolveWorkspacePane(element);
            if (view == null || ViewModel.ActiveTab?.WorkspaceLayout is not { Enabled: true } group) return;
            var pane = group.Panes.FirstOrDefault(p => ReferenceEquals(p.Explorer, view.Explorer));
            if (pane != null) ActivateWorkspaceView(pane);
        }

        private bool TryGetVisibleWorkspacePane(ExplorerViewModel explorer, out WorkspaceFolderPane view)
        {
            var pane = _workspaceTab == ViewModel.ActiveTab && ViewModel.IsFreeLayoutActive
                ? _workspaceTab?.WorkspaceLayout?.Panes.FirstOrDefault(p => ReferenceEquals(p.Explorer, explorer)) : null;
            view = pane != null && _workspaceViews.TryGetValue(pane, out var found) ? found : null!;
            return view != null;
        }

        private void OnWorkspaceMillerItemsLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ItemsControl items)
                ApplyCheckboxToItemsControl(items, _millerSelectionMode);
        }

        private void QueueWorkspaceColumnLayout(WorkspacePaneState pane, WorkspaceFolderPane view, NotifyCollectionChangedEventArgs change)
        {
            if (_isClosed) return;
            if (change.Action == NotifyCollectionChangedAction.Add && !pane.Explorer.IsReplacingLastColumn)
                view.MillerScroller.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed || !TryGetVisibleWorkspacePane(pane.Explorer, out var current) || current != view) return;
                    AdjustSpacerAndScrollAfterReplace(pane.Explorer, view.MillerScroller,
                        GetMillerSpacer(view.MillerScroller)!, view.MillerItems);
                    ApplyCheckboxToItemsControl(view.MillerItems, _millerSelectionMode);
                });
            else if (change.Action != NotifyCollectionChangedAction.Add && !pane.Explorer.IsReplacingLastColumn)
            {
                var spacer = GetMillerSpacer(view.MillerScroller);
                if (spacer != null) spacer.Width = 0;
            }
            UpdateFileSystemWatcherPaths();
        }

        private bool RouteWorkspaceBeforeReplace(ExplorerViewModel explorer, int vanishingColumns)
        {
            if (!TryGetVisibleWorkspacePane(explorer, out var view)) return false;
            SetMillerSpacerWidth(GetMillerSpacer(view.MillerScroller)!, view.MillerItems, vanishingColumns);
            return true;
        }

        private bool RouteWorkspaceAfterReplace(ExplorerViewModel explorer, bool inserted)
        {
            if (!TryGetVisibleWorkspacePane(explorer, out var view)) return false;
            if (inserted) view.MillerScroller.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isClosed || !TryGetVisibleWorkspacePane(explorer, out var current) || current != view) return;
                AdjustSpacerAndScrollAfterReplace(explorer, view.MillerScroller, GetMillerSpacer(view.MillerScroller)!, view.MillerItems);
                ApplyCheckboxToItemsControl(view.MillerItems, _millerSelectionMode);
            });
            return true;
        }

        private bool WatchWorkspacePaths()
        {
            if (!ViewModel.IsFreeLayoutActive || ViewModel.ActiveTab?.WorkspaceLayout is not { Enabled: true } group) return false;
            _watcherService.SetWatchedPaths(group.Panes.SelectMany(p => p.Explorer.Columns)
                .Select(c => c.Path).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase));
            return true;
        }
    }
}
