using OrielFolder.Helpers;

namespace OrielFolder.Tests.Services;

[TestClass]
public class DetailsColumnOrderTests
{
    [TestMethod]
    public void Parse_PreservesKnownOrderAndAppendsMissingColumns()
    {
        var order = DetailsColumnOrder.Parse("Size,Name,Size,obsolete,ColorTag");
        CollectionAssert.AreEqual(new[] { "Size", "Name", "ColorTag", "CloudStatus", "Location", "DateModified", "Type", "Git" }, order);
    }

    [TestMethod]
    public void Parse_EmptySettingsProvidesIndependentStatusAndTagColumns()
    {
        CollectionAssert.AreEqual(new[] { "Name", "CloudStatus", "ColorTag", "Location", "DateModified", "Type", "Size", "Git" }, DetailsColumnOrder.Parse(null));
    }

    [TestMethod]
    public void Move_CanMoveNameAndTagInBothDirectionsWithoutLosingHiddenColumns()
    {
        var order = DetailsColumnOrder.Move(DetailsColumnOrder.Parse(null), "Name", "Size", after: true);
        CollectionAssert.AreEqual(new[] { "CloudStatus", "ColorTag", "Location", "DateModified", "Type", "Size", "Name", "Git" }, order);
        order = DetailsColumnOrder.Move(order, "ColorTag", "CloudStatus", after: false);
        Assert.AreEqual("ColorTag", order[0]);
        CollectionAssert.AreEquivalent(DetailsColumnOrder.Parse(null), order);
    }

    [TestMethod]
    public void Serialize_ReorderedColumnsSurviveReload()
    {
        var order = DetailsColumnOrder.Move(DetailsColumnOrder.Parse(null), "ColorTag", "Name", after: false);
        CollectionAssert.AreEqual(order, DetailsColumnOrder.Parse(DetailsColumnOrder.Serialize(order)));
    }

    [TestMethod]
    public void Move_SelfOrUnknownColumnLeavesOrderUnchanged()
    {
        var order = DetailsColumnOrder.Parse(null);
        CollectionAssert.AreEqual(order, DetailsColumnOrder.Move(order, "Name", "Name", false));
        CollectionAssert.AreEqual(order, DetailsColumnOrder.Move(order, "unknown", "Name", false));
    }
}
