using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OrielFolder.Services;

namespace OrielFolder.Views;

/// <summary>One modeless settings window, independent of browsing tabs.</summary>
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _current;
    private readonly SettingsModeView _view;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private bool _closed;

    public static void Show(MainWindow owner)
    {
        if (_current == null)
        {
            _current = new SettingsWindow(owner);
            App.Current.RegisterWindow(_current);
        }
        _current._view.BrowsingOwner = owner;
        _current._view.RefreshSettings();
        if (_current.AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
        _current.Activate();
    }

    private SettingsWindow(MainWindow owner)
    {
        _settings = App.Current.Services.GetRequiredService<SettingsService>();
        _localization = App.Current.Services.GetRequiredService<LocalizationService>();
        _view = new SettingsModeView { OwnerWindow = this, BrowsingOwner = owner };
        Content = _view;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 760));
        _view.BackRequested += (_, _) => Close();
        _view.Loaded += (_, _) => ApplyAppearance();
        _settings.SettingChanged += OnSettingChanged;
        _localization.LanguageChanged += ApplyAppearance;
        Closed += (_, _) =>
        {
            _closed = true;
            _settings.SettingChanged -= OnSettingChanged;
            _localization.LanguageChanged -= ApplyAppearance;
            foreach (var window in App.Current.GetRegisteredWindows())
                if (window is MainWindow main) main._isRecordingShortcut = false;
            _current = null;
            App.Current.UnregisterWindow(this);
        };
        ApplyAppearance();
    }

    private void OnSettingChanged(string key, object? value)
    {
        if (key is "Theme" or "FontFamily" or "IconFontScale" or "UseCustomAccent" or "CustomAccentColor")
            DispatcherQueue.TryEnqueue(ApplyAppearance);
    }

    private void ApplyAppearance()
    {
        if (_closed) return;
        Title = _localization.Get("Settings");
        string theme = _settings.Theme;
        MainWindow.ApplyCustomThemeOverrides(_view, theme);
        if (_settings.UseCustomAccent && MainWindow.TryParseAccentHex(_settings.CustomAccentColor, out var accent))
            MainWindow.ApplyAccentOverride(_view, accent, theme);
        _view.RequestedTheme = theme switch
        {
            "light" or "solarized-light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ when MainWindow._customThemes.Contains(theme) => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        var font = new FontFamily(MainWindow.ResolveFontSpec(_settings.FontFamily));
        _view.FontFamily = font;
        var stats = new MainWindow.FontApplyStats();
        MainWindow.ApplyFontToVisualTree(_view, font, stats);
        Helpers.DebugLogger.Log($"[Font] SettingsWindow '{_settings.FontFamily}' applied={stats.Applied} protected={stats.Protected}");
        _view.ApplyIconFontScale(Helpers.FontScaleService.Instance.Level);
    }
}
