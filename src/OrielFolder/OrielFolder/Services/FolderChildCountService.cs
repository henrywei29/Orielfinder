using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OrielFolder.Services;

internal static class FolderChildCountService
{
    // Shared across windows. Waiting rows do not occupy ThreadPool workers.
    private static readonly SemaphoreSlim Slots = new(2, 2);

    internal sealed record Snapshot(string Path, bool ShowHidden, DateTime LastWriteTimeUtc, int Count);

    internal static async Task<Snapshot?> ReadAsync(string path, bool showHidden, Snapshot? previous, CancellationToken token)
    {
        await Slots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var directory = new DirectoryInfo(path);
                directory.Refresh();
                if (!directory.Exists) return null;
                var modified = directory.LastWriteTimeUtc;
                if (previous != null && previous.ShowHidden == showHidden
                    && string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase)
                    && previous.LastWriteTimeUtc == modified)
                    return previous;

                int count = 0;
                foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = false,
                    AttributesToSkip = Helpers.FileVisibility.AttributesToSkip(showHidden)
                }))
                {
                    token.ThrowIfCancellationRequested();
                    count++;
                }
                token.ThrowIfCancellationRequested();
                return new Snapshot(path, showHidden, modified, count);
            }, token).ConfigureAwait(false);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        finally { Slots.Release(); }
    }
}
