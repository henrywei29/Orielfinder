using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OrielFolder.ViewModels;

namespace OrielFolder;

public sealed partial class MainWindow
{
    private Dictionary<string, double>? _millerColumnWidths;
    private Dictionary<string, double> MillerColumnWidths
    {
        get
        {
            if (_millerColumnWidths != null) return _millerColumnWidths;
            try
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, double>>(_settings.MillerColumnWidthsJson);
                _millerColumnWidths = new(StringComparer.OrdinalIgnoreCase);
                if (saved != null)
                    foreach (var entry in saved)
                        if (double.IsFinite(entry.Value) && entry.Value >= 150 && entry.Value <= 600)
                            _millerColumnWidths[entry.Key] = entry.Value;
            }
            catch (JsonException) { _millerColumnWidths = new(StringComparer.OrdinalIgnoreCase); }
            return _millerColumnWidths;
        }
    }

    private void OnMillerColumnWidthLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid { DataContext: FolderViewModel folder } grid
            && MillerColumnWidths.TryGetValue(folder.Path, out var width)) grid.Width = width;
    }

    private void RememberMillerColumnWidth(Grid grid)
    {
        if (grid.DataContext is FolderViewModel folder && double.IsFinite(grid.Width))
            MillerColumnWidths[folder.Path] = Math.Clamp(grid.Width, 150, 600);
    }

    private static ItemsControl? GetMillerColumnHost(DependencyObject element)
    {
        for (var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element); parent != null;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
            if (parent is ItemsControl items) return items;
        return null;
    }
}
