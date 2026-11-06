using System.Runtime.InteropServices;

namespace OrielFolder.Helpers;

internal static class DesktopShellIdentity
{
    internal const string AppId = "OrielFolder.Desktop.Win32";

    internal static void Initialize()
    {
        int result = SetCurrentProcessExplicitAppUserModelID(AppId);
        if (result < 0) DebugLogger.Log($"[Shell] AppUserModelID failed (0x{result:X8})");
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
