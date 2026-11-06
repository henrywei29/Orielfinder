using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OrielFolder.Services;

/// <summary>Owns shell COM calls, their message pump, and their lifetime on one STA.</summary>
internal sealed class ShellStaWorker : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly Queue<Action> _pending = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _sync = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopping;

    private ShellStaWorker(SemaphoreSlim slots) => _slots = slots;

    public Task Completion => _completion.Task;

    public static async Task<ShellStaWorker?> TryStartAsync(SemaphoreSlim slots, int waitMs,
        CancellationToken cancellationToken = default)
    {
        if (!await slots.WaitAsync(waitMs, cancellationToken).ConfigureAwait(false)) return null;
        var worker = new ShellStaWorker(slots);
        try
        {
            var thread = new Thread(worker.Run) { IsBackground = true, Name = "OrielFolder Shell STA" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return worker;
        }
        catch
        {
            worker._wake.Dispose();
            slots.Release();
            throw;
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken = default)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_stopping) return Task.FromException<T>(new ObjectDisposedException(nameof(ShellStaWorker)));
            _pending.Enqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.TrySetCanceled(cancellationToken);
                    return;
                }
                try { result.TrySetResult(callback()); }
                catch (Exception ex) { result.TrySetException(ex); }
            });
            _wake.Set();
        }
        return result.Task;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_stopping) return;
            _stopping = true;
            _wake.Set();
        }
    }

    private void Run()
    {
        // OleInitialize also initializes STA COM for shell extensions using OLE.
        int initialized = OleInitialize(IntPtr.Zero);
        try
        {
            var handles = new[] { _wake.SafeWaitHandle.DangerousGetHandle() };
            while (true)
            {
                while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 1))
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }

                Action? action;
                lock (_sync)
                {
                    action = _pending.Count > 0 ? _pending.Dequeue() : null;
                    if (action == null && _stopping) break;
                }
                if (action != null) action();
                else MsgWaitForMultipleObjectsEx(1, handles, uint.MaxValue, 0x04FF, 0x0004);
            }
        }
        finally
        {
            if (initialized >= 0) OleUninitialize();
            lock (_sync) _stopping = true;
            _wake.Dispose();
            // A timed-out caller cannot release this slot while native shell code still runs.
            _slots.Release();
            _completion.TrySetResult();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")]
    private static extern void OleUninitialize();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles,
        uint milliseconds, uint wakeMask, uint flags);
}
