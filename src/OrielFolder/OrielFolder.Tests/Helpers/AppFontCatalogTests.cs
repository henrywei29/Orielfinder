using System.Linq;
using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

/// <summary>
/// 字体设置的纯逻辑回归测试。
/// 根因：旧实现按元素“当前字体”判断是否为受保护的等宽/图标字体；
/// 一旦用户选过 Cascadia Code / Consolas，所有元素当前字体都含该名字，
/// 之后的每次切换都会把它们当成受保护字体跳过（applied=0），字体不再生效。
/// </summary>
[TestClass]
public class AppFontCatalogTests
{
    [TestMethod]
    public void ShouldReplaceFont_AfterUserPickedMonospaceFont_StillReplaceable()
    {
        foreach (var mono in new[] { "Cascadia Code", "Consolas", "Courier New" })
        {
            var appliedSpec = AppFontCatalog.ResolveFontSpec(mono, "zh-Hans");
            Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(appliedSpec),
                $"用户选择的 {mono} 字体规格必须能被下一次切换替换: {appliedSpec}");
        }
    }

    [TestMethod]
    public void ShouldReplaceFont_RepeatedSwitchSequence_EveryStepReplaceable()
    {
        // 模拟元素在多次切换中依次持有的字体（含新字体与等宽字体）。
        string? current = null;
        foreach (var key in new[] { "Arial", "Cascadia Code", "Verdana", "Consolas", "Inter", "Geist",
                                    "Noto Sans SC", "Pretendard", "Segoe UI Variable", "Cascadia Code", "Inter" })
        {
            Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(current), $"切换到 {key} 前，当前字体 '{current}' 被错误跳过");
            current = AppFontCatalog.ResolveFontSpec(key, "zh-Hans");
        }
    }

    [TestMethod]
    public void ShouldReplaceFont_SpecsResolvedForOtherLanguage_StillReplaceable()
    {
        // 切换界面语言后回退链顺序会变，旧语言下应用过的规格仍属于用户字体。
        var koSpec = AppFontCatalog.ResolveFontSpec("Cascadia Code", "ko");
        _ = AppFontCatalog.ResolveFontSpec("Cascadia Code", "zh-Hans");
        Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(koSpec));
    }

    [TestMethod]
    public void ShouldReplaceFont_IntentionalMonoAndIconFonts_Protected()
    {
        // 先应用一轮用户字体，确保登记表不会误伤 XAML 里有意设置的字体。
        foreach (var option in AppFontCatalog.Options)
            _ = AppFontCatalog.ResolveFontSpec(option.Key, "zh-Hans");

        string[] protectedSources =
        [
            "Consolas",
            "Cascadia Code, Consolas, Malgun Gothic, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI", // MonoFont
            "Segoe Fluent Icons",
            "Segoe Fluent Icons,Segoe MDL2 Assets",
            "/Assets/Fonts/remixicon.ttf#remixicon",
            "/Assets/Fonts/Phosphor.ttf#Phosphor-Fill",
            "/Assets/Fonts/tabler-icons.ttf#tabler-icons",
        ];
        foreach (var src in protectedSources)
            Assert.IsFalse(AppFontCatalog.ShouldReplaceFont(src), $"受保护字体被替换: {src}");
    }

    [TestMethod]
    public void ShouldReplaceFont_DefaultOrPlainUiFonts_Replaceable()
    {
        Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(null));
        Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(""));
        Assert.IsTrue(AppFontCatalog.ShouldReplaceFont("Segoe UI Variable"));
        Assert.IsTrue(AppFontCatalog.ShouldReplaceFont(
            "Segoe UI Variable, Segoe UI, Malgun Gothic, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI")); // AppFont
    }

    [TestMethod]
    public void Options_AreUniqueAndContainNewFonts()
    {
        var keys = AppFontCatalog.Options.Select(o => o.Key).ToList();
        CollectionAssert.AllItemsAreUnique(keys);
        Assert.AreEqual(AppFontCatalog.DefaultFont, keys[0]);
        foreach (var expected in new[] { "Inter", "Geist", "Noto Sans SC", "Pretendard" })
            CollectionAssert.Contains(keys, expected);
    }

    [TestMethod]
    public void ResolveFontSpec_BundledFonts_UseAppPackagePathWithCjkFallback()
    {
        var inter = AppFontCatalog.ResolveFontSpec("Inter", "zh-Hans");
        StringAssert.StartsWith(inter, "/Assets/Fonts/InterVariable.ttf#");
        // 简体中文：Inter 的中文字形走打包的思源黑体（Noto Sans SC），再落到系统雅黑。
        StringAssert.Contains(inter, "/Assets/Fonts/NotoSansSC-VF.ttf#Noto Sans SC");
        StringAssert.Contains(inter, "Microsoft YaHei UI");

        var noto = AppFontCatalog.ResolveFontSpec("Noto Sans SC", "zh-Hans");
        StringAssert.StartsWith(noto, "/Assets/Fonts/NotoSansSC-VF.ttf#Noto Sans SC");
        Assert.AreEqual(1, CountOccurrences(noto, "NotoSansSC-VF.ttf"));
    }

    [TestMethod]
    public void ResolveFontSpec_CjkFallbackOrder_FollowsUiLanguage()
    {
        // 简体中文时雅黑必须排在 Malgun Gothic 前面，否则“设置”这类词会混用韩文汉字字形。
        var zh = AppFontCatalog.ResolveFontSpec("Arial", "zh-Hans");
        Assert.IsTrue(zh.IndexOf("Microsoft YaHei UI") < zh.IndexOf("Malgun Gothic"), zh);

        var hant = AppFontCatalog.ResolveFontSpec("Arial", "zh-Hant");
        Assert.IsTrue(hant.IndexOf("Microsoft JhengHei UI") < hant.IndexOf("Microsoft YaHei UI"), hant);

        var ja = AppFontCatalog.ResolveFontSpec("Arial", "ja");
        Assert.IsTrue(ja.IndexOf("Yu Gothic UI") < ja.IndexOf("Malgun Gothic"), ja);

        // 其他语言保持原有顺序。
        Assert.AreEqual("Arial, Malgun Gothic, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI",
            AppFontCatalog.ResolveFontSpec("Arial", "ko"));
        Assert.AreEqual("Arial, Malgun Gothic, Microsoft YaHei UI, Microsoft JhengHei UI, Yu Gothic UI",
            AppFontCatalog.ResolveFontSpec("Arial", "en"));
    }

    [TestMethod]
    public void ResolveFontSpec_FontAlsoInFallback_NotDuplicated()
    {
        var spec = AppFontCatalog.ResolveFontSpec("Microsoft YaHei UI", "zh-Hans");
        Assert.AreEqual(1, CountOccurrences(spec, "Microsoft YaHei UI"), spec);
        StringAssert.StartsWith(spec, "Microsoft YaHei UI,");
    }

    [TestMethod]
    public void ResolveFontSpec_EmptyName_FallsBackToDefault()
    {
        StringAssert.StartsWith(AppFontCatalog.ResolveFontSpec("", "en"), AppFontCatalog.DefaultFont);
        StringAssert.StartsWith(AppFontCatalog.ResolveFontSpec(null, "en"), AppFontCatalog.DefaultFont);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(value, idx, System.StringComparison.Ordinal)) >= 0) { count++; idx += value.Length; }
        return count;
    }
}
