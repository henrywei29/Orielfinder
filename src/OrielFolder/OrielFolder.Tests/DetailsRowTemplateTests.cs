using System.Xml.Linq;

namespace OrielFolder.Tests;

/// <summary>
/// 详细信息视图每个缓存行都会创建一整套 XAML 对象；WinUI 为每个对象向 GC 报内存压力，
/// 每次 Gen2 都要遍历所有活对象（包括隐藏标签的行）。字号绑定用编译型 x:Bind：
/// 原来每行 11 个 {Binding} 表达式对象，配合 GcBallast 后每次 Gen2 从 ~220 ms 降到 ~155 ms（6 个 System32 标签）。
///
/// 不要在行模板里用随数据变化的 x:Load（x:Load="{x:Bind ...}"）：实测新建或回收的行容器
/// 绑定到条件已经为 true 的项时，元素不会被创建（Git 列的 M/? 消失），
/// 之后再发属性通知也补不回来。只允许常量 x:Load="False"（永远不显示的元素）。
/// </summary>
[TestClass]
public class DetailsRowTemplateTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement RowTemplate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OrielFolder.sln")))
            directory = directory.Parent;
        if (directory == null) throw new AssertFailedException("Cannot locate development source tree.");
        var doc = XDocument.Load(Path.Combine(directory.FullName, "src", "OrielFolder", "OrielFolder", "Views", "DetailsModeView.xaml"));
        return doc.Descendants().Single(e => e.Name.LocalName == "ListView.ItemTemplate");
    }

    [TestMethod]
    public void RowTemplate_HasNoRuntimeBindings()
    {
        var bindings = RowTemplate().DescendantsAndSelf().Attributes()
            .Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal))
            .Select(a => $"{a.Parent!.Name.LocalName}.{a.Name.LocalName}").ToArray();
        Assert.AreEqual(0, bindings.Length, "Use x:Bind in the row template: " + string.Join(", ", bindings));
    }

    [TestMethod]
    public void RowTemplate_FontSizesFollowFontScaleLive()
    {
        var fontSizes = RowTemplate().Descendants().Attributes()
            .Where(a => a.Name.LocalName is "FontSize" && a.Value.Contains("FontScaleService.Instance", StringComparison.Ordinal))
            .ToArray();
        Assert.IsTrue(fontSizes.Length > 0);
        Assert.IsTrue(fontSizes.All(a => a.Value.Contains("Mode=OneWay", StringComparison.Ordinal)),
            "Font scale changes at runtime; its bindings must be OneWay");
    }

    [TestMethod]
    public void RowTemplate_HasNoDataDrivenXLoad()
    {
        var dataDriven = RowTemplate().Descendants()
            .Where(e => e.Attribute(X + "Load") is { } load && load.Value != "False")
            .Select(e => (string?)e.Attribute(X + "Name") ?? e.Name.LocalName).ToArray();
        Assert.AreEqual(0, dataDriven.Length, "Recycled rows miss data-driven x:Load parts: " + string.Join(", ", dataDriven));
    }

    [TestMethod]
    public void NeverShownTagDot_IsNotCreated()
    {
        var dot = RowTemplate().Descendants().SingleOrDefault(e => (string?)e.Attribute(X + "Name") == "TagDot");
        Assert.IsNotNull(dot);
        Assert.AreEqual("False", (string?)dot.Attribute(X + "Load"));
    }
}
