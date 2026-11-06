using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OrielFolder.Helpers;

/// <summary>Route direct development EXE launches through the registered package's settings identity.</summary>
internal static class DevelopmentPackageActivation
{
    private static string RegisteredAppId()
    {
        try
        {
            var packages = new Windows.Management.Deployment.PackageManager()
                .FindPackagesForUser("", "LocalDevelopment.OrielFolderDev", "CN=OrielFolder Dev");
            foreach (var package in packages)
                if (!package.IsFramework && !package.IsResourcePackage)
                    return package.Id.FamilyName + "!App";
        }
        catch (UnauthorizedAccessException)
        {
            return "LocalDevelopment.OrielFolderDev_tvznpbczqragy!App";
        }
        throw new InvalidOperationException("Please register OrielFolder Dev via start-dev.ps1 before launching it.");
    }

    public static bool EnsurePackageIdentity()
    {
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        if (result == 122) return true; // ERROR_INSUFFICIENT_BUFFER: identity exists.
        if (result != 15700) throw new Win32Exception(result); // APPMODEL_ERROR_NO_PACKAGE

        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            // Preserve the raw command line, including quoted drive roots and paths with spaces.
            string commandLine = Marshal.PtrToStringUni(GetCommandLineW()) ?? "";
            uint processId;
            int hr = manager.ActivateApplication(RegisteredAppId(), ArgumentsAfterExecutable(commandLine),
                2 /* AO_NOERRORUI: show our actionable error instead */, out processId);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally { Marshal.ReleaseComObject(manager); }
        return false;
    }

    internal static string ArgumentsAfterExecutable(string commandLine)
    {
        commandLine = commandLine.TrimStart();
        if (commandLine.Length == 0) return "";
        int end = commandLine[0] == '"'
            ? commandLine.IndexOf('"', 1) + 1
            : commandLine.IndexOfAny(new[] { ' ', '\t' });
        return end <= 0 ? "" : commandLine.Substring(end).TrimStart();
    }

    public static void ShowLaunchError(Exception error)
    {
        MessageBoxW(IntPtr.Zero,
            "无法启动已注册的 OrielFolder Dev 开发版，程序已停止启动，避免设置只保存在内存中。\n" +
            "请先运行 start-dev.ps1 注册并启动开发版。\n\n" +
            "Cannot activate the registered OrielFolder Dev package. Error: 0x" + error.HResult.ToString("X8"),
            "OrielFolder Dev", 0x10);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr fullName);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCommandLineW();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
}
