using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Span.Helpers;

/// <summary>Prepared once per query; advanced filters and wildcards remain strict.</summary>
public sealed class SearchNameMatcher
{
    private readonly string _query;
    private readonly string _compactQuery;
    private readonly string[] _terms;

    public SearchNameMatcher(string query, IEnumerable<string>? terms = null)
    {
        _query = query;
        _compactQuery = Compact(query);
        _terms = (terms ?? query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(Compact).Where(term => term.Length > 0).Distinct().ToArray();
    }

    public bool IsMatch(string name)
    {
        if (name.Contains(_query, StringComparison.OrdinalIgnoreCase)) return true;
        if (_compactQuery.Length == 0) return false;
        string normalized = name.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        string compactName = Compact(normalized);
        if (compactName.Contains(_compactQuery, StringComparison.Ordinal)) return true;

        string[]? words = null;
        foreach (string term in _terms)
        {
            if (compactName.Contains(term, StringComparison.Ordinal)) continue;
            // Short queries need a real substring, otherwise one-letter errors match too much.
            if (term.EnumerateRunes().Count() < 4) return false;
            words ??= SplitWords(normalized).ToArray();
            if (!words.Any(word => OneEditApart(term, word))) return false;
        }
        return _terms.Length > 0;
    }

    private static string Compact(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (Rune rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune)) continue;
            Rune upper = Rune.ToUpperInvariant(rune);
            if (upper.IsBmp) result.Append((char)upper.Value);
            else result.Append(upper.ToString());
        }
        return result.ToString();
    }

    private static IEnumerable<string> SplitWords(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length;)
        {
            Rune rune = Rune.GetRuneAt(text, i);
            if (Rune.IsLetterOrDigit(rune)) { i += rune.Utf16SequenceLength; continue; }
            if (i > start) yield return text.Substring(start, i - start);
            i += rune.Utf16SequenceLength;
            start = i;
        }
        if (start < text.Length) yield return text.Substring(start);
    }

    // Bounded, linear comparison: one insertion, deletion, substitution or adjacent swap.
    private static bool OneEditApart(string leftText, string rightText)
    {
        var left = leftText.EnumerateRunes().ToArray();
        var right = rightText.EnumerateRunes().ToArray();
        if (Math.Abs(left.Length - right.Length) > 1) return false;
        int i = 0;
        while (i < left.Length && i < right.Length && left[i] == right[i]) i++;
        if (i == Math.Min(left.Length, right.Length)) return true;
        if (left.Length == right.Length)
        {
            if (left.AsSpan(i + 1).SequenceEqual(right.AsSpan(i + 1))) return true;
            return i + 1 < left.Length && left[i] == right[i + 1] && left[i + 1] == right[i]
                && left.AsSpan(i + 2).SequenceEqual(right.AsSpan(i + 2));
        }
        return left.Length > right.Length
            ? left.AsSpan(i + 1).SequenceEqual(right.AsSpan(i))
            : left.AsSpan(i).SequenceEqual(right.AsSpan(i + 1));
    }
}
