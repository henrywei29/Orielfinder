$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$settings = [IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\Services\SettingsService.cs'))
$settings = $settings.Replace('namespace Span.Services;', 'namespace Span.Services {') + "`n}"
$translations = [IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\Services\LocalizationData.cs'))
$collaborators = @'
namespace Windows.Storage {
    public class ApplicationDataContainer {
        public System.Collections.Generic.Dictionary<string, object?> Values { get; } = new();
    }
    public class ApplicationData {
        public static ApplicationData Current { get; } = new();
        public ApplicationDataContainer LocalSettings { get; } = new();
    }
}
namespace Span.Helpers { public static class DebugLogger { public static void Log(string text) {} } }
namespace Span.Services {
    public interface ISettingsService {}
    public partial class LocalizationService {
        public static void VerifyWorkspaceTranslations() {
            var keys = new[] { "Settings_MultiPaneLayout", "Settings_MultiPaneLayoutDesc",
                "Settings_MultiPaneClassic", "Settings_MultiPaneTiled", "Settings_MultiPaneFloating",
                "Workspace_AddPane", "Workspace_Horizontal", "Workspace_Vertical", "Workspace_Grid" };
            foreach (var key in keys) {
                var count = 0;
                foreach (var entry in Entries) {
                    if (entry.key != key) continue;
                    count++;
                    foreach (var value in new[] { entry.en, entry.ko, entry.ja, entry.zhHans,
                        entry.zhHant, entry.de, entry.es, entry.fr, entry.ptBR })
                        if (string.IsNullOrWhiteSpace(value)) throw new System.Exception("Missing translation: " + key);
                }
                if (count != 1) throw new System.Exception("Expected one translation entry: " + key);
            }
        }
    }
    public static class WorkspaceSettingsProbe {
        public static void Verify() {
            var settings = new SettingsService();
            if (settings.MultiPaneLayoutMode != 0 || settings.MultiPaneTileArrangement != 2)
                throw new System.Exception("Workspace defaults must preserve classic layout and grid arrangement.");
            var changes = new System.Collections.Generic.List<string>();
            settings.SettingChanged += (key, value) => changes.Add(key);
            settings.MultiPaneLayoutMode = 2;
            settings.MultiPaneLayoutMode = 2;
            settings.MultiPaneTileArrangement = 0;
            var restored = new SettingsService();
            if (restored.MultiPaneLayoutMode != 2 || restored.MultiPaneTileArrangement != 0)
                throw new System.Exception("Workspace settings were not retained.");
            if (changes.Count != 2 || changes[0] != "MultiPaneLayoutMode" || changes[1] != "MultiPaneTileArrangement")
                throw new System.Exception("Workspace setting changes must notify once per changed value.");
        }
    }
}
'@
$viewModes = [IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\Models\ViewMode.cs'))
Add-Type -TypeDefinition ("#nullable enable annotations`n#nullable disable warnings`n" + $settings + "`n" + $translations + "`n" + $collaborators + "`n" + $viewModes)
[Span.Services.WorkspaceSettingsProbe]::Verify()
Write-Output 'PASS: production settings defaults, persistence and change notifications'
[Span.Services.LocalizationService]::VerifyWorkspaceTranslations()
Write-Output 'PASS: nine workspace labels each have nine nonempty translations and unique keys'

$xaml = [xml][IO.File]::ReadAllText((Join-Path $repo 'src\Span\Span\Views\SettingsModeView.xaml'))
$names = [Xml.XmlNamespaceManager]::new($xaml.NameTable)
$names.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$combo = $xaml.SelectSingleNode('//*[@x:Name="GeneralSection"]//*[@x:Name="MultiPaneLayoutCombo"]', $names)
if (-not $combo) { throw 'Workspace layout chooser must be in General settings.' }
$items = @($combo.ChildNodes | Where-Object { $_.LocalName -eq 'ComboBoxItem' })
if ($items.Count -ne 3 -or $combo.GetAttribute('SelectedIndex') -ne '0') {
    throw 'Workspace layout chooser must offer three layouts and default to classic.'
}
$expected = @('MultiPaneClassic', 'MultiPaneTiled', 'MultiPaneFloating')
for ($i = 0; $i -lt $expected.Count; $i++) {
    if ($items[$i].GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml') -ne $expected[$i]) {
        throw 'Workspace layout option order must match persisted values.'
    }
}
Write-Output 'PASS: General settings XAML is valid and layout options match stored values'
