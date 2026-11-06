using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OrielFolder.Services.DevUninstall;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OrielFolder.Views;

/// <summary>
/// Settings → Advanced → "Uninstall OrielFolder Dev" (Dev package only).
/// Order: confirm → optional configuration backup → elevated cleanup helper → final confirm → remove package.
/// Any failure stops before the package is removed.
/// </summary>
public sealed partial class SettingsModeView
{
    private bool _devUninstallBusy;

    private async void OnDevUninstall(object sender, RoutedEventArgs e)
    {
        if (_devUninstallBusy || _loc == null || !DevUninstallService.IsAvailable) return;
        _devUninstallBusy = true;
        DevUninstallBtn.IsEnabled = false;
        string title = _loc.Get("DevUninstall_Label");
        try
        {
            var plan = DevUninstallService.CreatePlan();
            var backupCheck = new CheckBox
            {
                Content = _loc.Get("DevUninstall_BackupCheck"),
                IsChecked = true,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = string.Format(_loc.Get("DevUninstall_ConfirmBody"), plan.BackupDirectory, plan.ProtectedFolders[0]),
                TextWrapping = TextWrapping.Wrap
            });
            body.Children.Add(backupCheck);
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _loc.Get("DevUninstall_ConfirmTitle"),
                Content = new ScrollViewer { Content = body },
                PrimaryButtonText = _loc.Get("DevUninstall_Button"),
                CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            string backupLine;
            if (backupCheck.IsChecked == true)
            {
                try
                {
                    await DevUninstallService.WriteConfigurationBackupAsync(plan, await CaptureConfigurationAsync());
                    backupLine = string.Format(_loc.Get("DevUninstall_BackupSaved"), plan.ConfigurationBackupPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Helpers.DebugLogger.Log($"[DevUninstall] Backup failed: {ex.GetType().Name} {ex.Message}");
                    await ShowBackupMessageAsync(title, _loc.Get("DevUninstall_BackupFailed"));
                    return;
                }
            }
            else backupLine = _loc.Get("DevUninstall_NoBackup");

            var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            progress.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 });
            progress.Children.Add(new TextBlock
            {
                Text = _loc.Get("DevUninstall_Working"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            });
            var working = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = progress };
            var workingShown = working.ShowAsync();
            DevUninstallCleanupResult result;
            try { result = await DevUninstallService.RunCleanupAsync(plan); }
            finally
            {
                working.Hide();
                await workingShown;
            }

            if (result.Cancelled)
            {
                await ShowBackupMessageAsync(title, _loc.Get("DevUninstall_Cancelled"));
                return;
            }
            if (!result.Ok)
            {
                await ShowBackupMessageAsync(title, string.Format(_loc.Get("DevUninstall_Failed"), result.Error ?? "?"));
                return;
            }

            var ready = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _loc.Get("DevUninstall_ReadyTitle"),
                Content = new TextBlock { Text = string.Format(_loc.Get("DevUninstall_Ready"), backupLine), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = _loc.Get("DevUninstall_RemoveApp"),
                CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            if (await ready.ShowAsync() != ContentDialogResult.Primary) return;

            var error = await DevUninstallService.RemovePackageAsync(); // app is normally terminated during this call
            if (error != null)
                await ShowBackupMessageAsync(title, string.Format(_loc.Get("DevUninstall_RemoveFailed"), error));
        }
        catch (Exception ex)
        {
            // Outermost UI entry point (spec: error-handling): report instead of crashing; nothing is removed after a failure.
            Helpers.DebugLogger.Log($"[DevUninstall] Failed: {ex.GetType().Name} {ex.Message}");
            await ShowBackupMessageAsync(title, string.Format(_loc.Get("DevUninstall_Failed"), ex.Message));
        }
        finally
        {
            _devUninstallBusy = false;
            DevUninstallBtn.IsEnabled = true;
        }
    }
}
