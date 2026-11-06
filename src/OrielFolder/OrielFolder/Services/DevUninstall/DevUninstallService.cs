using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using OrielFolder.Helpers;

namespace OrielFolder.Services.DevUninstall;

public sealed record DevUninstallStep(string Name, string Status, string Detail);

public sealed record DevUninstallCleanupResult(bool Ok, bool Cancelled, IReadOnlyList<DevUninstallStep> Steps, string? Error)
{
    public static DevUninstallCleanupResult Failed(string error) => new(false, false, Array.Empty<DevUninstallStep>(), error);
}

/// <summary>
/// Uninstall for the OrielFolder <b>Dev</b> package only. MSIX virtualises this app's HKCU/AppData writes, so
/// real-system cleanup (stale folder-open overrides, out-of-package caches, Listary plugin) runs in an elevated
/// helper outside the container (<c>Assets/DevUninstall/Uninstall-OrielFolderDev.ps1</c>). The package itself is
/// removed last, by the app, only after the helper reported success.
/// </summary>
public static class DevUninstallService
{
    public const string DevPackageName = "LocalDevelopment.OrielFolderDev";

    /// <summary>True only when running as the Dev package (never in a release identity).</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return Windows.ApplicationModel.Package.Current.Id.Name == DevPackageName; }
            catch (InvalidOperationException) { return false; } // unpackaged
        }
    }

    public static DevUninstallPlan CreatePlan() => DevUninstallPlan.Create(
        AppIdentity.MarkedOutputRoot, AppIdentity.PreviewCacheRoot, AppIdentity.LocalDataRoot,
        AppIdentity.DataFolderName, DateTime.Now);

    public static async Task WriteConfigurationBackupAsync(DevUninstallPlan plan, string json)
    {
        Directory.CreateDirectory(plan.BackupDirectory);
        await ConfigurationBackupService.WriteFileAsync(plan.ConfigurationBackupPath, json);
        if (!File.Exists(plan.ConfigurationBackupPath)) throw new IOException("Backup file was not written.");
        DebugLogger.Log($"[DevUninstall] Configuration backup written: {plan.ConfigurationBackupPath}");
    }

    public static async Task<DevUninstallCleanupResult> RunCleanupAsync(DevUninstallPlan plan)
    {
        if (!IsAvailable) return DevUninstallCleanupResult.Failed("Not running as the OrielFolder Dev package.");
        var script = Path.Combine(AppContext.BaseDirectory, "Assets", "DevUninstall", "Uninstall-OrielFolderDev.ps1");
        if (!File.Exists(script)) return DevUninstallCleanupResult.Failed("Uninstall helper is missing: " + script);
        foreach (var folder in plan.FoldersToDelete)
            if (!plan.IsAllowedDeletion(folder)) return DevUninstallCleanupResult.Failed("Refusing to delete " + folder);

        Directory.CreateDirectory(plan.RunDirectory);
        var planJson = JsonSerializer.Serialize(new
        {
            plan.ResultPath,
            plan.RegistryBackupDirectory,
            plan.DataFolderName,
            plan.FoldersToDelete,
            plan.ProtectedFolders,
            Classes = StaleFolderOpenRule.Classes,
            ExpectedUserSid = WindowsIdentity.GetCurrent().User?.Value ?? "",
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(plan.PlanPath, planJson);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Plan \"{plan.PlanPath}\"",
                UseShellExecute = true,
                Verb = "runas", // leaves the MSIX container; one UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process == null) return DevUninstallCleanupResult.Failed("Uninstall helper did not start.");
            await process.WaitForExitAsync();
            DebugLogger.Log($"[DevUninstall] Helper exited with {process.ExitCode}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC declined
        {
            DebugLogger.Log("[DevUninstall] UAC cancelled");
            return new DevUninstallCleanupResult(false, true, Array.Empty<DevUninstallStep>(), null);
        }

        return ReadResult(plan.ResultPath);
    }

    internal static DevUninstallCleanupResult ReadResult(string resultPath)
    {
        if (!File.Exists(resultPath)) return DevUninstallCleanupResult.Failed("Uninstall helper wrote no result.");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(resultPath));
            var root = doc.RootElement;
            var steps = new List<DevUninstallStep>();
            if (root.TryGetProperty("Steps", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var s in arr.EnumerateArray())
                    steps.Add(new DevUninstallStep(Str(s, "Name"), Str(s, "Status"), Str(s, "Detail")));
            bool ok = root.TryGetProperty("Ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            string? error = null;
            foreach (var s in steps) if (s.Status == "failed") error = s.Detail;
            foreach (var s in steps) DebugLogger.Log($"[DevUninstall] {s.Name}: {s.Status} {s.Detail}");
            return new DevUninstallCleanupResult(ok && error == null, false, steps, error);
        }
        catch (JsonException ex)
        {
            return DevUninstallCleanupResult.Failed("Unreadable helper result: " + ex.Message);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>
    /// Removes the running Dev package (the app is terminated by the deployment service). Returns an error message
    /// if removal could not be started; null when it was started.
    /// </summary>
    public static async Task<string?> RemovePackageAsync()
    {
        if (!IsAvailable) return "Not running as the OrielFolder Dev package.";
        var fullName = Windows.ApplicationModel.Package.Current.Id.FullName;
        DebugLogger.Log($"[DevUninstall] Removing package {fullName}");
        try
        {
            var result = await new Windows.Management.Deployment.PackageManager().RemovePackageAsync(fullName);
            if (result.IsRegistered) return result.ErrorText;
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException
                                   or System.Runtime.InteropServices.COMException)
        {
            DebugLogger.Log($"[DevUninstall] PackageManager removal failed: {ex.GetType().Name} {ex.Message}; falling back");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Remove-AppxPackage -Package '{fullName}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return null;
            }
            catch (Win32Exception fallback)
            {
                return $"{ex.Message} / {fallback.Message}. Run: Remove-AppxPackage -Package {fullName}";
            }
        }
    }
}
