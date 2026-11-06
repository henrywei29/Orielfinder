using OrielFolder.Helpers;

namespace OrielFolder.Tests.Helpers;

[TestClass]
public class WindowFrameGeometryTests
{
    // Typical default WM_NCCALCSIZE result at 150 %: side border 11 px, caption + top border 45 px.
    private const int OrigLeft = 100, OrigTop = 200, DefaultLeft = 111;

    [TestMethod]
    public void NormalWindowKeepsTheOriginalTopEdge() =>
        Assert.AreEqual(OrigTop, WindowFrameGeometry.AdjustedClientTop(OrigTop, OrigLeft, DefaultLeft, isMaximized: false, hasCaptionStyle: true));

    [TestMethod]
    public void MaximizedWindowIsInsetByTheBorderSoTheTopIsNotClipped() =>
        Assert.AreEqual(OrigTop + 11, WindowFrameGeometry.AdjustedClientTop(OrigTop, OrigLeft, DefaultLeft, isMaximized: true, hasCaptionStyle: true));

    [TestMethod]
    public void WindowWithoutCaptionStyleIsLeftToTheSystem() =>
        Assert.IsNull(WindowFrameGeometry.AdjustedClientTop(OrigTop, OrigLeft, DefaultLeft, isMaximized: false, hasCaptionStyle: false));

    [TestMethod]
    public void NegativeBorderFromAnUnexpectedDefaultIsClampedToZero() =>
        Assert.AreEqual(OrigTop, WindowFrameGeometry.AdjustedClientTop(OrigTop, OrigLeft, OrigLeft - 5, isMaximized: true, hasCaptionStyle: true));

    [TestMethod]
    public void ContainsIncludesTheTopLeftAndExcludesTheBottomRightEdge()
    {
        Assert.IsTrue(WindowFrameGeometry.Contains(10, 20, 46, 32, 10, 20));
        Assert.IsTrue(WindowFrameGeometry.Contains(10, 20, 46, 32, 55, 51));
        Assert.IsFalse(WindowFrameGeometry.Contains(10, 20, 46, 32, 56, 30));
        Assert.IsFalse(WindowFrameGeometry.Contains(10, 20, 46, 32, 30, 52));
        Assert.IsFalse(WindowFrameGeometry.Contains(10, 20, 46, 32, 9, 30));
    }

    [TestMethod]
    public void EmptyRectContainsNothing() =>
        Assert.IsFalse(WindowFrameGeometry.Contains(10, 20, 0, 32, 10, 20));

    [TestMethod]
    public void ScreenPointFromLParamKeepsNegativeMultiMonitorCoordinates()
    {
        // lParam packs x (low word) and y (high word) as signed 16-bit values.
        short x16 = -1200, y16 = -300;
        long lParam = ((long)unchecked((ushort)y16) << 16) | unchecked((ushort)x16);
        var (x, y) = WindowFrameGeometry.PointFromLParam(new IntPtr(lParam));
        Assert.AreEqual(-1200, x);
        Assert.AreEqual(-300, y);
    }
}
