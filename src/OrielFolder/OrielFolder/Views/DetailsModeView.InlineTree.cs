using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OrielFolder.Helpers;
using OrielFolder.ViewModels;

namespace OrielFolder.Views;

public sealed partial class DetailsModeView
{
    private bool _treeRefreshQueued;
    private readonly System.Collections.ObjectModel.ObservableCollection<FileSystemViewModel> _treeRows = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<VisibleRowGroup<FileSystemViewModel>> _treeGroups = new();
    private Microsoft.UI.Xaml.Data.CollectionViewSource? _treeGroupSource;

    private void SubscribeInlineTree()
    {
        if (_viewModel == null) return;
        _viewModel.InlineTree.Changed -= OnInlineTreeChanged;
        _viewModel.InlineTree.Changed += OnInlineTreeChanged;
        _viewModel.PropertyChanged -= OnTreeExplorerChanged;
        _viewModel.PropertyChanged += OnTreeExplorerChanged;
    }

    private void UnsubscribeInlineTree()
    {
        if (_viewModel == null) return;
        _viewModel.InlineTree.Changed -= OnInlineTreeChanged;
        _viewModel.PropertyChanged -= OnTreeExplorerChanged;
    }

    private void OnTreeExplorerChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SortItems replaces Children and emits CurrentItems synchronously; do not schedule another sort.
        if (_viewModel?.CurrentFolder?.IsSorting == true) return;
        if (e.PropertyName is not (nameof(ExplorerViewModel.CurrentFolder) or nameof(ExplorerViewModel.CurrentItems)
            or nameof(ExplorerViewModel.CurrentPath)) || _treeRefreshQueued) return;
        _treeRefreshQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _treeRefreshQueued = false;
            if (_isCleanedUp || !_isLoaded) return;
            _unfilteredItems = null;
            ApplyCurrentView();
        });
    }

    private void OnInlineTreeChanged()
    {
        if (DispatcherQueue is { HasThreadAccess: false } dq)
        {
            dq.TryEnqueue(OnInlineTreeChanged);
            return;
        }
        if (_isLoaded && !_isCleanedUp) ApplyFiltersAndGrouping();
    }

    private void SetTreeItemsSource(object source)
    {
        var folder = ViewModel?.CurrentFolder;
        var selectedPaths = DetailsListView.SelectedItems.OfType<FileSystemViewModel>()
            .Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedPaths.Count == 0 && folder?.SelectedChild is { } focused) selectedPaths.Add(focused.Path);
        _isSyncingSelection = true;
        try
        {
            HashSet<FileSystemViewModel> visible;
            if (source is IReadOnlyList<VisibleRowGroup<FileSystemViewModel>> groups)
            {
                VisibleRows.UpdateGroups(_treeGroups, groups, item => item.Path);
                _treeGroupSource ??= new Microsoft.UI.Xaml.Data.CollectionViewSource { Source = _treeGroups, IsSourceGrouped = true };
                if (!ReferenceEquals(DetailsListView.ItemsSource, _treeGroupSource.View)) DetailsListView.ItemsSource = _treeGroupSource.View;
                visible = groups.SelectMany(group => group).ToHashSet();
            }
            else if (source is IReadOnlyList<FileSystemViewModel> rows)
            {
                VisibleRows.Update(_treeRows, rows, item => item.Path);
                if (!ReferenceEquals(DetailsListView.ItemsSource, _treeRows)) DetailsListView.ItemsSource = _treeRows;
                visible = rows.ToHashSet();
            }
            else
            {
                if (!ReferenceEquals(DetailsListView.ItemsSource, source)) DetailsListView.ItemsSource = source;
                visible = DetailsListView.Items.OfType<FileSystemViewModel>().ToHashSet();
            }
            for (int i = DetailsListView.SelectedItems.Count - 1; i >= 0; i--)
                if (DetailsListView.SelectedItems[i] is not FileSystemViewModel selected || !visible.Contains(selected))
                    DetailsListView.SelectedItems.RemoveAt(i);
            foreach (var item in visible)
                if (selectedPaths.Contains(item.Path) && !DetailsListView.SelectedItems.Contains(item))
                    DetailsListView.SelectedItems.Add(item);
            if (folder != null)
            {
                folder.SyncSelectedItems(DetailsListView.SelectedItems);
                if (DetailsListView.SelectedItems.Count == 0) folder.SelectedChild = null;
            }
        }
        finally { _isSyncingSelection = false; }
    }

    private IEnumerable<FileSystemViewModel> ArrangeTreeSiblings(IEnumerable<FileSystemViewModel> items)
    {
        if (HasAnyFilter) items = items.Where(PassesFilter);
        bool mix = ViewModel?.CurrentFolder?.IsDownloadsFolder == true;
        return items.OrderBy(item => item, Comparer<FileSystemViewModel>.Create((a, b) =>
        {
            if (!mix && (a is FolderViewModel) != (b is FolderViewModel)) return a is FolderViewModel ? -1 : 1;
            int result = _currentSortBy switch
            {
                "DateModified" => a.DateModifiedValue.CompareTo(b.DateModifiedValue),
                "Size" => a.SizeValue.CompareTo(b.SizeValue),
                "Type" => StringComparer.CurrentCultureIgnoreCase.Compare(a.FileType, b.FileType),
                "Git" => StringComparer.OrdinalIgnoreCase.Compare(a.GitStatusText, b.GitStatusText),
                _ => NaturalStringComparer.Instance.Compare(a.Name, b.Name)
            };
            return _isAscending ? result : -result;
        }));
    }

    private async void OnTreeExpanderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderViewModel folder } anchor || ViewModel == null) return;
        var explorer = ViewModel;
        var root = explorer.CurrentPath;
        try { await explorer.InlineTree.ToggleAsync(folder); }
        catch (Exception ex)
        {
            if (ReferenceEquals(ViewModel, explorer) && root == explorer.CurrentPath && anchor.XamlRoot != null)
                InlineTreeViewHelper.ShowLoadError(anchor, ex.Message);
        }
    }

    private void OnTreeExpanderDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => e.Handled = true;
}
