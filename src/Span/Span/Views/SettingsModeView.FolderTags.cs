using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using System;
using System.IO;
using System.Linq;

namespace Span.Views;

public sealed partial class SettingsModeView
{
    private void BuildFolderTagEditors()
    {
        FolderTagEditors.Children.Clear();
        FolderTagEditors.Visibility = _settings.FolderTagsEnabled ? Visibility.Visible : Visibility.Collapsed;
        var tags = App.Current.Services.GetRequiredService<FolderTagService>();
        foreach (var color in Enum.GetValues<FolderTagColor>().Where(color => color != FolderTagColor.None))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new FontIcon { Glyph = "\uEA3B", Foreground = ContextMenuService.FolderTagBrush(color), FontSize = 16 };
            row.Children.Add(dot);
            string savedName = tags.GetName(color);
            var name = new TextBox { Text = savedName, PlaceholderText = _loc!.Get("Tag_" + color), MaxLength = 40 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, _loc.Get("Tag_" + color));
            Grid.SetColumn(name, 1); row.Children.Add(name);
            void SaveName()
            {
                if (name.Text == savedName) return;
                string? error = tags.SetName(color, name.Text);
                FolderTagEditorStatus.Text = error ?? "";
                if (error == null) name.Text = savedName = tags.GetName(color);
            }
            name.LostFocus += (_, _) => SaveName();
            name.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { SaveName(); e.Handled = true; } };
            var view = new Button { Content = _loc.Get("Tag_ViewFolders"), VerticalAlignment = VerticalAlignment.Center };
            view.Click += async (_, _) =>
            {
                var paths = tags.GetTaggedPaths(color).Where(path => Directory.Exists(path) || File.Exists(path))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                var list = new ListView { ItemsSource = paths, IsItemClickEnabled = true, MaxHeight = 380, MinWidth = 260 };
                var dialog = new ContentDialog
                {
                    Title = string.IsNullOrEmpty(tags.GetName(color)) ? _loc.Get("Tag_" + color) : tags.GetName(color),
                    Content = paths.Count == 0 ? new TextBlock { Text = _loc.Get("Tag_NoFolders") } : list,
                    CloseButtonText = _loc.Get("Tag_Close"), XamlRoot = XamlRoot, RequestedTheme = ActualTheme
                };
                list.ItemClick += (_, click) =>
                {
                    if (click.ClickedItem is not string path) return;
                    string? folder = Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path) : null;
                    if (folder == null) return;
                    var owner = BrowsingOwner ?? App.Current.GetRegisteredWindows().OfType<MainWindow>().FirstOrDefault(window => window.Content?.XamlRoot == XamlRoot);
                    dialog.Hide();
                    if (owner == null) return;
                    BackRequested?.Invoke(this, EventArgs.Empty);
                    ((IContextMenuHost)owner).PerformOpenInNewTab(folder);
                };
                await dialog.ShowAsync();
            };
            Grid.SetColumn(view, 2); row.Children.Add(view);
            FolderTagEditors.Children.Add(row);
        }
    }
}
