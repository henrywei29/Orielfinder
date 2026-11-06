using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace OrielFolder.Services;

public static class ProductIconService
{
    public static string Normalize(string? name) => name == "classic" ? "classic" : "blue";

    public static string RelativePath(SettingsService settings) =>
        "Assets/ProductIcons/" + Normalize(settings.Get("ProductIcon", "blue")) + ".ico";

    public static void Apply(Window window, SettingsService settings)
    {
        try { window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, RelativePath(settings))); }
        catch (Exception ex) { Helpers.DebugLogger.Log("[ProductIcon] " + ex.Message); }
    }
}
