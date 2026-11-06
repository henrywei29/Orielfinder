using System.Globalization;
using System.Text;

namespace OrielFolder.Helpers;

/// <summary>Compact breadcrumb/tab display only; navigation always uses the original path.</summary>
public static class CompactNavigationLabel
{
    public static string Format(string text)
    {
        var result = new StringBuilder();
        int width = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int code = char.ConvertToUtf32(element, 0);
            int size = code is >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7AF
                or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF or >= 0x10000 ? 2 : 1;
            if (width + size > 10) return result.Append('…').ToString();
            width += size;
            result.Append(element);
        }
        return result.ToString();
    }
}
