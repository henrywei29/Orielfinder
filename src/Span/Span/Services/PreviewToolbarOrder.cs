using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Span.Services;

public static class PreviewToolbarOrder
{
    public const string SettingsKey = "PreviewToolbarOrderJson";
    public static string[] DefaultOrder => new[] { "group-navigation", "group-brushes", "group-rectangles", "group-colors", "group-width", "group-opacity", "group-labels", "group-actions" };
    private static readonly Dictionary<string, string> LegacyGroups = new(StringComparer.Ordinal)
    {
        ["tool-select"] = "group-navigation", ["tool-pan"] = "group-navigation",
        ["tool-pen"] = "group-brushes", ["tool-highlighter"] = "group-brushes", ["tool-mosaicPen"] = "group-brushes", ["tool-redactPen"] = "group-brushes",
        ["tool-rect"] = "group-rectangles", ["tool-mosaicRect"] = "group-rectangles", ["tool-redactRect"] = "group-rectangles", ["tool-text"] = "group-labels",
        ["undoAnnotation"] = "group-actions", ["copyImage"] = "group-actions", ["exportPdf"] = "group-actions"
    };

    public static List<string> Parse(string json)
    {
        try
        {
            var order = JsonSerializer.Deserialize<List<string>>(json);
            if (order == null || order.Distinct(StringComparer.Ordinal).Count() != order.Count || (order.Any(id => id == null) || (!order.All(id => DefaultOrder.Contains(id)) && !order.All(id => LegacyGroups.ContainsKey(id)))))
                throw new InvalidDataException("Invalid preview toolbar order.");
            order = order.Select(id => LegacyGroups.TryGetValue(id, out var group) ? group : id).Distinct(StringComparer.Ordinal).ToList();
            return order.Concat(DefaultOrder.Where(id => !order.Contains(id))).ToList();
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid preview toolbar settings.", ex); }
    }
}
