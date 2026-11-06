using System;
using System.Collections.Generic;
using System.Linq;

namespace OrielFolder.Helpers
{
    /// <summary>
    /// 用户界面字体的目录与解析规则（纯逻辑，无 WinUI 依赖，可单元测试）。
    /// <para>
    /// 设置页的字体选项、FontFamily 规格字符串（含 CJK 回退链）、以及
    /// “可视树遍历时哪些元素可以被替换为用户字体”的判断都集中在这里。
    /// </para>
    /// <para>
    /// 判断规则：元素当前字体如果是本应用曾经应用过的用户字体规格，就始终可替换；
    /// 否则才检查是否为 XAML 里有意设置的等宽/图标/应用内资源字体。
    /// 旧实现只看“当前字体名是否包含 Cascadia/Consolas”，导致用户选过等宽字体后，
    /// 所有元素都被误判成受保护字体，之后的切换全部失效。
    /// </para>
    /// </summary>
    public static class AppFontCatalog
    {
        public const string DefaultFont = "Segoe UI Variable";

        public sealed record FontOption(string Key, string DisplayName);

        /// <summary>设置页字体下拉框的选项（Key 为持久化到设置里的值）。</summary>
        public static IReadOnlyList<FontOption> Options { get; } =
        [
            // Sans-serif
            new("Segoe UI Variable", "Segoe UI Variable"),
            new("Inter", "Inter"),
            new("Geist", "Geist"),
            new("Noto Sans SC", "Noto Sans SC (思源黑体)"),
            new("Pretendard", "Pretendard"),
            new("Arial", "Arial"),
            new("Verdana", "Verdana"),
            new("Calibri", "Calibri"),
            // Monospace
            new("Cascadia Code", "Cascadia Code"),
            new("Consolas", "Consolas"),
            new("Courier New", "Courier New"),
            // CJK-optimized
            new("Malgun Gothic", "Malgun Gothic"),
            new("Microsoft YaHei UI", "Microsoft YaHei UI"),
            new("Yu Gothic UI", "Yu Gothic UI"),
        ];

        private const string NotoSansScSpec = "/Assets/Fonts/NotoSansSC-VF.ttf#Noto Sans SC";

        /// <summary>打包在应用内的字体：设置名 → 应用包内路径#字体族名。</summary>
        private static readonly Dictionary<string, string> BundledFonts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Inter"] = "/Assets/Fonts/InterVariable.ttf#Inter Variable",
            ["Geist"] = "/Assets/Fonts/Geist-Variable.ttf#Geist",
            ["Noto Sans SC"] = NotoSansScSpec,
            ["Pretendard"] = "/Assets/Fonts/PretendardVariable.ttf#Pretendard Variable",
        };

        /// <summary>
        /// 只有拉丁字形的 macOS 风格字体：简体中文界面下，中文字形优先用打包的思源黑体，
        /// 观感接近 SF Pro + 苹方 的组合。
        /// </summary>
        private static readonly HashSet<string> LatinOnlyBundledFonts = new(StringComparer.OrdinalIgnoreCase)
        {
            "Inter", "Geist",
        };

        private static readonly string[] DefaultCjkOrder =
            ["Malgun Gothic", "Microsoft YaHei UI", "Microsoft JhengHei UI", "Yu Gothic UI"];

        /// <summary>XAML 中有意设置、不能被用户字体覆盖的字体名片段。</summary>
        private static readonly string[] ProtectedFontMarkers =
        [
            "Consolas", "Cascadia", "Segoe Fluent", "Segoe MDL2",
            "remixicon", "Phosphor", "tabler-icons",
        ];

        private static readonly object SpecLock = new();
        private static readonly HashSet<string> AppliedUserSpecs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 返回 CJK 回退链。简体/繁体中文、日文界面把对应字体排在前面，
        /// 避免同一个词里的字混用 Malgun Gothic（韩文汉字字形）和雅黑。
        /// 其他语言保持原有顺序。
        /// </summary>
        public static IReadOnlyList<string> GetCjkFallbackOrder(string? uiLanguage)
        {
            var lang = uiLanguage ?? "";
            if (lang.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                || lang.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || lang.Equals("zh-HK", StringComparison.OrdinalIgnoreCase))
                return ["Microsoft JhengHei UI", "Microsoft YaHei UI", "Yu Gothic UI", "Malgun Gothic"];
            if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return ["Microsoft YaHei UI", "Microsoft JhengHei UI", "Yu Gothic UI", "Malgun Gothic"];
            if (lang.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                return ["Yu Gothic UI", "Malgun Gothic", "Microsoft YaHei UI", "Microsoft JhengHei UI"];
            return DefaultCjkOrder;
        }

        private static bool IsSimplifiedChinese(string? uiLanguage)
        {
            var lang = uiLanguage ?? "";
            return lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                && GetCjkFallbackOrder(lang)[0] == "Microsoft YaHei UI";
        }

        /// <summary>
        /// 把字体设置名解析成 FontFamily 规格字符串（主字体 + CJK 回退链），并登记为用户字体规格。
        /// 打包字体使用应用包内路径，系统字体使用字体名。
        /// </summary>
        public static string ResolveFontSpec(string? fontKey, string? uiLanguage)
        {
            var key = string.IsNullOrWhiteSpace(fontKey) ? DefaultFont : fontKey.Trim();
            var parts = new List<string>();

            if (BundledFonts.TryGetValue(key, out var bundledSpec))
            {
                parts.Add(bundledSpec);
                if (LatinOnlyBundledFonts.Contains(key) && IsSimplifiedChinese(uiLanguage))
                    parts.Add(NotoSansScSpec);
            }
            else
            {
                parts.Add(key);
            }

            foreach (var cjk in GetCjkFallbackOrder(uiLanguage))
            {
                if (!parts.Contains(cjk, StringComparer.OrdinalIgnoreCase))
                    parts.Add(cjk);
            }

            var spec = string.Join(", ", parts);
            lock (SpecLock) AppliedUserSpecs.Add(spec);
            return spec;
        }

        /// <summary>该规格是否是本应用解析/应用过的用户字体。</summary>
        public static bool IsUserFontSpec(string? source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            lock (SpecLock) return AppliedUserSpecs.Contains(source);
        }

        /// <summary>
        /// 可视树遍历时，当前字体为 <paramref name="currentSource"/> 的元素是否应替换为用户字体。
        /// 用户字体规格始终可替换（即使是 Cascadia Code / Consolas）；
        /// XAML 里有意设置的等宽/图标字体，以及应用包内的其他字体资源受保护。
        /// </summary>
        public static bool ShouldReplaceFont(string? currentSource)
        {
            if (string.IsNullOrEmpty(currentSource)) return true;
            if (IsUserFontSpec(currentSource)) return true;

            foreach (var marker in ProtectedFontMarkers)
            {
                if (currentSource.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 应用包内的字体资源（图标包等）不是用户字体，不能覆盖。
            if (currentSource.Contains("/Assets/Fonts/", StringComparison.OrdinalIgnoreCase)
                || currentSource.Contains("ms-appx:", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
