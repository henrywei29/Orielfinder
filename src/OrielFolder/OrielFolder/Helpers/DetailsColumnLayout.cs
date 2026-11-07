using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OrielFolder.Helpers;

/// <summary>
/// Pure rules for the Details header: which columns the header menu can toggle,
/// the localization key of each column, saved column widths, and the
/// Explorer-style resize (a separator only resizes the column on its left;
/// every column to its right keeps its width and moves).
/// </summary>
public static class DetailsColumnLayout
{
    /// <summary>"Location" only appears in search results and is not user-toggleable.</summary>
    public static bool IsToggleable(string id) => id != "Location";

    /// <summary>Localization key used for the column in the header menu.</summary>
    public static string LabelKey(string id) => id switch
    {
        "CloudStatus" => "ColumnCloudStatus",
        "ColorTag" => "ColumnColorTag",
        "Git" => "ColumnGitStatus",
        _ => id // Name, DateModified, Type, Size
    };

    /// <summary>
    /// A column may be hidden unless it is the last visible toggleable column
    /// (the header must keep at least one column).
    /// </summary>
    public static bool CanHide(IEnumerable<string> visibleColumns, string id)
    {
        var visible = visibleColumns.Where(IsToggleable).ToList();
        return !visible.Contains(id, StringComparer.Ordinal) || visible.Count > 1;
    }

    /// <summary>New width of the column left of a dragged separator.</summary>
    public static double ResizeWidth(double startWidth, double delta, double minWidth)
    {
        double width = startWidth + delta;
        if (!double.IsFinite(width)) width = startWidth;
        return Math.Round(Math.Max(Math.Max(minWidth, 0), width));
    }

    /// <summary>
    /// The header needs an empty filler column at the right end unless Name is visible
    /// and still stretches (star width) to fill the row.
    /// </summary>
    public static bool NeedsFiller(bool nameVisible, bool nameStretches) => !(nameVisible && nameStretches);

    /// <summary>Parses "Name=320;Size=100". Unknown, malformed and non-positive entries are skipped.</summary>
    public static Dictionary<string, double> ParseWidths(string? saved)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(saved)) return result;
        foreach (var part in saved.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string id = part[..eq].Trim();
            if (!IsKnownColumn(id) || !IsToggleable(id)) continue;
            if (double.TryParse(part[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                && double.IsFinite(width) && width > 0)
                result[id] = Math.Round(width);
        }
        return result;
    }

    public static string SerializeWidths(IEnumerable<KeyValuePair<string, double>> widths) =>
        string.Join(";", widths
            .Where(pair => IsKnownColumn(pair.Key) && IsToggleable(pair.Key) && double.IsFinite(pair.Value) && pair.Value > 0)
            .Select(pair => pair.Key + "=" + Math.Round(pair.Value).ToString(CultureInfo.InvariantCulture)));

    private static bool IsKnownColumn(string id) => DetailsColumnOrder.Parse(null).Contains(id, StringComparer.Ordinal);
}
