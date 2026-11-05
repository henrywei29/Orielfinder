using System.IO;
using System.Text.Json;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class PreviewLabelPresetsTests
{
    [TestMethod]
    public void Parse_PreservesUnicodeColorsOrderAndDuplicateText()
    {
        var labels = PreviewLabelPresets.Parse("[{\"text\":\"重点 中文\",\"color\":\"#ABCDEF\"},{\"text\":\"重点 中文\",\"color\":\"#112233\"}]");
        Assert.AreEqual(2, labels.Count);
        Assert.AreEqual("重点 中文", labels[0].Text);
        Assert.AreEqual("#ABCDEF", labels[0].Color);
        Assert.AreEqual("#112233", labels[1].Color);
        var restored = PreviewLabelPresets.Parse(JsonSerializer.Serialize(labels, PreviewLabelPresets.JsonOptions));
        Assert.AreEqual(labels[0], restored[0]);
        Assert.AreEqual(labels[1], restored[1]);
    }

    [DataTestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[null]")]
    [DataRow("[{\"text\":\" \" ,\"color\":\"#112233\"}]")]
    [DataRow("[{\"text\":\"a\",\"color\":\"red\"}]")]
    [DataRow("[{\"text\":\"a\",\"color\":\"#gggggg\"}]")]
    [DataRow("[{\"text\":\"a\",\"color\":\"# 12345\"}]")]
    [DataRow("[{\"text\":\"a\"}]")]
    public void Parse_InvalidSettingsRejected(string json) =>
        Assert.ThrowsException<InvalidDataException>(() => PreviewLabelPresets.Parse(json));

    [TestMethod]
    public void Parse_TextBeyondEditorLimitRejected() =>
        Assert.ThrowsException<InvalidDataException>(() => PreviewLabelPresets.Parse("[{\"text\":\"" + new string('x', 2001) + "\",\"color\":\"#112233\"}]"));

    [TestMethod]
    public void Parse_EmptyListAllowed() => Assert.AreEqual(0, PreviewLabelPresets.Parse("[]").Count);
}
