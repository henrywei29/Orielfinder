using System.Text.Json;
using System.Xml.Linq;
using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class FileAppearanceTests
{
    [TestMethod]
    public void ContrastTheme_ProvidesAllApplicationThemeResources()
    {
        var themes = ReadThemeDictionaries();
        XNamespace key = "http://schemas.microsoft.com/winfx/2006/xaml";
        var required = themes.Where(t => t.Key is "Light" or "Dark")
            .SelectMany(t => t.Value.Elements()).Select(e => (string?)e.Attribute(key + "Key"))
            .Where(k => k != null).Distinct().ToArray();
        var contrast = themes["HighContrast"].Elements()
            .Select(e => (string?)e.Attribute(key + "Key")).ToHashSet();
        var missing = required.Where(k => !contrast.Contains(k)).ToArray();
        Assert.AreEqual(0, missing.Length, "Contrast theme cannot resolve: " + string.Join(", ", missing));
    }

    [TestMethod]
    [DataRow("Dark")]
    [DataRow("Light")]
    public void KeyboardFocus_RemainsVisibleAgainstFileContent(string theme)
    {
        XNamespace key = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = ReadThemeDictionaries()[theme].Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(key + "Key")!, e => e.Value.Trim().TrimStart('#'));
        var background = Channels(colors["SpanBgLayer1Color"]);
        string focus = colors["SpanAccentDimColor"];
        double alpha = focus.Length == 8 ? Convert.ToInt32(focus[..2], 16) / 255d : 1;
        var blended = Channels(focus).Zip(background, (f, b) => f * alpha + b * (1 - alpha)).ToArray();
        double first = Luminance(blended), second = Luminance(background);
        double contrast = (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        Assert.IsTrue(contrast >= 3, $"{theme} focus contrast is {contrast:F2}:1");
    }

    private static Dictionary<string, XElement> ReadThemeDictionaries()
    {
        XNamespace key = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(Path.GetDirectoryName(FindIconConfig("icons.json"))!, "App.xaml"));
        return doc.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary"
            && (string?)e.Attribute(key + "Key") is "Light" or "Dark" or "HighContrast")
            .ToDictionary(e => (string)e.Attribute(key + "Key")!);
    }

    private static double[] Channels(string hex) => Enumerable.Range(0, 3)
        .Select(i => Convert.ToInt32(hex.Substring(hex.Length - 6 + i * 2, 2), 16) / 255d).ToArray();

    private static double Luminance(double[] rgb)
    {
        var values = rgb.Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
        return values[0] * .2126 + values[1] * .7152 + values[2] * .0722;
    }

    [TestMethod]
    public void FileSpacing_FractionalSettingsPreserveSubpixelValues()
    {
        var scale = new FontScaleService();
        Assert.AreEqual(29d, scale.GetFileRowHeight("2.5"));
        Assert.AreEqual(37.5d, scale.GetFileRowHeight("6.75"));
    }

    [TestMethod]
    public void FileSpacing_SixLevelsRemainOrderedAndNeverClipLargestIcon()
    {
        var scale = new FontScaleService { Level = 5 };
        double previous = 0;
        for (int level = 0; level <= 5; level++)
        {
            double height = scale.GetFileRowHeight(level.ToString());
            Assert.IsTrue(height >= scale.IconFontSize + 4);
            Assert.IsTrue(height >= previous);
            previous = height;
        }
        scale.Level = 0;
        Assert.AreEqual(24d, scale.GetFileRowHeight("compact"));
        Assert.AreEqual(28d, scale.GetFileRowHeight("comfortable"));
        Assert.AreEqual(32d, scale.GetFileRowHeight("spacious"));
        Assert.AreEqual(28d, scale.GetFileRowHeight("invalid"));
        Assert.AreEqual(24d, scale.GetFileRowHeight("-9"));
        Assert.AreEqual(48d, scale.GetFileRowHeight("99"));
    }

    [TestMethod]
    [DataRow(0d, 13d, 20d)]
    [DataRow(.25d, 13.25d, 20.25d)]
    [DataRow(4.75d, 17.75d, 24.75d)]
    [DataRow(5d, 18d, 25d)]
    public void ContinuousFontScale_PreservesFractionalTextAndIconSizes(double value, double text, double icon)
    {
        var scale = new FontScaleService { Scale = value };
        Assert.AreEqual(text, scale.ItemFontSize);
        Assert.AreEqual(icon, scale.IconFontSize);
        Assert.AreEqual(icon + 9d, scale.GetFileRowHeight("2.5"));
        Assert.AreEqual(value, FontScaleService.GetFontScale(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestMethod]
    public void ContinuousSizes_RejectNonFiniteValuesAndClampOutOfRange()
    {
        var scale = new FontScaleService { Scale = double.NaN };
        Assert.AreEqual(0d, scale.Scale);
        scale.Scale = 99;
        Assert.AreEqual(5d, scale.Scale);
        Assert.AreEqual(0d, FontScaleService.GetFontScale("Infinity"));
        Assert.AreEqual(2d, FontScaleService.GetFileSpacing("NaN"));
        Assert.AreEqual(0d, FontScaleService.GetFontScale("-99"));
    }

    [TestMethod]
    [DataRow("icons.json")]
    [DataRow("icons-phosphor.json")]
    [DataRow("icons-tabler.json")]
    public void BuiltInOfficeIcons_UseDistinctSaturatedFamilyColors(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindIconConfig(file)));
        foreach (string extension in new[] { "doc", "docx", "xls", "xlsx", "ppt", "pptx" })
        {
            var mapping = doc.RootElement.GetProperty("mappings").EnumerateArray().Single(m =>
                m.GetProperty("extensions").EnumerateArray().Any(e => e.GetString() == extension));
            string hex = mapping.GetProperty("color").GetString()!;
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int b = Convert.ToInt32(hex.Substring(5, 2), 16);
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            Assert.IsTrue((max - min) / (double)max >= .75, $"{file}: {extension} color too muted");
            if (extension.StartsWith("doc")) Assert.IsTrue(b > r && b > g);
            if (extension.StartsWith("xls")) Assert.IsTrue(g > r && g > b);
            if (extension.StartsWith("ppt")) Assert.IsTrue(r > g && r > b);
        }
    }

    private static string FindIconConfig(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Span", file);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(file);
    }
}
