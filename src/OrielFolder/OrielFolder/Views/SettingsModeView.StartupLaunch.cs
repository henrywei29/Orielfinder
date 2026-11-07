using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OrielFolder.Services;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace OrielFolder.Views;

/// <summary>
/// Settings → General → "Launch at startup". The on/off state is owned by Windows (the packaged
/// startup task), so the switch always shows what Windows reports instead of a stored setting.
/// </summary>
public sealed partial class SettingsModeView
{
    private bool _startupLaunchSyncing;
    private string? _startupLaunchNoteKey;

    private async Task RefreshStartupLaunchAsync()
        => ShowStartupLaunchState(await StartupLaunchService.GetStateAsync());

    private async void OnStartupLaunchToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _startupLaunchSyncing) return;
        bool wanted = StartupLaunchToggle.IsOn;
        StartupLaunchToggle.IsEnabled = false;
        var state = await StartupLaunchService.SetEnabledAsync(wanted);
        ShowStartupLaunchState(state);
    }

    private void ShowStartupLaunchState(StartupTaskState? state)
    {
        var (isOn, canChange, noteKey) = Helpers.StartupLaunchRules.Describe(state);
        _startupLaunchSyncing = true;
        try
        {
            if (StartupLaunchToggle.IsOn != isOn) StartupLaunchToggle.IsOn = isOn;
        }
        finally { _startupLaunchSyncing = false; }
        StartupLaunchToggle.IsEnabled = canChange;
        _startupLaunchNoteKey = noteKey;
        StartupLaunchNote.Text = noteKey != null ? _loc?.Get(noteKey) ?? noteKey : string.Empty;
        StartupLaunchNote.Visibility = noteKey != null ? Visibility.Visible : Visibility.Collapsed;
        Helpers.DebugLogger.Log($"[StartupLaunch] Settings shows {state?.ToString() ?? "unavailable"}");
    }
}
