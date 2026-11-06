using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace OrielFolder.Helpers;

internal static class InlineTreeViewHelper
{
    private static readonly Brush Clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private sealed record StripePalette(Brush Odd);
    private static readonly ConditionalWeakTable<XamlRoot, StripePalette> Palettes = new();
    // Lists that use stripes (they call ApplyStripe from ContainerContentChanging). RefreshStripeTree only touches
    // these; it used to stripe every ListViewBase under the pane host, e.g. the Home page card GridViews.
    private static readonly ConditionalWeakTable<ListViewBase, object> StripedLists = new();
    private static readonly object StripedMarker = new();

    internal static void ApplyStripe(ListViewBase list, SelectorItem container, int index)
    {
        StripedLists.AddOrUpdate(list, StripedMarker);
        var palette = list.XamlRoot is { } xaml ? Palettes.GetValue(xaml, CreatePalette) : null;
        Brush brush = index % 2 == 1 ? palette?.Odd ?? Clear : Clear;
        if (!ReferenceEquals(container.Background, brush)) container.Background = brush;
    }

    private static StripePalette CreatePalette(XamlRoot xaml)
    {
        if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            && xaml.Content is FrameworkElement root)
        {
            var theme = root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            foreach (var resources in new[] { root.Resources, Application.Current.Resources })
            {
                if (resources.ThemeDictionaries.TryGetValue(theme, out var dictionary)
                    && dictionary is ResourceDictionary palette
                    && palette.TryGetValue("OrielFolderBgLayer2Brush", out var value) && value is Brush stripe)
                    return new StripePalette(stripe);
            }
        }
        return new StripePalette(Clear);
    }

    internal static void RefreshStripes(ListViewBase list)
    {
        if (list.XamlRoot is { } xaml) Palettes.Remove(xaml);
        RefreshStripesCore(list);
    }

    private static void RefreshStripesCore(ListViewBase list)
    {
        if (list.ItemsPanelRoot is not Panel panel) return;
        foreach (var child in panel.Children)
            if (child is SelectorItem container)
                ApplyStripe(list, container, list.IndexFromContainer(container));
    }

    internal static void RefreshStripeTree(DependencyObject root)
    {
        if (root is FrameworkElement element && element.XamlRoot == null) return;
        if (root is FrameworkElement loaded) Palettes.Remove(loaded.XamlRoot);
        RefreshStripeTreeCore(root);
    }

    private static void RefreshStripeTreeCore(DependencyObject root)
    {
        if (root is ListViewBase list)
        {
            if (StripedLists.TryGetValue(list, out _)) RefreshStripesCore(list);
            return;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            RefreshStripeTreeCore(VisualTreeHelper.GetChild(root, i));
    }

    internal static void ShowLoadError(FrameworkElement anchor, string message) =>
        new Flyout { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 } }.ShowAt(anchor);
}
