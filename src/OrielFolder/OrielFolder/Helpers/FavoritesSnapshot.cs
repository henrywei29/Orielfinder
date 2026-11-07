using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OrielFolder.Helpers
{
    /// <summary>One sidebar favorite as last read from Windows Quick Access (name before localization).</summary>
    public sealed record FavoriteSnapshotEntry(string Name, string Path);

    /// <summary>
    /// Last Quick Access result, kept so startup can show the sidebar favorites at once and read
    /// Quick Access (Shell COM, ~400 ms) off the UI thread. Pure logic — no I/O here.
    /// </summary>
    public static class FavoritesSnapshot
    {
        public static string Serialize(IEnumerable<FavoriteSnapshotEntry> entries)
            => JsonSerializer.Serialize(entries);

        /// <summary>Parses a snapshot; null when missing, malformed or empty.</summary>
        public static List<FavoriteSnapshotEntry>? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var raw = JsonSerializer.Deserialize<List<FavoriteSnapshotEntry>>(json);
                if (raw == null) return null;
                var result = new List<FavoriteSnapshotEntry>(raw.Count);
                foreach (var e in raw)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Path)) continue;
                    result.Add(new FavoriteSnapshotEntry(e.Name ?? string.Empty, e.Path));
                }
                return result.Count > 0 ? result : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Same items in the same order (paths case-insensitive, names exact).</summary>
        public static bool SameEntries(IReadOnlyList<FavoriteSnapshotEntry>? a, IReadOnlyList<FavoriteSnapshotEntry>? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
