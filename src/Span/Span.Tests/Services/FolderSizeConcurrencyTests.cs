using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FolderSizeConcurrencyTests
{
    [TestMethod]
    public async Task ManyRequestsHaveAtMostTwoWorkersAndQueuedCancellationReleasesResources()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, peak = 0, runs = 0;
        var service = new FolderSizeService((_, token) =>
        {
            int count = Interlocked.Increment(ref active);
            Interlocked.Increment(ref runs);
            int old;
            do { old = Volatile.Read(ref peak); } while (count > old && Interlocked.CompareExchange(ref peak, count, old) != old);
            if (count == 2) started.TrySetResult();
            try { release.Wait(token); return 5; }
            finally { Interlocked.Decrement(ref active); }
        });
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0;
        service.SizeCalculated += (_, _) => { if (Interlocked.Increment(ref completed) == 2) complete.TrySetResult(); };
        try
        {
            service.RequestCalculation("A"); service.RequestCalculation("B");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for (int i = 0; i < 100; i++) service.RequestCalculation("queued/" + i);
            for (int i = 0; i < 100; i++) service.CancelCalculation("queued/" + i);
            release.Set();
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(2, peak);
            Assert.AreEqual(2, runs, "Cancelled queued requests must never enumerate directories");
        }
        finally { release.Set(); service.CancelAll(); }
    }
}
