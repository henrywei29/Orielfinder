using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OrielFolder.Services.DevUninstall;

/// <summary>
/// Paths for the Dev uninstall (no I/O). Backups go next to <c>marked-files</c>; caches outside the MSIX
/// package are deleted; <c>marked-files</c> and <c>backups</c> are always protected. The same allow-list is
/// re-checked by the elevated helper script before it deletes anything.
/// </summary>
public sealed class DevUninstallPlan
{
    public const string ListaryPluginsFolderName = "FileAppPlugins";

    public string BaseDirectory { get; }
    public string BackupDirectory { get; }
    public string ConfigurationBackupPath { get; }
    public string RunDirectory { get; }
    public string RegistryBackupDirectory { get; }
    public string PlanPath => Path.Combine(RunDirectory, "plan.json");
    public string ResultPath => Path.Combine(RunDirectory, "result.json");
    public string DataFolderName { get; }
    public string LocalDataRoot { get; }
    public string PreviewCacheRoot { get; }
    public IReadOnlyList<string> FoldersToDelete { get; }
    public IReadOnlyList<string> ProtectedFolders { get; }

    private DevUninstallPlan(string markedOutputRoot, string previewCacheRoot, string localDataRoot,
        string dataFolderName, DateTime now)
    {
        DataFolderName = dataFolderName;
        LocalDataRoot = Normalize(localDataRoot);
        PreviewCacheRoot = Normalize(previewCacheRoot);
        var marked = Normalize(markedOutputRoot);
        BaseDirectory = Path.GetDirectoryName(marked)!;
        BackupDirectory = Path.Combine(BaseDirectory, "backups");
        var stamp = now.ToString("yyyyMMdd-HHmmss");
        ConfigurationBackupPath = Path.Combine(BackupDirectory, $"OrielFolder-config-{stamp}.json");
        RunDirectory = Path.Combine(BackupDirectory, $"uninstall-{stamp}");
        RegistryBackupDirectory = Path.Combine(RunDirectory, "registry");

        var delete = new List<string> { LocalDataRoot };
        if (!IsSameOrUnder(PreviewCacheRoot, LocalDataRoot)) delete.Add(PreviewCacheRoot);
        FoldersToDelete = delete;
        ProtectedFolders = new[] { marked, BackupDirectory };
    }

    public static DevUninstallPlan Create(string markedOutputRoot, string previewCacheRoot, string localDataRoot,
        string dataFolderName, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(dataFolderName) || dataFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid data folder name.", nameof(dataFolderName));
        foreach (var p in new[] { markedOutputRoot, previewCacheRoot, localDataRoot })
            if (string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p))
                throw new ArgumentException("Paths must be absolute.");
        if (!Path.GetFileName(Normalize(localDataRoot)).Equals(dataFolderName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local data root must be the app's own data folder.", nameof(localDataRoot));
        if (!Path.GetFileName(Normalize(previewCacheRoot)).Equals("preview-cache", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unexpected preview cache folder.", nameof(previewCacheRoot));
        return new DevUninstallPlan(markedOutputRoot, previewCacheRoot, localDataRoot, dataFolderName, now);
    }

    /// <summary>True only for the planned cache folders and <c>…\FileAppPlugins\&lt;DataFolderName&gt;</c>.</summary>
    public bool IsAllowedDeletion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        var p = Normalize(path);
        if (ProtectedFolders.Any(k => IsSameOrUnder(p, k) || IsSameOrUnder(k, p))) return false;
        if (FoldersToDelete.Any(d => Same(p, d))) return true;
        var parent = Path.GetDirectoryName(p);
        return parent != null
            && Path.GetFileName(p).Equals(DataFolderName, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(parent).Equals(ListaryPluginsFolderName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrUnder(string path, string root) =>
        Same(path, root) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
