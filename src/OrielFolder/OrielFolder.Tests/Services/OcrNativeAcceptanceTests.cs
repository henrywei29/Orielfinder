using OrielFolder.Services;

namespace OrielFolder.Tests.Services;

[TestClass]
public class OcrNativeAcceptanceTests
{
    [TestMethod]
    [TestCategory("NativeAcceptance")]
    public async Task SyntheticChineseEnglishImageIsRecognizedByInstalledEngine()
    {
        var fixture = Environment.GetEnvironmentVariable("SPAN_NATIVE_OCR_FIXTURE");
        if (string.IsNullOrWhiteSpace(fixture))
            Assert.Inconclusive("Set SPAN_NATIVE_OCR_FIXTURE to the synthetic acceptance image to exercise installed OCR.");
        var result = await new OcrService().RecognizeImageAsync(fixture!, CancellationToken.None);
        Assert.AreEqual(OcrError.None, result.Error);
        string text = result.Text.Replace(" ", "");
        StringAssert.Contains(text, "SPAN");
        StringAssert.Contains(text, "123");
        StringAssert.Contains(text, "本地文字");
        StringAssert.Contains(text, "测试成功");
        AssertWordGeometry(result);
    }

    [TestMethod]
    [TestCategory("NativeAcceptance")]
    public async Task ScannedPdfPageUsesTheRequestedPage()
    {
        var fixture = Environment.GetEnvironmentVariable("SPAN_NATIVE_OCR_PDF");
        if (string.IsNullOrWhiteSpace(fixture))
            Assert.Inconclusive("Set SPAN_NATIVE_OCR_PDF to the synthetic three-page acceptance PDF.");
        var result = await new OcrService().RecognizePdfPageAsync(fixture!, 1, CancellationToken.None);
        Assert.AreEqual(OcrError.None, result.Error);
        StringAssert.Contains(result.Text.Replace(" ", ""), "SPANDevOCRTest123");
        Assert.IsFalse(result.Text.Contains("third page", StringComparison.OrdinalIgnoreCase));
        AssertWordGeometry(result);
    }

    private static void AssertWordGeometry(OcrRecognitionResult result)
    {
        Assert.IsTrue(result.PixelWidth > 0 && result.PixelHeight > 0);
        Assert.IsNotNull(result.Words);
        Assert.IsTrue(result.Words.Count > 0);
        int lineIndex = -1;
        int wordIndex = -1;
        foreach (var word in result.Words)
        {
            Assert.IsTrue(word.X >= 0 && word.Y >= 0 && word.Width > 0 && word.Height > 0);
            Assert.IsTrue(word.X + word.Width <= 1.000001 && word.Y + word.Height <= 1.000001);
            Assert.IsTrue(word.LineIndex >= lineIndex);
            Assert.AreEqual(word.LineIndex == lineIndex ? wordIndex + 1 : 0, word.WordIndex);
            lineIndex = word.LineIndex;
            wordIndex = word.WordIndex;
        }
        string Compact(string value) => System.Text.RegularExpressions.Regex.Replace(value, @"\s", "");
        Assert.AreEqual(Compact(result.Text), Compact(string.Concat(result.Words.Select(word => word.Text))));
    }
}
