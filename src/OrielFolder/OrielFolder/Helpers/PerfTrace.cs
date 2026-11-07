using System;
using System.Diagnostics;

namespace OrielFolder.Helpers
{
    /// <summary>
    /// Startup / navigation timing marks written to the debug log as <c>[Perf]</c> lines.
    /// Times are milliseconds since the OS created this process, so a mark taken in
    /// <c>Program.Main</c> already includes runtime/host start-up.
    /// </summary>
    public static class PerfTrace
    {
        private static readonly Stopwatch s_clock = Stopwatch.StartNew();
        private static readonly double s_offsetMs = ComputeProcessOffsetMs();

        private static double ComputeProcessOffsetMs()
        {
            try
            {
                var started = Process.GetCurrentProcess().StartTime;
                var offset = (DateTime.Now - started).TotalMilliseconds;
                return offset is > 0 and < 120_000 ? offset : 0;
            }
            catch { return 0; }
        }

        /// <summary>Milliseconds since process creation.</summary>
        public static long SinceProcessStartMs => (long)(s_offsetMs + s_clock.Elapsed.TotalMilliseconds);

        /// <summary>Monotonic timestamp for measuring spans with <see cref="ElapsedMs"/>.</summary>
        public static long Now() => Stopwatch.GetTimestamp();

        public static long ElapsedMs(long startTimestamp)
            => (long)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);

        private static long s_pendingFirstRow;

        /// <summary>Arms <see cref="FirstRow"/>: the next realized row logs the time since <paramref name="loadStart"/>.</summary>
        public static void ArmFirstRow(long loadStart) => System.Threading.Interlocked.Exchange(ref s_pendingFirstRow, loadStart);

        /// <summary>Called when a view realizes a row; logs once per armed load.</summary>
        public static void FirstRow(string view)
        {
            var start = System.Threading.Interlocked.Exchange(ref s_pendingFirstRow, 0);
            if (start != 0) DebugLogger.Log($"[Perf] FolderLoad.FirstRow {ElapsedMs(start)}ms view={view} @{SinceProcessStartMs}ms");
        }

        /// <summary>
        /// End of FolderViewModel.EnsureChildrenLoadedAsync: logs the load and arms <see cref="FirstRow"/>.
        /// <paramref name="cacheCheckMs"/> includes the wait for the UI thread after the cache probe.
        /// </summary>
        public static void FolderLoaded(long loadStart, long cacheCheckMs, bool fromCache, int items, string path)
        {
            Span("FolderLoad", loadStart,
                $"source={(fromCache ? "cache" : "disk")} cacheCheck={cacheCheckMs}ms items={items} path='{path}'");
            ArmFirstRow(loadStart);
        }

        /// <summary>
        /// Debug-only: pings the UI thread every 50 ms from a background thread and logs
        /// each stall (dispatch latency) of 150 ms or more as <c>[Perf] UiStall</c>.
        /// </summary>
        [Conditional("DEBUG")]
        public static void StartUiStallMonitor(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
        {
            var thread = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(50);
                    var sent = Now();
                    using var done = new System.Threading.ManualResetEventSlim(false);
                    if (!dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => done.Set()))
                        return;
                    if (!done.Wait(60_000)) return;
                    var latency = ElapsedMs(sent);
                    if (latency >= 150)
                        DebugLogger.Log($"[Perf] UiStall {latency}ms ending @{SinceProcessStartMs}ms");
                }
            })
            { IsBackground = true, Name = "PerfTrace.UiStallMonitor" };
            thread.Start();
        }

        public static void Mark(string name)
            => DebugLogger.Log($"[Perf] {name} @{SinceProcessStartMs}ms");

        public static void Span(string name, long startTimestamp, string? detail = null)
            => DebugLogger.Log(detail == null
                ? $"[Perf] {name} {ElapsedMs(startTimestamp)}ms"
                : $"[Perf] {name} {ElapsedMs(startTimestamp)}ms {detail}");
    }
}
