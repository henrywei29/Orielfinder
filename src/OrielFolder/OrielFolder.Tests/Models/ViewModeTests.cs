namespace OrielFolder.Tests.Models;

[TestClass]
public class ViewModeTests
{
    [TestMethod]
    public void ViewMode_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)OrielFolder.Models.ViewMode.MillerColumns);
        Assert.AreEqual(1, (int)OrielFolder.Models.ViewMode.Details);
        Assert.AreEqual(2, (int)OrielFolder.Models.ViewMode.IconSmall);
        Assert.AreEqual(3, (int)OrielFolder.Models.ViewMode.IconMedium);
        Assert.AreEqual(4, (int)OrielFolder.Models.ViewMode.IconLarge);
        Assert.AreEqual(5, (int)OrielFolder.Models.ViewMode.IconExtraLarge);
        Assert.AreEqual(6, (int)OrielFolder.Models.ViewMode.Home);
    }

    [TestMethod]
    public void PreviewType_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)OrielFolder.Models.PreviewType.None);
        Assert.AreEqual(1, (int)OrielFolder.Models.PreviewType.Image);
        Assert.AreEqual(2, (int)OrielFolder.Models.PreviewType.Text);
        Assert.AreEqual(3, (int)OrielFolder.Models.PreviewType.Pdf);
        Assert.AreEqual(4, (int)OrielFolder.Models.PreviewType.Media);
        Assert.AreEqual(5, (int)OrielFolder.Models.PreviewType.Folder);
        Assert.AreEqual(6, (int)OrielFolder.Models.PreviewType.HexBinary);
        Assert.AreEqual(7, (int)OrielFolder.Models.PreviewType.Font);
        Assert.AreEqual(8, (int)OrielFolder.Models.PreviewType.Archive);
        Assert.AreEqual(9, (int)OrielFolder.Models.PreviewType.Markdown);
        Assert.AreEqual(10, (int)OrielFolder.Models.PreviewType.Csv);
        Assert.AreEqual(11, (int)OrielFolder.Models.PreviewType.Generic);
    }

    [TestMethod]
    public void ActivePane_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)OrielFolder.Models.ActivePane.Left);
        Assert.AreEqual(1, (int)OrielFolder.Models.ActivePane.Right);
    }
}
