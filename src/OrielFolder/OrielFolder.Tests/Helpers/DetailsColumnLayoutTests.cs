using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

[TestClass]
public class DetailsColumnLayoutTests
{
    [DataTestMethod]
    [DataRow("Name", true)]
    [DataRow("CloudStatus", true)]
    [DataRow("ColorTag", true)]
    [DataRow("DateModified", true)]
    [DataRow("Type", true)]
    [DataRow("Size", true)]
    [DataRow("Git", true)]
    [DataRow("Location", false)]
    public void IsToggleable_EveryColumnExceptLocation(string id, bool expected)
        => Assert.AreEqual(expected, DetailsColumnLayout.IsToggleable(id));

    [DataTestMethod]
    [DataRow("Name", "Name")]
    [DataRow("CloudStatus", "ColumnCloudStatus")]
    [DataRow("ColorTag", "ColumnColorTag")]
    [DataRow("DateModified", "DateModified")]
    [DataRow("Type", "Type")]
    [DataRow("Size", "Size")]
    [DataRow("Git", "ColumnGitStatus")]
    public void LabelKey_MapsToLocalizationKeys(string id, string key)
        => Assert.AreEqual(key, DetailsColumnLayout.LabelKey(id));

    [TestMethod]
    public void CanHide_LastVisibleColumn_False()
    {
        Assert.IsFalse(DetailsColumnLayout.CanHide(new[] { "Size" }, "Size"));
        // Location does not count as a remaining column.
        Assert.IsFalse(DetailsColumnLayout.CanHide(new[] { "Name", "Location" }, "Name"));
    }

    [TestMethod]
    public void CanHide_OtherColumnsRemain_True()
    {
        Assert.IsTrue(DetailsColumnLayout.CanHide(new[] { "Name", "Size" }, "Name"));
        Assert.IsTrue(DetailsColumnLayout.CanHide(new[] { "Size" }, "Type")); // already hidden: showing it is allowed
    }

    [TestMethod]
    public void ResizeWidth_AddsDeltaAndClampsToMinimum()
    {
        Assert.AreEqual(180, DetailsColumnLayout.ResizeWidth(130, 50, 60));
        Assert.AreEqual(60, DetailsColumnLayout.ResizeWidth(130, -200, 60));
        Assert.AreEqual(131, DetailsColumnLayout.ResizeWidth(130, 0.6, 60));
        Assert.AreEqual(130, DetailsColumnLayout.ResizeWidth(130, double.NaN, 60));
    }

    [DataTestMethod]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(false, false, true)]
    public void NeedsFiller_OnlyWhenNameDoesNotStretch(bool nameVisible, bool nameStretches, bool expected)
        => Assert.AreEqual(expected, DetailsColumnLayout.NeedsFiller(nameVisible, nameStretches));

    [TestMethod]
    public void Widths_RoundTrip()
    {
        var widths = new Dictionary<string, double> { ["Name"] = 320.4, ["ColorTag"] = 130, ["Size"] = 99.6 };
        var parsed = DetailsColumnLayout.ParseWidths(DetailsColumnLayout.SerializeWidths(widths));
        Assert.AreEqual(3, parsed.Count);
        Assert.AreEqual(320, parsed["Name"]);
        Assert.AreEqual(130, parsed["ColorTag"]);
        Assert.AreEqual(100, parsed["Size"]);
    }

    [TestMethod]
    public void ParseWidths_SkipsUnknownMalformedAndLocation()
    {
        var parsed = DetailsColumnLayout.ParseWidths("Name=300;Bogus=50;Size=abc;Type=-5;Location=200;=7;Git");
        Assert.AreEqual(1, parsed.Count);
        Assert.AreEqual(300, parsed["Name"]);
        Assert.AreEqual(0, DetailsColumnLayout.ParseWidths(null).Count);
    }
}
