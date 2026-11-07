using Windows.ApplicationModel;

namespace OrielFolder.Helpers;

/// <summary>
/// How the "Launch at startup" switch shows a Windows startup-task state.
/// Pure mapping, kept separate from the WinRT calls so it can be unit tested.
/// </summary>
public static class StartupLaunchRules
{
    /// <summary>Must match the TaskId of the windows.startupTask extension in Package.appxmanifest.</summary>
    public const string TaskId = "OrielFolderStartup";

    /// <param name="state">The task state, or null when the startup task is not available (e.g. unpackaged run).</param>
    /// <returns>
    /// IsOn: switch position. CanChange: whether the app can change it (Windows only lets the user re-enable
    /// a task they turned off in Task Manager / Settings, and policy-controlled tasks cannot be changed).
    /// NoteKey: localization key of an explanation to show, or null.
    /// </returns>
    public static (bool IsOn, bool CanChange, string? NoteKey) Describe(StartupTaskState? state) => state switch
    {
        StartupTaskState.Enabled => (true, true, null),
        StartupTaskState.Disabled => (false, true, null),
        StartupTaskState.DisabledByUser => (false, false, "Settings_StartupDisabledByUser"),
        StartupTaskState.DisabledByPolicy => (false, false, "Settings_StartupDisabledByPolicy"),
        StartupTaskState.EnabledByPolicy => (true, false, "Settings_StartupEnabledByPolicy"),
        _ => (false, false, "Settings_StartupUnavailable")
    };
}
