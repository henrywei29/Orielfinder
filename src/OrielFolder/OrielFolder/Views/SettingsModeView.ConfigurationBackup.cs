using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OrielFolder.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace OrielFolder.Views;

public sealed partial class SettingsModeView
{
    private bool _configurationBackupBusy;

    private IntPtr BackupPickerOwner()
    {
        var owner = ((App)App.Current).GetRegisteredWindows().FirstOrDefault(w => w.Content?.XamlRoot == XamlRoot)
            ?? throw new InvalidOperationException("Settings window is unavailable.");
        return WinRT.Interop.WindowNative.GetWindowHandle(owner);
    }

    private async Task<string> CaptureConfigurationAsync()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var preferences = new Dictionary<string, object>();
        foreach (var entry in ConfigurationBackupService.PreferenceTypes)
        {
            var property = typeof(SettingsService).GetProperty(entry.Key);
            if (property?.CanRead == true && property.GetValue(_settings) is object value)
                preferences.Add(entry.Key, value); // Include defaults, so a fresh setup restores faithfully.
            else if (values.TryGetValue(entry.Key, out var stored)) preferences.Add(entry.Key, stored);
        }
        var workspaces = App.Current.Services.GetRequiredService<WorkspaceService>();
        return ConfigurationBackupService.Export(preferences, await workspaces.GetWorkspacesForBackupAsync());
    }

    private void SetBackupBusy(bool busy)
    {
        _configurationBackupBusy = busy;
        ExportConfigurationButton.IsEnabled = ImportConfigurationButton.IsEnabled = !busy;
    }

    private async void OnExportConfiguration(object sender, RoutedEventArgs e)
    {
        if (_configurationBackupBusy || _loc == null) return;
        SetBackupBusy(true);
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "OrielFolder-config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                SettingsIdentifier = "OrielFolderConfigurationBackup"
            };
            picker.FileTypeChoices.Add("OrielFolder configuration", new[] { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, BackupPickerOwner());
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            await ConfigurationBackupService.WriteFileAsync(file.Path, await CaptureConfigurationAsync());
            await ShowBackupMessageAsync(_loc.Get("ConfigBackup_Title"), _loc.Get("ConfigBackup_Exported"));
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ConfigurationBackup] Export failed: {ex.GetType().Name}");
            await ShowBackupMessageAsync(_loc.Get("ConfigBackup_Title"), _loc.Get("ConfigBackup_Failed"));
        }
        finally { SetBackupBusy(false); }
    }

    private async void OnImportConfiguration(object sender, RoutedEventArgs e)
    {
        if (_configurationBackupBusy || _loc == null) return;
        SetBackupBusy(true);
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                SettingsIdentifier = "OrielFolderConfigurationBackup"
            };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, BackupPickerOwner());
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            if ((await file.GetBasicPropertiesAsync()).Size > 8 * 1024 * 1024)
                throw new InvalidDataException("Configuration backup is too large.");
            var backup = ConfigurationBackupService.Parse(await File.ReadAllTextAsync(file.Path));
            var confirm = new ContentDialog
            {
                Title = _loc.Get("ConfigBackup_Import"),
                Content = string.Format(_loc.Get("ConfigBackup_Confirm"), backup.Preferences.Count, backup.Workspaces.Count),
                PrimaryButtonText = _loc.Get("ConfigBackup_Import"), CloseButtonText = _loc.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            string safetyPath = Path.Combine(Path.GetDirectoryName(file.Path)!,
                "OrielFolder-before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            await ConfigurationBackupService.WriteFileAsync(safetyPath, await CaptureConfigurationAsync());
            var workspaces = App.Current.Services.GetRequiredService<WorkspaceService>();
            await ConfigurationBackupService.ApplyAsync(backup, ApplicationData.Current.LocalSettings.Values,
                workspaces.MergeImportedWorkspacesAsync);
            foreach (var window in ((App)App.Current).GetRegisteredWindows().OfType<MainWindow>())
                window.ViewModel.RefreshImportedFavoriteOrder();
            // No setting-change events: cached services adopt the imported state together on restart.
            LoadSettingsToUI();
            await ShowBackupMessageAsync(_loc.Get("ConfigBackup_Title"), string.Format(_loc.Get("ConfigBackup_Imported"), safetyPath));
        }
        catch (InvalidDataException)
        {
            await ShowBackupMessageAsync(_loc.Get("ConfigBackup_Title"), _loc.Get("ConfigBackup_Invalid"));
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ConfigurationBackup] Import failed: {ex.GetType().Name}");
            await ShowBackupMessageAsync(_loc.Get("ConfigBackup_Title"), _loc.Get("ConfigBackup_Failed"));
        }
        finally { SetBackupBusy(false); }
    }
    private async System.Threading.Tasks.Task ShowBackupMessageAsync(string title, string message)
    {
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message,
            CloseButtonText = "OK" }.ShowAsync();
    }
}
