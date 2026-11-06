param(
    [ValidateSet('write','read')][string]$Mode,
    [ValidatePattern('^[a-f0-9]{32}$')][string]$RunId
)
$ErrorActionPreference = 'Stop'
$resultPath = "D:\codex\Orielfolder-development\settings-probe-$RunId-$Mode.json"
try {
    Add-Type -TypeDefinition @'
public static class SettingsValuesProbe {
    public static void Check(object values, bool write) {
        var map = (System.Collections.Generic.IDictionary<string, object>)values;
        if (write) {
            map["Theme"] = "dark";
            map["OnboardingCompleted"] = true;
            map["Spacing"] = 7.5;
        } else if (!map.ContainsKey("Theme") || !object.Equals(map["Theme"], "dark") ||
            !object.Equals(map["OnboardingCompleted"], true) || !object.Equals(map["Spacing"], 7.5)) {
            throw new System.Exception("Settings did not survive process exit.");
        }
    }
}
'@
    $package = [Windows.ApplicationModel.Package,Windows.ApplicationModel,ContentType=WindowsRuntime]::Current
    if ($package.Id.FamilyName -ne 'LocalDevelopment.OrielFolderDev_tvznpbczqragy') { throw 'Wrong test package identity.' }
    $data = [Windows.Storage.ApplicationData,Windows.Storage,ContentType=WindowsRuntime]::Current
    $root = $data.LocalSettings
    $containerName = 'PersistenceProbe-' + $RunId
    $disposition = if ($Mode -eq 'write') { [Windows.Storage.ApplicationDataCreateDisposition]::Always } else { [Windows.Storage.ApplicationDataCreateDisposition]::Existing }
    $store = $root.CreateContainer($containerName, $disposition)
    [SettingsValuesProbe]::Check($store.Values, ($Mode -eq 'write'))
    [pscustomobject]@{Passed=$true;Mode=$Mode;ProcessId=$PID;CorrectPackage=$true} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
} catch {
    [pscustomobject]@{Passed=$false;Mode=$Mode;ProcessId=$PID;ErrorType=$_.Exception.GetType().FullName;Error=$_.Exception.Message} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
    exit 1
}
