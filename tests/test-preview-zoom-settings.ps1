$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$header = @'
#nullable enable
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OrielFolder.Models;
using OrielFolder.Services;
using Windows.Storage;
namespace Windows.Storage {
    public class ApplicationDataContainer { public Dictionary<string,object?> Values { get; } = new(); }
    public class ApplicationData {
        public static ApplicationData Current { get; } = new();
        public ApplicationDataContainer LocalSettings { get; } = new();
    }
}
namespace OrielFolder.Helpers { public static class DebugLogger { public static void Log(string message) {} } }
'@
$sources = foreach ($file in @('Services\ISettingsService.cs', 'Services\SettingsService.cs',
    'Services\PreviewLabelPresets.cs', 'Services\PreviewToolbarOrder.cs', 'Services\ConfigurationBackupService.cs', 'Services\LocalizationData.cs',
    'Models\ViewMode.cs', 'Models\TabStateDto.cs', 'Models\WorkspaceDto.cs')) {
    $code = [regex]::Replace([IO.File]::ReadAllText((Join-Path $root $file)), '(?m)^using .*;\r?\n', '')
    if ($code -match '(?m)^namespace ([\w.]+);') {
        $code = [regex]::Replace($code, '(?m)^namespace ([\w.]+);', 'namespace $1 {') + "`n}"
    }
    $code
}
$driver = @'
public static class PreviewZoomSettingsTests {
    public static async Task Run() {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var property = typeof(SettingsService).GetProperty("PreviewZoomModifier");
        Check(property != null, "Preview zoom modifier setting is missing.");
        Check(typeof(ISettingsService).GetProperty("PreviewZoomModifier") != null
            || typeof(IBrowsingSettings).GetProperty("PreviewZoomModifier") != null,
            "Preview zoom modifier is unavailable to settings consumers.");
        var settings = new SettingsService();
        int Read() => (int)property!.GetValue(settings)!;
        void Write(int value) => property!.SetValue(settings, value);
        Check(Read() == 0, "Preview wheel zoom must default to Ctrl for existing users.");
        var changes = new List<(string Key, object? Value)>();
        settings.SettingChanged += (key, value) => changes.Add((key, value));
        Write(1); Write(1); Write(2);
        Check((int)property!.GetValue(new SettingsService())! == 2, "Chosen modifier was not persisted.");
        Check(changes.Count == 2 && changes[0].Key == "PreviewZoomModifier" && changes[0].Value is int first && first == 1
            && changes[1].Key == "PreviewZoomModifier" && changes[1].Value is int second && second == 2,
            "Changed modifiers must notify once with their stored integer.");
        Write(-1); Check(Read() == 0, "Negative runtime modifier must clamp to Ctrl.");
        Write(99); Check(Read() == 2, "High runtime modifier must clamp to Shift.");
        settings.Set("PreviewZoomModifier", -50); Check(Read() == 0, "Corrupt persisted modifier must clamp on read.");
        settings.Set("PreviewZoomModifier", 50); Check(Read() == 2, "High persisted modifier must clamp on read.");
        settings.Set("PreviewZoomModifier", "Alt"); Check(Read() == 0, "Incorrect persisted type must use Ctrl.");

        foreach (int modifier in new[] { 0, 1, 2 }) {
            var json = ConfigurationBackupService.Export(new Dictionary<string,object> { ["PreviewZoomModifier"] = modifier }, new());
            var backup = ConfigurationBackupService.Parse(json);
            Check(backup.Preferences.TryGetValue("PreviewZoomModifier", out var stored) && stored.GetInt32() == modifier,
                "Backup excluded or changed chosen preview zoom modifier.");
            var target = new Dictionary<string,object>();
            await ConfigurationBackupService.ApplyAsync(backup, target, _ => Task.CompletedTask);
            Check(target["PreviewZoomModifier"] is int value && value == modifier, "Imported modifier lost its integer type.");
        }
        foreach (object invalid in new object[] { -1, 3, 99, "1", 1.5, true }) {
            var backup = new ConfigurationBackupService.Backup { Format = "SPAN Dev Configuration", Version = 1 };
            backup.Preferences["Theme"] = JsonSerializer.SerializeToElement("dark");
            backup.Preferences["PreviewZoomModifier"] = JsonSerializer.SerializeToElement(invalid);
            var target = new Dictionary<string,object> { ["Theme"] = "light", ["PreviewZoomModifier"] = 1 };
            bool rejected = false;
            try { await ConfigurationBackupService.ApplyAsync(backup, target, _ => Task.CompletedTask); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && (string)target["Theme"] == "light" && (int)target["PreviewZoomModifier"] == 1,
                "Invalid imported modifier must reject the whole file before any writes: " + invalid);
        }
        var legacy = ConfigurationBackupService.Parse("{\"format\":\"SPAN Dev Configuration\",\"version\":1,\"preferences\":{},\"workspaces\":[]}");
        var existing = new Dictionary<string,object> { ["PreviewZoomModifier"] = 1 };
        await ConfigurationBackupService.ApplyAsync(legacy, existing, _ => Task.CompletedTask);
        Check((int)existing["PreviewZoomModifier"] == 1, "Older backups must preserve an existing modifier choice.");
        foreach (string key in new[] { "Settings_PreviewZoomModifier", "Settings_PreviewZoomModifierDesc", "Settings_MouseWheel" }) {
            var entries = LocalizationService.Entries.Where(entry => entry.key == key).ToArray();
            Check(entries.Length == 1, "Preview zoom label must have exactly one localization entry: " + key);
            var entry = entries[0];
            Check(new[] { entry.en, entry.ko, entry.ja, entry.zhHans, entry.zhHant, entry.de, entry.es, entry.fr, entry.ptBR }
                .All(text => !string.IsNullOrWhiteSpace(text)), "Missing preview zoom translation: " + key);
        }
    }
}
'@
Add-Type -TypeDefinition ($header + ($sources -join "`n") + $driver)
$null = [PreviewZoomSettingsTests]::Run().GetAwaiter().GetResult()
Write-Output 'PASS: preview modifier default, persistence, events, runtime normalization, portable round trip and strict pre-write import validation'

$xaml = [xml][IO.File]::ReadAllText((Join-Path $root 'Views\SettingsModeView.xaml'))
$names = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
$names.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$combo = $xaml.SelectSingleNode('//*[@x:Name="GeneralSection"]//*[@x:Name="PreviewZoomModifierCombo"]', $names)
if (-not $combo -or @($combo.ChildNodes | Where-Object LocalName -eq 'ComboBoxItem').Count -ne 3) {
    throw 'Preview settings must offer Ctrl, Alt and Shift wheel zoom choices.'
}
$items = @($combo.ChildNodes | Where-Object LocalName -eq 'ComboBoxItem')
if ($combo.GetAttribute('SelectedIndex') -ne '0') { throw 'Preview zoom chooser must initially select Ctrl.' }
$keys = @('Ctrl', 'Alt', 'Shift')
for ($i = 0; $i -lt $keys.Count; $i++) {
    if (-not $items[$i].GetAttribute('Content').StartsWith($keys[$i] + ' + ')) {
        throw 'Preview zoom chooser option order does not match stored modifier values.'
    }
}
Write-Output 'PASS: preview modifier chooser is valid XAML in General settings'
