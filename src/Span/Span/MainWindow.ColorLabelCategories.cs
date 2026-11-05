using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Input;
using Span.Models;
using Span.Services;

namespace Span;

public sealed partial class MainWindow
{
    private void OnSidebarColorLabelsLoaded(object sender, RoutedEventArgs e) => RefreshSidebarColorLabels();

    private void OnColorLabelsHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        _settings.ColorLabelsExpanded = !_settings.ColorLabelsExpanded;
        RefreshSidebarColorLabels();
        e.Handled = true;
    }

    private void RefreshSidebarColorLabels()
    {
        if (SidebarColorLabelItems == null || _isClosed) return;
        SidebarColorLabelsText.Text = _loc.Get("Sidebar_ColorLabels");
        ColorLabelsChevron.Glyph = _settings.ColorLabelsExpanded ? "\uE70D" : "\uE76C";
        SidebarColorLabelItems.Visibility = _settings.ColorLabelsExpanded ? Visibility.Visible : Visibility.Collapsed;
        var tags = App.Current.Services.GetService(typeof(FolderTagService)) as FolderTagService;
        if (tags == null) return;
        if (SidebarColorLabelItems.Children.Count == 0)
        {
            foreach (var color in Enum.GetValues<FolderTagColor>().Where(c => c != FolderTagColor.None))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = ContextMenuService.FolderTagBrush(color) });
                var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                text.SetBinding(TextBlock.FontSizeProperty, new Microsoft.UI.Xaml.Data.Binding
                {
                    Source = Helpers.FontScaleService.Instance, Path = new PropertyPath("ItemFontSize")
                });
                row.Children.Add(text);
                var button = new Button { Tag = color, Content = row, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(4,0,4,0),
                    Padding = new Thickness(20,5,8,5), BorderThickness = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                button.Click += async (_, _) =>
                {
                    var explorer = ViewModel.ActiveExplorer;
                    if (explorer == null) return;
                    ViewModel.SwitchViewMode(ViewMode.Details);
                    await explorer.ShowColorLabelAsync(color);
                    ViewModel.UpdateStatusBar();
                };
                SidebarColorLabelItems.Children.Add(button);
            }
        }
        foreach (var button in SidebarColorLabelItems.Children.OfType<Button>())
        {
            var color = (FolderTagColor)button.Tag;
            var name = tags.GetName(color);
            ((TextBlock)((StackPanel)button.Content).Children[1]).Text = string.IsNullOrEmpty(name) ? _loc.Get("Tag_" + color) : name;
        }
    }
}
