$ErrorActionPreference = 'Stop'
$program = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder\Program.cs') -Raw
$program = $program.Replace('namespace OrielFolder;', 'namespace OrielFolder {') + "`n}"
$stubs = @'
namespace WinRT { public static class ComWrappersSupport { public static void InitializeComWrappers() {} } }
namespace Microsoft.Windows.AppLifecycle {
    public class AppInstance {
        public static int Registrations;
        public bool IsCurrent => true;
        public static AppInstance FindOrRegisterForKey(string key) { Registrations++; return new(); }
        public static AppInstance GetCurrent() => new();
        public object GetActivatedEventArgs() => new();
        public Pending RedirectActivationToAsync(object args) => new();
    }
    public class Pending { public System.Threading.Tasks.Task AsTask() => System.Threading.Tasks.Task.CompletedTask; }
}
namespace Microsoft.UI.Xaml {
    public static class Application { public static void Start(System.Action<object> action) => action(new()); }
}
namespace Microsoft.UI.Dispatching {
    public class DispatcherQueue { public static DispatcherQueue GetForCurrentThread() => new(); }
    public class DispatcherQueueSynchronizationContext : System.Threading.SynchronizationContext {
        public DispatcherQueueSynchronizationContext(DispatcherQueue queue) {}
    }
}
namespace OrielFolder {
    public class App { public static int Constructions; public App() { Constructions++; } }
}
namespace OrielFolder.Helpers {
    public static class AppIdentity { public const string SingleInstanceKey = "ORIEL_FOLDER_DEV_MAIN"; }
    public static class DevelopmentPackageActivation {
        public static string Mode = "packaged";
        public static int Activations;
        public static bool EnsurePackageIdentity() {
            if (Mode == "packaged") return true;
            if (Mode == "failed") throw new System.InvalidOperationException("Activation failed");
            Activations++; return false;
        }
        public static void ShowLaunchError(System.Exception error) {}
    }
}
'@
Add-Type -TypeDefinition ($program + $stubs)
$entry = [OrielFolder.App].Assembly.GetType('OrielFolder.Program').GetMethod('Main', [Reflection.BindingFlags]'Static,NonPublic')
foreach ($mode in @('unpackaged','failed','packaged')) {
    [OrielFolder.Helpers.DevelopmentPackageActivation]::Mode = $mode
    [OrielFolder.Helpers.DevelopmentPackageActivation]::Activations = 0
    [OrielFolder.App]::Constructions = 0
    [Microsoft.Windows.AppLifecycle.AppInstance]::Registrations = 0
    $exitCode = $entry.Invoke($null, (, [string[]]@()))
    if ($mode -eq 'packaged') {
        if ([OrielFolder.App]::Constructions -ne 1 -or $exitCode -ne 0) { throw 'Packaged startup must construct the app normally.' }
    } else {
        if ([OrielFolder.App]::Constructions -ne 0 -or [Microsoft.Windows.AppLifecycle.AppInstance]::Registrations -ne 0) {
            throw "$mode startup reached the settings/UI and single-instance registration without package identity."
        }
        if ($mode -eq 'unpackaged' -and [OrielFolder.Helpers.DevelopmentPackageActivation]::Activations -ne 1) { throw 'Direct launch was not redirected.' }
        if ($mode -eq 'failed' -and $exitCode -ne 1) { throw 'Activation failure must be reported.' }
    }
    "PASS: $mode startup"
}
