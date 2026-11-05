using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Span.Helpers;

namespace Listary.FileAppPlugin.OrielFolder
{
    public sealed class OrielFolderPlugin : IFileAppPlugin
    {
        public bool IsOpenedFolderProvider => true;
        public bool IsQuickSwitchTarget => false;
        public bool IsSharedAcrossApplications => false;
        public SearchBarType SearchBarType => SearchBarType.Floating;
        public Task<bool> Initialize(IFileAppPluginHost host) => Task.FromResult(true);

        public IFileWindow BindFileWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return null;
            try
            {
                Native.GetWindowThreadProcessId(handle, out var processId);
                using (var process = Process.GetProcessById((int)processId))
                    if (!string.Equals(process.ProcessName, "OrielFolder", StringComparison.OrdinalIgnoreCase)) return null;
                // The dedicated endpoint also distinguishes main windows from dialogs/tool windows.
                return Native.QueryTextWindow(handle) != IntPtr.Zero ? new OrielFolderWindow(handle) : null;
            }
            catch { return null; }
        }
    }

    public sealed class OrielFolderWindow : IFileWindow
    {
        public IntPtr Handle { get; }
        public OrielFolderWindow(IntPtr handle) { Handle = handle; }
        public Task<IFileTab> GetCurrentTab() => Task.FromResult<IFileTab>(new OrielFolderTab(Handle));

    }

    // Listary 6 discovers these roles with mutually exclusive branches, so the tab must be a separate class.
    public sealed class OrielFolderTab : IFileTab, IGetFolder
    {
        private readonly IntPtr _window;
        public OrielFolderTab(IntPtr window) { _window = window; }

        public Task<string> GetCurrentFolder()
        {
            // Bound the request so a busy SPAN UI cannot indefinitely block Listary.
            var textWindow = Native.QueryTextWindow(_window);
            if (textWindow == IntPtr.Zero) return Task.FromResult(string.Empty);
            var text = new StringBuilder(32768);
            if (!Native.ReadWindowText(textWindow, text)) return Task.FromResult(string.Empty);
            return Task.FromResult(ListaryFolderPath.Normalize(text.ToString()));
        }
    }

    internal static class Native
    {
        private static readonly uint QueryMessage = RegisterWindowMessageW(ListaryFolderPath.QueryMessageName);
        internal static IntPtr QueryTextWindow(IntPtr parent)
        {
            if (parent == IntPtr.Zero || QueryMessage == 0) return IntPtr.Zero;
            if (SendMessageTimeoutW(parent, QueryMessage, IntPtr.Zero, IntPtr.Zero, 0x0002, 250, out var result) == IntPtr.Zero)
                return IntPtr.Zero;
            var textWindow = new IntPtr(unchecked((long)result.ToUInt64()));
            return GetParent(textWindow) == parent ? textWindow : IntPtr.Zero;
        }
        internal static bool ReadWindowText(IntPtr window, StringBuilder text) =>
            SendTextMessageTimeout(window, 0x000D, new IntPtr(text.Capacity), text, 0x0002, 250, out _) != IntPtr.Zero;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessageW(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendTextMessageTimeout(IntPtr window, uint message, IntPtr maxCount,
            [Out] StringBuilder text, uint flags, uint timeout, out UIntPtr result);
    }
}
