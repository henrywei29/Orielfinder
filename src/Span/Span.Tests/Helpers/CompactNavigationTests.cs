using Microsoft.VisualStudio.TestTools.UnitTesting;
using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class CompactNavigationTests
{
    [DataTestMethod]
    [DataRow("abcdefghij", "abcdefghij")]
    [DataRow("abcdefghijk", "abcdefghij…")]
    [DataRow("一二三四五", "一二三四五")]
    [DataRow("一二三四五六", "一二三四五…")]
    [DataRow("abc一二三四", "abc一二三…")]
    [DataRow("ééééééééééx", "éééééééééé…")]
    [DataRow("", "")]
    public void ShortensDisplayOnlyWithoutSplittingTextElements(string input, string expected)
        => Assert.AreEqual(expected, CompactNavigationLabel.Format(input));
}
