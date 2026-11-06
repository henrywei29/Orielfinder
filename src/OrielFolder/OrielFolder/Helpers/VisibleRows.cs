using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OrielFolder.Helpers;

/// <summary>Keep unchanged rows and their UI containers when one tree branch changes.</summary>
public static class VisibleRows
{
    public static void UpdateGroups<T>(ObservableCollection<VisibleRowGroup<T>> groups,
        IReadOnlyList<VisibleRowGroup<T>> next, Func<T, string> key) where T : class
    {
        var existing = groups.ToDictionary(group => group.Key, StringComparer.Ordinal);
        var ordered = new List<VisibleRowGroup<T>>(next.Count);
        foreach (var group in next)
        {
            if (existing.TryGetValue(group.Key, out var retained))
            {
                Update(retained, group, key);
                ordered.Add(retained);
            }
            else ordered.Add(group);
        }
        Update(groups, ordered, group => group.Key);
    }

    public static void Update<T>(ObservableCollection<T> rows, IReadOnlyList<T> next, Func<T, string> key)
        where T : class
    {
        int prefix = 0;
        int common = Math.Min(rows.Count, next.Count);
        while (prefix < common && StringComparer.OrdinalIgnoreCase.Equals(key(rows[prefix]), key(next[prefix])))
        {
            if (!ReferenceEquals(rows[prefix], next[prefix])) rows[prefix] = next[prefix];
            prefix++;
        }
        int suffix = 0;
        while (suffix < common - prefix && StringComparer.OrdinalIgnoreCase.Equals(
            key(rows[rows.Count - 1 - suffix]), key(next[next.Count - 1 - suffix]))) suffix++;
        int oldMid = rows.Count - prefix - suffix;
        int newMid = next.Count - prefix - suffix;
        int shared = Math.Min(oldMid, newMid);
        for (int i = 0; i < shared; i++)
            if (!ReferenceEquals(rows[prefix + i], next[prefix + i])) rows[prefix + i] = next[prefix + i];
        for (int i = rows.Count - suffix - 1; i >= prefix + shared; i--) rows.RemoveAt(i);
        for (int i = prefix + shared; i < next.Count - suffix; i++) rows.Insert(i, next[i]);
        for (int i = next.Count - suffix; i < next.Count; i++)
            if (!ReferenceEquals(rows[i], next[i])) rows[i] = next[i];
    }
}

public sealed class VisibleRowGroup<T> : ObservableCollection<T> where T : class
{
    public string Key { get; }
    public VisibleRowGroup(string key, IEnumerable<T> rows) : base(rows) => Key = key;
}
