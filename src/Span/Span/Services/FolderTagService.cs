using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Span.Helpers;
using Span.Models;

namespace Span.Services;

/// <summary>
/// File and folder tags stored inside SPAN's own data directory. Target items are
/// never modified. Legacy desktop.ini tags are read only on the first uncached lookup.
/// </summary>
public sealed class FolderTagService
{
    private const string StoreFileName = "folder-tags.json";
    private readonly object _sync = new();
    private readonly string _storePath;
    private Dictionary<string, FolderTagColor> _tags = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<FolderTagColor, string> _names = new();
    private readonly Dictionary<string, FolderTagColor> _cache = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;
    public string? LoadError { get; private set; }

    public FolderTagService() : this(AppIdentity.LocalDataRoot) { }

    /// <param name="dataDirectory">Application data directory, independent of tagged folders.</param>
    public FolderTagService(string dataDirectory)
    {
        _storePath = Path.Combine(Path.GetFullPath(dataDirectory), StoreFileName);
        try { (_tags, _names) = ReadStore(); }
        catch (Exception ex) { LoadError = Error(ex); }
    }

    public FolderTagColor GetTag(string folderPath)
    {
        if (!TryNormalize(folderPath, out var path)) return FolderTagColor.None;
        lock (_sync)
        {
            if (_tags.TryGetValue(path, out var stored)) return stored;
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var tag = Directory.Exists(path) ? ReadLegacyTag(path) : FolderTagColor.None;
            _cache[path] = tag;
            return tag;
        }
    }

    /// <summary>Returns known values without reading target folders or the store.</summary>
    public FolderTagColor? GetCachedTag(string folderPath)
    {
        if (!TryNormalize(folderPath, out var path)) return null;
        lock (_sync)
            return _tags.TryGetValue(path, out var tag) || _cache.TryGetValue(path, out tag) ? tag : null;
    }

    /// <summary>None persists an explicit override so clearing also suppresses legacy tags.</summary>
    public string? SetTag(string folderPath, FolderTagColor color)
    {
        if (!Enum.IsDefined(color)) return "Invalid folder tag color.";
        if (!IsTaggable(folderPath) || !TryNormalize(folderPath, out var path))
            return LocalizationService.L("Op_PathNotFoundShort");
        return Change((tags, _) => { tags[path] = color; return true; });
    }

    /// <summary>Empty means the caller should use its localized default color name.</summary>
    public string GetName(FolderTagColor color)
    {
        lock (_sync) return _names.TryGetValue(color, out var name) ? name : string.Empty;
    }

    public string? SetName(FolderTagColor color, string? name)
    {
        if (color == FolderTagColor.None || !Enum.IsDefined(color)) return "Invalid folder tag color.";
        if (!ValidName(name)) return "Folder tag names must be at most 40 characters and contain no control characters.";
        var trimmed = (name ?? string.Empty).Trim();
        return Change((_, names) =>
        {
            if (trimmed.Length == 0) names.Remove(color);
            else names[color] = trimmed;
            return true;
        });
    }

    /// <summary>A detached snapshot; never scans the disk for tagged folders.</summary>
    public IReadOnlyList<string> GetTaggedPaths(FolderTagColor color)
    {
        if (color == FolderTagColor.None || !Enum.IsDefined(color)) return Array.Empty<string>();
        lock (_sync)
            return _tags.Where(p => p.Value == color).Select(p => p.Key)
                .Concat(_cache.Where(p => p.Value == color && !_tags.ContainsKey(p.Key)).Select(p => p.Key))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Call after a successful internal move/rename. Copies must not call this.</summary>
    public string? MovePath(string oldPath, string newPath)
    {
        if (!TryNormalize(oldPath, out var oldRoot)
            || oldRoot.Equals(Path.GetPathRoot(oldRoot), StringComparison.OrdinalIgnoreCase) || !IsTaggable(newPath)
            || !TryNormalize(newPath, out var newRoot))
            return LocalizationService.L("Op_PathNotFoundShort");
        if (oldRoot.Equals(newRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return Change((tags, _) =>
        {
            foreach (var pair in _cache.Where(p => p.Value != FolderTagColor.None && IsAtOrBelow(p.Key, oldRoot)))
                tags.TryAdd(pair.Key, pair.Value);
            var moved = tags.Where(p => IsAtOrBelow(p.Key, oldRoot)).ToArray();
            if (moved.Length == 0) return false;
            foreach (var pair in moved) tags.Remove(pair.Key);
            foreach (var pair in moved) tags[newRoot + pair.Key[oldRoot.Length..]] = pair.Value;
            return true;
        }, oldRoot);
    }

    public void InvalidateCache(string folderPath)
    {
        if (!TryNormalize(folderPath, out var path)) return;
        lock (_sync)
            foreach (var key in _cache.Keys.Where(p => IsAtOrBelow(p, path)).ToArray()) _cache.Remove(key);
    }

    private string? Change(Func<Dictionary<string, FolderTagColor>, Dictionary<FolderTagColor, string>, bool> change,
        string? movedRoot = null)
    {
        lock (_sync)
        {
            if (LoadError != null) return LoadError;
            try
            {
                // Validate again before writing: externally damaged or future-version stores
                // must remain untouched, even if this service loaded a valid store earlier.
                var (tags, names) = ReadStore();
                if (!change(tags, names)) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
                var temporaryPath = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var data = new
                {
                    Version = 1,
                    Tags = tags.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                    Names = names.ToDictionary(p => p.Key.ToString(), p => p.Value)
                };
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data, new JsonSerializerOptions { WriteIndented = true });
                    stream.Flush(flushToDisk: true);
                }
                // Same-directory rename replaces the central store atomically. Failed writes
                // leave the previous store/cache intact; no target-folder cleanup is performed.
                File.Move(temporaryPath, _storePath, overwrite: true);
                _tags = tags;
                _names = names;
                if (movedRoot != null)
                    foreach (var key in _cache.Keys.Where(p => IsAtOrBelow(p, movedRoot)).ToArray()) _cache.Remove(key);
            }
            catch (Exception ex) { return Error(ex); }
        }
        var observers = Changed;
        if (observers != null)
            foreach (Action observer in observers.GetInvocationList())
            {
                try { observer(); }
                catch { DebugLogger.Log("[FolderTag] Change observer failed after successful save."); }
            }
        return null;
    }

    private (Dictionary<string, FolderTagColor>, Dictionary<FolderTagColor, string>) ReadStore()
    {
        var tags = new Dictionary<string, FolderTagColor>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<FolderTagColor, string>();
        if (!File.Exists(_storePath)) return (tags, names);
        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
            || !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 1
            || !root.TryGetProperty("Tags", out var tagData) || tagData.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Names", out var nameData) || nameData.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported or damaged folder tag store; original file was preserved.");
        foreach (var entry in tagData.EnumerateObject())
        {
            if (!TryNormalize(entry.Name, out var path) || entry.Value.ValueKind != JsonValueKind.String
                || !TryColor(entry.Value.GetString(), out var color) || !tags.TryAdd(path, color))
                throw new InvalidDataException("Invalid folder tag record; original file was preserved.");
        }
        foreach (var entry in nameData.EnumerateObject())
        {
            if (!TryColor(entry.Name, out var color) || color == FolderTagColor.None
                || entry.Value.ValueKind != JsonValueKind.String || !ValidName(entry.Value.GetString())
                || !names.TryAdd(color, entry.Value.GetString()!.Trim()))
                throw new InvalidDataException("Invalid folder tag name; original file was preserved.");
        }
        return (tags, names);
    }

    private static bool ValidName(string? name)
        => name == null || (name.Trim().Length <= 40 && !name.Any(char.IsControl));

    private static bool TryColor(string? value, out FolderTagColor color)
        => Enum.TryParse(value, ignoreCase: true, out color) && Enum.IsDefined(color)
            && string.Equals(value, color.ToString(), StringComparison.OrdinalIgnoreCase);

    private static FolderTagColor ReadLegacyTag(string path)
    {
        try
        {
            var ini = Path.Combine(path, "desktop.ini");
            if (!File.Exists(ini)) return FolderTagColor.None;
            var inSection = false;
            foreach (var line in File.ReadLines(ini))
            {
                var text = line.Trim();
                if (text.StartsWith('[') && text.EndsWith(']'))
                {
                    inSection = text.Equals("[Orielfolder]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection || text.StartsWith(';')) continue;
                var separator = text.IndexOf('=');
                if (separator > 0 && text[..separator].Trim().Equals("Tag", StringComparison.OrdinalIgnoreCase))
                    return TryColor(text[(separator + 1)..].Trim(), out var color) ? color : FolderTagColor.None;
            }
        }
        catch { /* Unreadable legacy metadata never prevents browsing. */ }
        return FolderTagColor.None;
    }

    private static bool IsAtOrBelow(string path, string root)
        => path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalize(string? value, out string path)
    {
        path = string.Empty;
        try
        {
            if (string.IsNullOrEmpty(value) || !Path.IsPathFullyQualified(value)
                || value.Contains("://", StringComparison.Ordinal)) return false;
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            return true;
        }
        catch { return false; }
    }

    /// <summary>Existing filesystem files and directories, including removable and network paths.</summary>
    public static bool IsTaggable(string? path)
    {
        if (!TryNormalize(path, out var normalized)) return false;
        try
        {
            return File.Exists(normalized) || Directory.Exists(normalized);
        }
        catch { return false; }
    }

    private static string Error(Exception ex)
        => LocalizationService.L("Toast_OperationFailed") + ": " + ex.Message;
}
