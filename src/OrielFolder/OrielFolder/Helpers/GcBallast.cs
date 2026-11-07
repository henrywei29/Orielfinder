using System;

namespace OrielFolder.Helpers
{
    /// <summary>
    /// Keeps a large, never-touched byte array alive for the life of the process.
    ///
    /// Why: WinUI reports the native memory of every XAML element it creates through
    /// <c>GC.AddMemoryPressure</c>. The runtime turns that into induced Gen2 collections once
    /// the reported pressure exceeds roughly a third of the managed heap. OrielFolder's managed
    /// heap is small (about 10 MB), so realizing a list's rows triggered one or two Gen2 GCs per
    /// frame, and each one walks every live XAML object, including hidden tabs. Measured with
    /// six System32 tabs (Debug): 18–60 Gen2 GCs and 1.0–1.4 s of GC pause per new tab.
    ///
    /// A large managed object raises that threshold. The array is allocated uninitialized on
    /// the large object heap, which is never compacted, and its pages are never written, so
    /// Windows commits them but does not back them with RAM: the working set does not grow.
    /// Only the commit charge does, which is why the size depends on installed memory.
    /// </summary>
    public static class GcBallast
    {
        private const long MB = 1024L * 1024L;
        private const long GB = 1024L * MB;

        private static byte[]? s_ballast;

        /// <summary>Ballast size for a machine with <paramref name="totalMemoryBytes"/> of memory; 0 = none.</summary>
        public static long ChooseSizeBytes(long totalMemoryBytes) => totalMemoryBytes switch
        {
            >= 8 * GB => 512 * MB,
            >= 4 * GB => 256 * MB,
            _ => 0
        };

        /// <summary>
        /// Allocates the ballast once and returns its size in bytes (0 when skipped or failed).
        /// Safe to call more than once; never throws.
        /// </summary>
        public static long Install()
        {
            if (s_ballast != null) return s_ballast.LongLength;
            try
            {
                long size = ChooseSizeBytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
                if (size > 0) s_ballast = GC.AllocateUninitializedArray<byte>((int)size);
                return s_ballast?.LongLength ?? 0;
            }
            catch (OutOfMemoryException) { return 0; }
        }
    }
}
