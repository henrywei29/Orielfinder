using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Span.Services;

public sealed record PreviewLabelPreset(string Text, string Color);

public static class PreviewLabelPresets
{
    public const string SettingsKey = "PreviewLabelPresetsJson";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static List<PreviewLabelPreset> Parse(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<PreviewLabelPreset>>(json, JsonOptions);
            if (items == null) throw new InvalidDataException("Invalid preview labels.");
            foreach (var item in items)
                if (item == null || string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > 2000 ||
                    item.Color == null || item.Color.Length != 7 || item.Color[0] != '#' ||
                    item.Color.Skip(1).Any(character => !Uri.IsHexDigit(character)))
                    throw new InvalidDataException("Invalid preview label.");
            return items;
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid preview label settings.", ex); }
    }
}
