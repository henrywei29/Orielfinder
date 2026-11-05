using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Helpers;

/// <summary>Stable column identities shared by the Details header and recycled rows.</summary>
public static class DetailsColumnOrder
{
    private static readonly string[] Defaults = { "Name", "CloudStatus", "ColorTag", "Location", "DateModified", "Type", "Size", "Git" };

    public static string[] Parse(string? saved) => (saved ?? string.Empty).Split(',')
        .Where(id => Defaults.Contains(id, StringComparer.Ordinal))
        .Concat(Defaults).Distinct(StringComparer.Ordinal).ToArray();

    public static string Serialize(IEnumerable<string> order) => string.Join(",", order);

    public static string[] Move(IEnumerable<string> order, string source, string target, bool after)
    {
        var result = Parse(Serialize(order)).ToList();
        if (source == target || !result.Contains(source) || !result.Contains(target)) return result.ToArray();
        result.Remove(source);
        result.Insert(result.IndexOf(target) + (after ? 1 : 0), source);
        return result.ToArray();
    }
}
