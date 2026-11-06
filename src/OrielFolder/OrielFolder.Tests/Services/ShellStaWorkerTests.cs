using System.Runtime.InteropServices;
using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class ShellStaWorkerTests
{
    [TestMethod]
    public async Task StalledNativeWorkDoesNotBlockCallerOrReleaseItsSlotEarly()
    {
        using var slots = new SemaphoreSlim(1);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = await ShellStaWorker.TryStartAsync(slots, 1000);
        Assert.IsNotNull(worker);
        var work = worker.InvokeAsync(() =>
        {
            entered.SetResult();
            release.Wait();
            return 17;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () =>
                await work.WaitAsync(TimeSpan.FromMilliseconds(30)));
            worker.Dispose();
            Assert.IsFalse(worker.Completion.IsCompleted);
            Assert.IsNull(await ShellStaWorker.TryStartAsync(slots, 30));
        }
        finally { release.Set(); }
        Assert.AreEqual(17, await work.WaitAsync(TimeSpan.FromSeconds(3)));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        using var next = await ShellStaWorker.TryStartAsync(slots, 1000);
        Assert.IsNotNull(next);
        next.Dispose();
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task IdleStaDispatchesNativeWindowMessages()
    {
        using var slots = new SemaphoreSlim(1);
        using var worker = await ShellStaWorker.TryStartAsync(slots, 1000);
        Assert.IsNotNull(worker);
        IntPtr window = await worker.InvokeAsync(() => CreateWindowExW(
            0, "STATIC", "OrielFolder shell STA test", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        Assert.AreNotEqual(IntPtr.Zero, window);
        try
        {
            // SendMessageTimeout needs the owning thread to dispatch this message.
            var delivered = await Task.Run(() => SendMessageTimeoutW(window, 0,
                IntPtr.Zero, IntPtr.Zero, 2, 1000, out _));
            Assert.AreNotEqual(IntPtr.Zero, delivered, "The idle STA did not pump WM_NULL.");
        }
        finally { await worker.InvokeAsync(() => DestroyWindow(window)); }
        worker.Dispose();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task CancelledQueuedCommandNeverRunsAndCleanupKeepsItsApartment()
    {
        using var slots = new SemaphoreSlim(1);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = await ShellStaWorker.TryStartAsync(slots, 1000);
        Assert.IsNotNull(worker);
        var first = worker.InvokeAsync(() =>
        {
            entered.SetResult();
            release.Wait();
            return Environment.CurrentManagedThreadId;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        bool staleCommandRan = false;
        var cancelled = worker.InvokeAsync(() => staleCommandRan = true, cancellation.Token);
        cancellation.Cancel();
        var cleanup = worker.InvokeAsync(() => (
            Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        worker.Dispose();
        release.Set();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await cancelled);
        var ownerThread = await first.WaitAsync(TimeSpan.FromSeconds(3));
        var disposed = await cleanup.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsFalse(staleCommandRan);
        Assert.AreEqual(ownerThread, disposed.Item1);
        Assert.AreEqual(ApartmentState.STA, disposed.Item2);
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () =>
            await worker.InvokeAsync(() => true));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title,
        uint style, int x, int y, int width, int height, IntPtr parent,
        IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
