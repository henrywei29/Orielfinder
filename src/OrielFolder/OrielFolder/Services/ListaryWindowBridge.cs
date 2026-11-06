using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using OrielFolder.Helpers;

namespace OrielFolder.Services
{
    // A read-only, on-demand path endpoint. No polling, disk access or dialog manipulation.
    public sealed class ListaryWindowBridge : IDisposable
    {
        private static readonly uint QueryMessage = RegisterWindowMessageW(ListaryFolderPath.QueryMessageName);
        private static readonly IntPtr SubclassId = new IntPtr(0x534C);
        private readonly IntPtr _parent;
        private readonly Func<string> _getFolder;
        private readonly SubclassProc _callback;
        private IntPtr _textWindow;

        public ListaryWindowBridge(IntPtr parent, Func<string> getFolder)
        {
            _parent = parent;
            _getFolder = getFolder;
            _callback = HandleMessage;
            _textWindow = CreateWindowExW(0, "STATIC", string.Empty, 0x40000000,
                0, 0, 0, 0, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_textWindow == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (QueryMessage == 0 || !SetWindowSubclass(parent, _callback, SubclassId, IntPtr.Zero))
            {
                DestroyWindow(_textWindow);
                _textWindow = IntPtr.Zero;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private IntPtr HandleMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            IntPtr subclassId, IntPtr data)
        {
            if (message == QueryMessage && _textWindow != IntPtr.Zero)
            {
                try
                {
                    if (SetWindowTextW(_textWindow, ListaryFolderPath.Normalize(_getFolder()))) return _textWindow;
                }
                catch { /* Do not let managed exceptions cross a native window procedure. */ }
                return IntPtr.Zero;
            }
            if (message == 0x0082) Dispose(); // WM_NCDESTROY
            return DefSubclassProc(window, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (_textWindow == IntPtr.Zero) return;
            RemoveWindowSubclass(_parent, _callback, SubclassId);
            DestroyWindow(_textWindow);
            _textWindow = IntPtr.Zero;
        }

        private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            IntPtr subclassId, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessageW(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetWindowTextW(IntPtr window, string text);
        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);
        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, IntPtr id, IntPtr data);
        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, IntPtr id);
        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
