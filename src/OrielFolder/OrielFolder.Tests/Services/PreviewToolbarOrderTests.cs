using System.IO;
using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class PreviewToolbarOrderTests
{
    [TestMethod]
    public void Parse_LegacyIconsBecomeWholeGroupsByFirstOccurrence()
    {
        var order = PreviewToolbarOrder.Parse("[\"exportPdf\",\"tool-redactPen\",\"tool-mosaicRect\",\"tool-highlighter\",\"tool-text\"]");
        CollectionAssert.AreEqual(new[] { "group-actions", "group-brushes", "group-rectangles", "group-labels", "group-navigation", "group-colors", "group-width", "group-opacity" }, order.ToArray());
    }

    [TestMethod]
    public void Parse_GroupOrderPreservedAndMissingGroupsRestored()
    {
        var order = PreviewToolbarOrder.Parse("[\"group-opacity\",\"group-colors\",\"group-width\"]");
        CollectionAssert.AreEqual(new[] { "group-opacity", "group-colors", "group-width" }, order.Take(3).ToArray());
        CollectionAssert.AreEquivalent(PreviewToolbarOrder.DefaultOrder, order.ToArray());
        Assert.AreEqual(8, order.Count);
    }

    [TestMethod]
    public void Parse_EmptyOrderKeepsAllFunctions() =>
        CollectionAssert.AreEqual(PreviewToolbarOrder.DefaultOrder, PreviewToolbarOrder.Parse("[]").ToArray());

    [DataTestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[null]")]
    [DataRow("[\"tool-pen\",\"tool-pen\"]")]
    [DataRow("[\"group-colors\",\"group-colors\"]")]
    [DataRow("[\"group-colors\",\"tool-pen\"]")]
    [DataRow("[\"unknown\"]")]
    [DataRow("[\"#tool-pen\"]")]
    public void Parse_InvalidOrderRejected(string json) =>
        Assert.ThrowsException<InvalidDataException>(() => PreviewToolbarOrder.Parse(json));
}
