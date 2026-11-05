using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class OcrServiceTests
{
    [TestMethod]
    public void WordCoordinatesUseTheDecodedBitmapDimensions()
    {
        var word = OcrService.NormalizeWordRegion("SPAN", new Windows.Foundation.Rect(100, 40, 200, 60),
            1000, 200, 2, 3);
        Assert.AreEqual(0.1, word.X, 0.000001);
        Assert.AreEqual(0.2, word.Y, 0.000001);
        Assert.AreEqual(0.2, word.Width, 0.000001);
        Assert.AreEqual(0.3, word.Height, 0.000001);
        Assert.AreEqual("SPAN", word.Text);
        Assert.AreEqual(2, word.LineIndex);
        Assert.AreEqual(3, word.WordIndex);
    }

    [TestMethod]
    public void WordCoordinatesClipEdgesInsteadOfMovingOverflowInsideTheImage()
    {
        var word = OcrService.NormalizeWordRegion("edge", new Windows.Foundation.Rect(-10, 90, 40, 30),
            100, 100, 0, 0);
        Assert.AreEqual(0, word.X, 0.000001);
        Assert.AreEqual(0.9, word.Y, 0.000001);
        Assert.AreEqual(0.3, word.Width, 0.000001);
        Assert.AreEqual(0.1, word.Height, 0.000001);

        var outside = OcrService.NormalizeWordRegion("outside", new Windows.Foundation.Rect(110, 120, 40, 30),
            100, 100, 0, 1);
        Assert.AreEqual(1, outside.X, 0.000001);
        Assert.AreEqual(1, outside.Y, 0.000001);
        Assert.AreEqual(0, outside.Width, 0.000001);
        Assert.AreEqual(0, outside.Height, 0.000001);
    }

    [TestMethod]
    public void ScalingKeepsAspectRatioWithinEngineLimit()
    {
        Assert.AreEqual((2048u, 1024u), OcrService.GetScaledDimensions(8000, 4000, 2048));
        Assert.AreEqual((1024u, 2048u), OcrService.GetScaledDimensions(4000, 8000, 2048));
    }

    [TestMethod]
    public void ScalingDoesNotUpscaleSmallImagesOrProduceZeroDimensions()
    {
        Assert.AreEqual((320u, 240u), OcrService.GetScaledDimensions(320, 240, 2048));
        Assert.AreEqual((2048u, 1u), OcrService.GetScaledDimensions(100000, 1, 2048));
    }

    [TestMethod]
    public void ScalingRejectsInvalidDimensions()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => OcrService.GetScaledDimensions(0, 4, 2048));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => OcrService.GetScaledDimensions(double.NaN, 4, 2048));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => OcrService.GetScaledDimensions(4, double.PositiveInfinity, 2048));
    }

    [TestMethod]
    public async Task EmptyPathAndNegativePdfPageAreRejectedBeforeOpeningFiles()
    {
        var service = new OcrService();
        Assert.AreEqual(OcrError.InvalidSource, (await service.RecognizeImageAsync("", CancellationToken.None)).Error);
        Assert.AreEqual(OcrError.InvalidPage, (await service.RecognizePdfPageAsync("unused.pdf", -1, CancellationToken.None)).Error);
    }

    [TestMethod]
    public async Task AlreadyCanceledRequestDoesNotLoadTheFileOrEngine()
    {
        var service = new OcrService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => service.RecognizeImageAsync("unused.png", cts.Token));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => service.RecognizePdfPageAsync("unused.pdf", 0, cts.Token));
    }
}
