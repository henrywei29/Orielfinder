$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'
$viewSource = [IO.File]::ReadAllText((Join-Path $root 'ViewModels\MainViewModel.ViewMode.cs'))
$settingsSource = [IO.File]::ReadAllText((Join-Path $root 'Services\SettingsService.cs'))
if ($viewSource -notmatch 'SwitchViewMode\(ViewMode mode, bool rememberSelection = false\)') { throw 'FAIL: folder view choices cannot be distinguished from programmatic navigation/restoration.' }
$accessor = [regex]::Match($settingsSource, '(?sm)^    public int LastUsedFolderViewMode\r?\n.*?^    }').Value
if (-not $accessor) { throw 'FAIL: last chosen folder mode is not persisted as a separate preference.' }

# Use the existing actual-method tab/workspace harness; replace only external settings storage.
$adapterTest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test-workspace-vm-adapter.ps1'))
$prefix = $adapterTest.Substring(0, $adapterTest.IndexOf("`nAdd-Type -TypeDefinition"))
$prefix = $prefix.Replace("'`$PSScriptRoot'", "'__SCRIPT_ROOT_LITERAL__'")
$prefix = $prefix.Replace('$PSScriptRoot', "'$($PSScriptRoot.Replace("'", "''"))'")
$prefix = $prefix.Replace('__SCRIPT_ROOT_LITERAL__', '$PSScriptRoot')
Invoke-Expression $prefix
$enumSource = [IO.File]::ReadAllText((Join-Path $root 'Models\ViewMode.cs'))
$realEnum = [regex]::Match($enumSource, '(?sm)public enum ViewMode\s*\{.*?^    }').Value
$hostPrefix = [regex]::Replace($hostPrefix, 'public enum ViewMode \{[^}]*\}', $realEnum)
$hostPrefix = $hostPrefix.Replace('public int LastUsedFolderViewMode { get; set; }', '')
$hostPrefix = $hostPrefix.Replace('public int DefaultViewMode => 0;', @"
public int DefaultViewMode { get => Get("DefaultViewMode", 0); set => Set("DefaultViewMode", value); }
public static Dictionary<string,object> Preferences = new();
public T Get<T>(string key, T fallback) => Preferences.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
public void Set<T>(string key, T value) => Preferences[key] = value!;
$accessor
"@)
Add-Type -TypeDefinition ($hostPrefix + $rightProperty.Value + ($methods -join "`n") + ($extraMethods -join "`n") + $switchMode + $resolveMode + "`n} }`n" + $geometry + $model + $tabModel + $adapter)

$settings = [OrielFolder.Services.SettingsService]::new()
$settings.DefaultViewMode = 4
$settings.LastUsedFolderViewMode = [int][OrielFolder.Models.ViewMode]::List
$vm = [OrielFolder.ViewModels.MainViewModel]::new()
$vm.AddNewTab()
$vm.ActiveTab.PreferredViewMode = [OrielFolder.Models.ViewMode]::Details
if ($vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::List) { throw 'FAIL: follow last did not override stale Home preferences.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::IconLarge, $true)
if ($settings.LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::IconLarge) { throw 'FAIL: explicit icon-size choice was not remembered.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Details)
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Home, $true)
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Settings, $true)
if ($settings.LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::IconLarge) { throw 'FAIL: navigation or virtual view overwrote the last user choice.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Details, $true)
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Details, $true)
if ([OrielFolder.Services.SettingsService]::new().LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::Details) { throw 'FAIL: a new settings instance cannot recover the last choice.' }
Write-Output 'PASS: explicit folder choices persist, follow last overrides stale Home preference, navigation and special views do not overwrite it'

$vm.IsSplitViewEnabled = $true
$vm.ActivePane = [OrielFolder.Models.ActivePane]::Right
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::List, $true)
if ($settings.LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::List) { throw 'FAIL: right-pane choice was not remembered.' }
$vm.IsSplitViewEnabled = $false
$pane = [OrielFolder.Models.WorkspacePaneState]::new($vm.CreateWorkspaceExplorer(''), [OrielFolder.Models.ViewMode]::Details)
$vm.ActiveTab.WorkspaceLayout = [OrielFolder.Models.WorkspaceLayoutState]::new()
$vm.ActiveTab.WorkspaceLayout.Add($pane.Explorer, $pane.Mode) | Out-Null
$pane = $vm.ActiveTab.WorkspaceLayout.Panes[0]
$vm.ActivateWorkspacePane($pane)
if ($settings.LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::List -or $vm.CurrentViewMode -ne [OrielFolder.Models.ViewMode]::Details) { throw 'FAIL: pane activation changed the global choice or lost its saved mode.' }
$vm.SwitchViewMode([OrielFolder.Models.ViewMode]::Home)
if ($vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::List) { throw 'FAIL: free-pane Home navigation does not follow the last choice.' }
$settings.DefaultViewMode = 1
if ($vm.ResolveViewModeFromHome() -ne [OrielFolder.Models.ViewMode]::Details) { throw 'FAIL: fixed defaults changed existing saved free-pane restoration.' }
Write-Output 'PASS: right-pane selections are remembered while activating saved panes preserves their mode; fixed defaults retain prior behavior'

foreach ($invalid in @([OrielFolder.Models.ViewMode]::Home, [OrielFolder.Models.ViewMode]::Settings, [OrielFolder.Models.ViewMode]::RecycleBin, 999)) {
    [OrielFolder.Services.SettingsService]::Preferences['LastUsedFolderViewMode'] = [int]$invalid
    if ($settings.LastUsedFolderViewMode -ne [int][OrielFolder.Models.ViewMode]::MillerColumns) { throw 'FAIL: invalid or virtual persisted modes were accepted.' }
}
Write-Output 'PASS: invalid or special persisted folder modes safely fall back to Miller Columns'
