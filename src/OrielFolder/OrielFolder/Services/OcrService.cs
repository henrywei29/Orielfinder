using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OrielFolder.Services;

public enum OcrError { None, InvalidSource, InvalidPage, FileTooLarge, NoLanguage, Failed }

public sealed record OcrWordRegion(string Text, double X, double Y, double Width, double Height, int LineIndex, int WordIndex);

public sealed record OcrRecognitionResult(string Text, string? LanguageTag = null, OcrError Error = OcrError.None,
    IReadOnlyList<OcrWordRegion>? Words = null, uint PixelWidth = 0, uint PixelHeight = 0);

public sealed class OcrService
{
    private const long MaximumFileBytes = 100 * 1024 * 1024;

    public Task<OcrRecognitionResult> RecognizeImageAsync(string path, CancellationToken cancellationToken) =>
        RecognizeAsync(path, null, cancellationToken);

    public Task<OcrRecognitionResult> RecognizePdfPageAsync(string path, int pageIndex, CancellationToken cancellationToken) =>
        RecognizeAsync(path, pageIndex, cancellationToken);

    private static async Task<OcrRecognitionResult> RecognizeAsync(string path, int? pageIndex, CancellationToken ct)
    {
        string stage = "source";
        ct.ThrowIfCancellationRequested();
        if (pageIndex < 0) return new("", Error: OcrError.InvalidPage);
        if (string.IsNullOrWhiteSpace(path)) return new("", Error: OcrError.InvalidSource);

        try
        {
            // File access and native OCR stay off the UI thread. The source handle is
            // released before decoding; OCR never writes the file or a temporary copy.
            return await Task.Run(async () =>
            {
                byte[] bytes;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (file.Length > MaximumFileBytes) return new OcrRecognitionResult("", Error: OcrError.FileTooLarge);
                    if (file.Length == 0) return new OcrRecognitionResult("", Error: OcrError.InvalidSource);
                    bytes = new byte[(int)file.Length];
                    await file.ReadExactlyAsync(bytes, ct);
                }
                ct.ThrowIfCancellationRequested();
                stage = "engine";
                var engine = CreateEngine();
                if (engine == null) return new OcrRecognitionResult("", Error: OcrError.NoLanguage);

                using var memory = new MemoryStream(bytes, writable: false);
                using var source = memory.AsRandomAccessStream();
                if (pageIndex.HasValue)
                {
                    var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(ct);
                    if ((uint)pageIndex.Value >= document.PageCount)
                        return new OcrRecognitionResult("", Error: OcrError.InvalidPage);
                    using var page = document.GetPage((uint)pageIndex.Value);
                    using var rendered = new InMemoryRandomAccessStream();
                    // Two pixels per PDF point gives useful text detail while keeping
                    // even large pages inside the installed engine's supported size.
                    var dimensions = GetScaledDimensions(page.Size.Width * 2, page.Size.Height * 2, OcrEngine.MaxImageDimension);
                    await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions
                    {
                        DestinationWidth = dimensions.Width,
                        DestinationHeight = dimensions.Height,
                        BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                    }).AsTask(ct);
                    rendered.Seek(0);
                    stage = "pdf-bitmap";
                    return await RecognizeBitmapAsync(rendered, engine, ct);
                }
                stage = "image-bitmap";
                return await RecognizeBitmapAsync(source, engine, ct);
            }, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return new("", Error: OcrError.InvalidSource); }
        catch (UnauthorizedAccessException) { return new("", Error: OcrError.InvalidSource); }
        catch (Exception error)
        {
            Helpers.DebugLogger.Log($"[OCR] stage={stage}; error={error.GetType().Name}; code=0x{error.HResult:X8}");
            return new("", Error: OcrError.Failed);
        }
    }

    private static OcrEngine? CreateEngine()
    {
        var profileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        // The installed Chinese recognizer also handles Latin text. Prefer it for
        // mixed Chinese/English pages, then retain the user's language or English.
        var chinese = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(
            language => language.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        if (chinese != null)
        {
            var mixedEngine = OcrEngine.TryCreateFromLanguage(chinese);
            if (mixedEngine != null) return mixedEngine;
        }
        if (profileEngine != null) return profileEngine;
        var english = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(
            language => language.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        return english == null ? null : OcrEngine.TryCreateFromLanguage(english);
    }

    private static async Task<OcrRecognitionResult> RecognizeBitmapAsync(IRandomAccessStream stream, OcrEngine engine, CancellationToken ct)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        var dimensions = GetScaledDimensions(decoder.PixelWidth, decoder.PixelHeight, OcrEngine.MaxImageDimension);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform { ScaledWidth = dimensions.Width, ScaledHeight = dimensions.Height },
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct);
        ct.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        ct.ThrowIfCancellationRequested();
        // EXIF orientation can swap axes. Word bounds belong to the exact bitmap
        // given to the engine, rather than the decoder's original dimensions.
        uint pixelWidth = (uint)bitmap.PixelWidth;
        uint pixelHeight = (uint)bitmap.PixelHeight;
        var words = new List<OcrWordRegion>();
        for (int lineIndex = 0; lineIndex < result.Lines.Count; lineIndex++)
        {
            var line = result.Lines[lineIndex];
            for (int wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var word = line.Words[wordIndex];
                words.Add(NormalizeWordRegion(word.Text, word.BoundingRect, pixelWidth, pixelHeight,
                    lineIndex, wordIndex));
            }
        }
        return new OcrRecognitionResult(string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)),
            engine.RecognizerLanguage.LanguageTag, Words: words.AsReadOnly(), PixelWidth: pixelWidth, PixelHeight: pixelHeight);
    }

    internal static OcrWordRegion NormalizeWordRegion(string text, Windows.Foundation.Rect bounds,
        uint pixelWidth, uint pixelHeight, int lineIndex, int wordIndex)
    {
        if (pixelWidth == 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight == 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        double left = Math.Clamp(bounds.X / pixelWidth, 0, 1);
        double top = Math.Clamp(bounds.Y / pixelHeight, 0, 1);
        double right = Math.Clamp((bounds.X + bounds.Width) / pixelWidth, 0, 1);
        double bottom = Math.Clamp((bounds.Y + bounds.Height) / pixelHeight, 0, 1);
        return new(text, left, top, Math.Max(0, right - left), Math.Max(0, bottom - top), lineIndex, wordIndex);
    }

    internal static (uint Width, uint Height) GetScaledDimensions(double width, double height, uint maximum)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (maximum == 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        var scale = Math.Min(1, maximum / Math.Max(width, height));
        return ((uint)Math.Max(1, Math.Floor(width * scale)), (uint)Math.Max(1, Math.Floor(height * scale)));
    }
}
