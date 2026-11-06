using OrielFolder.Models;
using OrielFolder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrielFolder;

public sealed partial class MainWindow
{
    private void OnToolbarFolderTagsLoaded(object sender, RoutedEventArgs e)
    {
        if (ToolbarFolderTags.Children.Count == 0)
        {
            foreach (var color in Enum.GetValues<FolderTagColor>().Where(c => c != FolderTagColor.None)
                .Append(FolderTagColor.None))
            {
                var brush = ContextMenuService.FolderTagBrush(color == FolderTagColor.None ? FolderTagColor.Gray : color);
                var button = new Button
                {
                    Tag = color,
                    Style = (Style)RootGrid.Resources["CompactToolbarButtonStyle"],
                    MinWidth = 16,
                    Padding = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    AllowFocusOnInteraction = false,
                    Content = new Ellipse
                    {
                        Width = 12, Height = 12,
                        Fill = color == FolderTagColor.None ? null : brush,
                        Stroke = brush, StrokeThickness = 1
                    }
                };
                AutomationProperties.SetAutomationId(button, "FolderTag_" + color);
                button.Click += OnToolbarFolderTagClick;
                Grid.SetColumn(button, ToolbarFolderTags.Children.Count);
                ToolbarFolderTags.Children.Add(button);
            }
        }
        RefreshToolbarFolderTags();
    }

    private void RefreshToolbarFolderTags()
    {
        if (ToolbarFolderTags == null || _isClosed) return;
        var tags = App.Current.Services.GetService(typeof(FolderTagService)) as FolderTagService;
        foreach (var button in ToolbarFolderTags.Children.OfType<Button>())
        {
            var color = (FolderTagColor)button.Tag;
            string name = color == FolderTagColor.None ? "" : tags?.GetName(color) ?? "";
            if (string.IsNullOrEmpty(name)) name = _loc.Get("Tag_" + color);
            ToolTipService.SetToolTip(button, name);
            AutomationProperties.SetName(button, name);
        }
        UpdateToolbarFolderTagState();
    }

    private void UpdateToolbarFolderTagState()
    {
        if (ToolbarFolderTags == null) return;
        bool enabled = !_isClosed && ViewModel?.ActiveExplorer != null && GetCurrentSelectedItems().Any();
        foreach (var button in ToolbarFolderTags.Children.OfType<Button>()) button.IsEnabled = enabled;
    }

    private async void OnToolbarFolderTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FolderTagColor color })
            await ApplyToolbarFolderTagAsync(color);
    }

    private async Task ApplyToolbarFolderTagAsync(FolderTagColor color)
    {
        if (_isClosed || ViewModel?.ActiveExplorer == null) return;
        var tags = App.Current.Services.GetService(typeof(FolderTagService)) as FolderTagService;
        if (tags == null) return;
        // Capture the active pane's selection before the worker yields to another click or pane.
        var paths = GetCurrentSelectedItems()
            .Select(folder => folder.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        // Explicitly assigning a tag also makes the existing tag column display it.
        if (!_settings.FolderTagsEnabled) _settings.FolderTagsEnabled = true;
        string? error;
        try
        {
            error = await Task.Run(() =>
            {
                string? firstError = null;
                foreach (string path in paths)
                {
                    string? result = tags.SetTag(path, color);
                    firstError ??= result;
                }
                return firstError;
            });
        }
        catch (Exception ex) { error = ex.Message; }
        if (_isClosed) return;
        if (error != null) ViewModel.ShowToast(error, 3000, isError: true);
        RefreshFolderTags();
    }

    internal void RefreshFolderTags()
    {
        if (_isClosed || ViewModel == null) return;
        RefreshSidebarColorLabels();
        foreach (var explorer in ViewModel.EnumerateOwnedExplorers())
            _ = explorer.RefreshColorLabelCategoryAsync();
        RefreshToolbarFolderTags();
        var tags = App.Current.Services.GetService(typeof(FolderTagService)) as FolderTagService;
        if (tags == null) return;
        bool enabled = _settings.FolderTagsEnabled;
        var pending = new Stack<ViewModels.FolderViewModel>(ViewModel.EnumerateOwnedExplorers()
            .SelectMany(explorer => explorer.Columns.Append(explorer.CurrentFolder)).OfType<ViewModels.FolderViewModel>());
        var visited = new HashSet<ViewModels.FolderViewModel>();
        while (pending.TryPop(out var column))
        {
            if (!visited.Add(column)) continue;
            foreach (var item in column.Children)
            {
                item.TagColor = enabled ? tags.GetCachedTag(item.Path) ?? FolderTagColor.None : FolderTagColor.None;
                item.NotifyTagTextChanged();
                item.TagInjected = !enabled || tags.GetCachedTag(item.Path).HasValue;
                // Only visit already loaded children; tagging never enumerates the filesystem here.
                if (item is ViewModels.FolderViewModel folder && folder.Children.Count > 0) pending.Push(folder);
            }
        }
    }
}
