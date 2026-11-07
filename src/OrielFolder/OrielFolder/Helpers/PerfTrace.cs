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
            s_winRows++; // rows realized (phase 0) inside the current frame-watch window

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

        // Debug-only frame watch: while armed, every frame of 50 ms or more is logged as
        // [Perf] FrameGap with the GC pause and Gen2 count inside it, and the window ends with a
        // [Perf] FrameWindow summary. Watching subscribes CompositionTarget.Rendering, which keeps
        // frames coming, so it is armed only for a short window (new tab from a redirect).
        private static long s_watchUntil;
        private static long s_lastFrame;
        private static bool s_frameHooked;
        private static TimeSpan s_lastPause;
        private static int s_lastGen2, s_lastGen0;
        private static long s_winGapSum, s_winGc, s_winGen2, s_winFrames, s_winMax;
        private static int s_winRows;

        [Conditional("DEBUG")]
        public static void WatchFrames(int durationMs)
        {
            FlushWatch();
            s_watchUntil = Now() + durationMs * Stopwatch.Frequency / 1000;
            s_lastFrame = Now();
            s_lastPause = GC.GetTotalPauseDuration();
            s_lastGen2 = GC.CollectionCount(2); s_lastGen0 = GC.CollectionCount(0);
            s_winGapSum = s_winGc = s_winGen2 = s_winFrames = s_winMax = 0;
            s_winRows = 0;
            if (!s_frameHooked)
            {
                s_frameHooked = true;
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnWatchFrame;
            }
        }

        private static void FlushWatch()
        {
            if (s_winFrames == 0) return;
            DebugLogger.Log($"[Perf] FrameWindow frames={s_winFrames} slowSum={s_winGapSum}ms maxGap={s_winMax}ms gcPause={s_winGc}ms gen2={s_winGen2} rowsRealized={s_winRows} ws={Environment.WorkingSet / 1048576}MB heapMB={GC.GetTotalMemory(false) / 1048576}");
            s_winFrames = 0;
        }

        private static void OnWatchFrame(object? sender, object e)
        {
            var now = Now();
            if (now > s_watchUntil)
            {
                FlushWatch();
                s_frameHooked = false;
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnWatchFrame;
                return;
            }
            var gap = (now - s_lastFrame) * 1000 / Stopwatch.Frequency;
            s_lastFrame = now;
            s_winFrames++;
            var pause = GC.GetTotalPauseDuration();
            int g2 = GC.CollectionCount(2), g0 = GC.CollectionCount(0);
            var gcMs = (long)(pause - s_lastPause).TotalMilliseconds;
            s_winGc += gcMs; s_winGen2 += g2 - s_lastGen2;
            if (gap >= 50)
            {
                s_winGapSum += gap;
                if (gap > s_winMax) s_winMax = gap;
                DebugLogger.Log($"[Perf] FrameGap {gap}ms ending @{SinceProcessStartMs}ms gcPause={gcMs}ms gen2={g2 - s_lastGen2} gen0={g0 - s_lastGen0} heapMB={GC.GetTotalMemory(false) / 1048576}");
            }
            s_lastPause = pause; s_lastGen2 = g2; s_lastGen0 = g0;
        }

        public static void Mark(string name)
            => DebugLogger.Log($"[Perf] {name} @{SinceProcessStartMs}ms");

        public static void Span(string name, long startTimestamp, string? detail = null)
            => DebugLogger.Log(detail == null
                ? $"[Perf] {name} {ElapsedMs(startTimestamp)}ms"
                : $"[Perf] {name} {ElapsedMs(startTimestamp)}ms {detail}");
    }
}
