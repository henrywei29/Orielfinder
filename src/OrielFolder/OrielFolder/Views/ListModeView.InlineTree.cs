using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using OrielFolder.Helpers;
using OrielFolder.ViewModels;

namespace OrielFolder.Views;

public sealed partial class ListModeView
{
    private readonly System.Collections.ObjectModel.ObservableCollection<VisibleRowGroup<FileSystemViewModel>> _treeGroups = new();
    private Microsoft.UI.Xaml.Data.CollectionViewSource? _treeGroupSource;
    private void SubscribeInlineTree()
    {
        if (_viewModel == null) return;
        _viewModel.InlineTree.Changed -= OnInlineTreeChanged;
        _viewModel.InlineTree.Changed += OnInlineTreeChanged;
    }

    private void OnInlineTreeChanged()
    {
        if (DispatcherQueue is { HasThreadAccess: false } dq)
        {
            dq.TryEnqueue(OnInlineTreeChanged);
            return;
        }
        if (!_isLoaded || _isCleanedUp) return;
        if (_currentGroupBy == "None" || string.IsNullOrEmpty(_currentGroupBy)) RebuildListItems();
        else RebuildGroupedListItems();
    }

    private void RestoreTreeSelection(FileSystemViewModel? savedSelection, string[] selectedPaths)
    {
        var wanted = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0 && savedSelection != null && !IsParentDotDot(savedSelection)) wanted.Add(savedSelection.Path);
        var visible = ListGridView.Items.OfType<FileSystemViewModel>().ToList();
        bool wasSyncing = _isSyncingSelection;
        _isSyncingSelection = true;
        try
        {
            var desired = visible.Where(item => !IsParentDotDot(item) && wanted.Contains(item.Path)).ToList();
            for (int i = ListGridView.SelectedItems.Count - 1; i >= 0; i--)
                if (ListGridView.SelectedItems[i] is not FileSystemViewModel selectedItem || !desired.Contains(selectedItem)) ListGridView.SelectedItems.RemoveAt(i);
            foreach (var item in visible)
                if (desired.Contains(item) && !ListGridView.SelectedItems.Contains(item)) ListGridView.SelectedItems.Add(item);
            if (ListGridView.SelectedItems.Count == 0 && visible.Count > 0) ListGridView.SelectedItem = visible[0];
            var selected = ListGridView.SelectedItems.OfType<FileSystemViewModel>().Where(item => !IsParentDotDot(item)).ToList();
            if (ViewModel?.CurrentFolder is { } folder)
            {
                folder.SyncSelectedItems(selected.Cast<object>().ToList());
                folder.SelectedChild = selected.FirstOrDefault(item => string.Equals(item.Path, savedSelection?.Path, StringComparison.OrdinalIgnoreCase))
                    ?? selected.FirstOrDefault();
            }
        }
        finally { _isSyncingSelection = wasSyncing; }
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
