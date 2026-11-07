using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace OrielFolder.Services;

/// <summary>
/// "Launch at startup" through the packaged app's startup task (windows.startupTask in
/// Package.appxmanifest). Windows lists it under Task Manager > Startup apps; no registry Run key.
/// </summary>
public static class StartupLaunchService
{
    /// <summary>Current state, or null when the startup task cannot be read (unpackaged run, missing extension).</summary>
    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(Helpers.StartupLaunchRules.TaskId);
            return task.State;
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[StartupLaunch] GetState failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Turns launch at startup on or off and returns the resulting state (null on failure).</summary>
    public static async Task<StartupTaskState?> SetEnabledAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(Helpers.StartupLaunchRules.TaskId);
            if (enabled)
            {
                // Desktop apps are enabled without a prompt; a task the user turned off in
                // Task Manager stays DisabledByUser and can only be re-enabled by the user.
                var result = await task.RequestEnableAsync();
                Helpers.DebugLogger.Log($"[StartupLaunch] Enable -> {result}");
                return result;
            }
            task.Disable();
            Helpers.DebugLogger.Log($"[StartupLaunch] Disable -> {task.State}");
            return task.State;
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[StartupLaunch] Set({enabled}) failed: {ex.Message}");
            return null;
        }
    }
}
