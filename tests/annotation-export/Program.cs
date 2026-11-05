using System.Drawing;
using System.Drawing.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Span.Models;
using Span.Services;

var root = Path.Combine(@"D:\codex\Orielfolder-development\annotation-export-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
Directory.CreateDirectory(root);
var service = new PreviewAnnotationExportService(Path.Combine(root, "exports"));
var tests = new List<(string Name, Func<Task> Run)>
{
    ("PDF preserves all pages, original content, source bytes and adds vector marks", PdfPreservation),
    ("Colliding exports keep both previous files and choose numbered name", Collision),
    ("PNG pen, rectangle, highlighter and Chinese text render without source writes", ImageTools),
    ("EXIF orientation is applied before drawing", ExifOrientation),
    ("Full long image is embedded on a proportional PDF page", LongImage),
    ("Cropped and rotated PDF annotations align with the rendered page", PdfRotationAndCrop),
    ("PDF rectangle, translucent highlighter and Chinese text render", PdfTools),
    ("Invalid annotation coordinates and page indices are rejected", Validation),
    ("Cancellation creates no export", Cancellation),
    ("WIC snapshot applies EXIF and is independent of the original", WicSnapshot),
    ("WIC long-image snapshot preserves all rows and respects pixel limits", WicLongImage),
    ("WIC snapshot disposal removes only its own PNG and empty folder", WicDisposal),
    ("WIC cancellation leaves no new snapshot folders", WicCancellation),
    ("Rotated PNG includes rotated annotations and unchanged original", RotatedImage),
    ("Rotated image PDF matches the rotated full image aspect", RotatedImagePdf),
    ("mosaicRect changes actual PNG and image PDF pixels only inside the mask", () => ImageMask("mosaicRect")),
    ("mosaicPen changes actual PNG and image PDF pixels only inside the brush", () => ImageMask("mosaicPen")),
    ("redactRect writes opaque black PNG and image PDF pixels", () => ImageMask("redactRect")),
    ("redactPen writes opaque black PNG and image PDF pixels", () => ImageMask("redactPen")),
    ("mosaicRect PDF page contains only flattened masked pixels", () => PdfMask("mosaicRect")),
    ("mosaicPen PDF page contains only flattened masked pixels", () => PdfMask("mosaicPen")),
    ("redactRect PDF page contains only flattened masked pixels", () => PdfMask("redactRect")),
    ("redactPen PDF page contains only flattened masked pixels", () => PdfMask("redactPen")),
    ("Masks and ordinary marks keep their order and rotate with EXIF images", MaskOrderAndOrientation),
    ("Flattened PDF masks preserve all cropped and rotated displayed pages", MaskedPdfGeometry),
    ("Mask validation rejects malformed shapes, width and coordinates without exports", MaskValidation),
    ("Cancelled masked PDF and PNG exports create no files", MaskCancellation),
    ("PNG ordinary tools honor explicit opacity and legacy defaults", ImageOpacity),
    ("Explicit 30 percent highlighter matches legacy alpha rounding", HighlighterDefaultOpacity),
    ("Vector PDF and image PDF preserve drawing opacity in alpha state and rendered pixels", PdfOpacity),
    ("Flattened masked PDF retains ordinary drawing opacity", FlattenedPdfOpacity),
    ("All masks remain opaque at zero requested opacity in PNG and PDF", MaskOpacity),
    ("Invalid opacity is rejected before PNG or PDF exports", OpacityValidation),
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); }
}
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed; artifacts: {root}");
return failed == 0 ? 0 : 1;

PreviewAnnotation Mark(string tool = "pen", int page = 0) => new()
{
    Tool = tool, Color = "#e53935", Width = .025, PageIndex = page,
    Points = new() { new() { X = .1, Y = .2 }, new() { X = .8, Y = .7 } }, Text = "中文标记"
};
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
string ImageFile(string name, int width = 200, int height = 100)
{
    var path = Path.Combine(root, name);
    using var image = new Bitmap(width, height);
    using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Color.White); graphics.FillRectangle(Brushes.Blue, 0, height - 10, width, 10); }
    image.Save(path, name.EndsWith("jpg") ? ImageFormat.Jpeg : ImageFormat.Png);
    return path;
}
string PdfFile(string name)
{
    var path = Path.Combine(root, name);
    using var pdf = new PdfDocument();
    pdf.Options.CompressContentStreams = false;
    foreach (var word in new[] { "Original page one", "Original page two", "Original page three" })
    {
        var page = pdf.AddPage(); page.Width = XUnit.FromPoint(300); page.Height = XUnit.FromPoint(400);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString(word, new XFont("Arial", 14, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.WinAnsi)), XBrushes.Black, 20, 30);
    }
    pdf.Save(path);
    return path;
}
byte[] Contents(PdfPage page) => page.Contents.Elements.GetObject(0) is PdfDictionary stream ? stream.Stream.UnfilteredValue : Array.Empty<byte>();
async Task PdfPreservation()
{
    var path = PdfFile("preserve.pdf"); var before = File.ReadAllBytes(path);
    using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    var output = await service.ExportPdfAsync("preserve.pdf", path, new[] { Mark(page: 1) });
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 3, "Page count changed");
    for (var i = 0; i < 3; i++) Check(Contents(source.Pages[i]).SequenceEqual(Contents(pdf.Pages[i])), "Existing page text/content changed");
    Check(pdf.Pages[1].Contents.Elements.Count > source.Pages[1].Contents.Elements.Count, "No appended vector annotation stream");
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "Original PDF changed");
}
async Task Collision()
{
    var source = PdfFile("collision.pdf");
    var first = await service.ExportPdfAsync("collision.pdf", source, Array.Empty<PreviewAnnotation>()); var firstBytes = File.ReadAllBytes(first);
    var second = await service.ExportPdfAsync("collision.pdf", source, new[] { Mark() });
    Check(Path.GetFileName(first) == "collision_标记.pdf", "Wrong first name");
    Check(Path.GetFileName(second) == "collision_标记 (2).pdf", "Wrong collision name");
    Check(firstBytes.SequenceEqual(File.ReadAllBytes(first)), "Existing export overwritten");
    var safe = await service.ExportPdfAsync("bad:name?.docx", source, Array.Empty<PreviewAnnotation>());
    Check(Path.GetFileName(safe) == "bad_name__标记.pdf", "Unsafe source name not sanitized");
}
async Task ImageTools()
{
    var path = ImageFile("tools.png"); var before = File.ReadAllBytes(path);
    foreach (var tool in new[] { "pen", "rect", "highlighter", "text" })
    {
        var mark = Mark(tool); if (tool == "text") mark.Points.RemoveAt(1);
        var bytes = await service.RenderImagePngAsync(path, new[] { mark });
        using var stream = new MemoryStream(bytes); using var actual = new Bitmap(stream); using var original = new Bitmap(path);
        var changed = 0; for (int x = 0; x < actual.Width; x++) for (int y = 0; y < actual.Height; y++) if (actual.GetPixel(x, y) != original.GetPixel(x, y)) changed++;
        Check(actual.Width == 200 && actual.Height == 100 && changed > 10, tool + " produced no pixels");
        if (tool == "highlighter") Check(actual.GetPixel(20, 20).G > 100, "Highlighter is opaque");
        File.WriteAllBytes(Path.Combine(root, tool + ".png"), bytes);
    }
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "Original image changed");
}
async Task ExifOrientation()
{
    var path = ImageFile("orientation.jpg", 40, 80); var jpeg = File.ReadAllBytes(path);
    // APP1 Exif, TIFF little endian, Orientation=6 (90 degrees clockwise).
    byte[] exif = { 0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 };
    File.WriteAllBytes(path, jpeg.Take(2).Concat(exif).Concat(jpeg.Skip(2)).ToArray());
    var before = File.ReadAllBytes(path);
    using var stream = new MemoryStream(await service.RenderImagePngAsync(path, Array.Empty<PreviewAnnotation>())); using var image = new Bitmap(stream);
    Check(image.Width == 80 && image.Height == 40, "Orientation ignored");
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "EXIF source changed");
}
async Task LongImage()
{
    var path = ImageFile("long.png", 100, 30000); var before = File.ReadAllBytes(path);
    var mark = Mark(); mark.Points = new() { new() { X = .1, Y = .99 }, new() { X = .9, Y = .99 } };
    var output = await service.ExportImagePdfAsync("long.png", path, new[] { mark });
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 1, "Long image was split"); var page = pdf.Pages[0];
    Check(Math.Abs(page.Height.Point / page.Width.Point - 300) < .001, "Long image aspect ratio clipped or distorted");
    Check(page.Height.Point <= 14400, "PDF page exceeds maximum height");
    var objects = page.Resources.Elements.GetDictionary("/XObject")!;
    var embedded = objects.Elements.Values.OfType<PdfSharp.Pdf.Advanced.PdfReference>().Select(x => x.Value).OfType<PdfDictionary>().First(x => x.Elements.GetName("/Subtype") == "/Image");
    Check(embedded.Elements.GetInteger("/Height") == 30000 && embedded.Elements.GetInteger("/Width") == 100, "Embedded image truncated");
    Check(before.SequenceEqual(File.ReadAllBytes(path)), "Long source image changed");
}
async Task PdfRotationAndCrop()
{
    var source = Path.Combine(root, "rotated.pdf");
    using (var document = new PdfDocument())
    {
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var page = document.AddPage();
            page.MediaBox = new PdfRectangle(new XPoint(20, 30), new XPoint(320, 430));
            page.CropBox = new PdfRectangle(new XPoint(50, 70), new XPoint(250, 370));
            page.Rotate = rotation;
        }
        document.Save(source);
    }
    var marks = Enumerable.Range(0, 4).Select(i => new PreviewAnnotation
    {
        PageIndex = i, Color = "#ff0000", Width = .02,
        Points = new() { new() { X = .2, Y = .3 }, new() { X = .8, Y = .3 } }
    }).ToArray();
    var output = await service.ExportPdfAsync("rotated.pdf", source, marks);
    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(output);
    var rendered = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
    for (uint i = 0; i < rendered.PageCount; i++)
    {
        using var page = rendered.GetPage(i);
        using var pixels = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(pixels, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 600 });
        pixels.Seek(0);
        using var stream = pixels.AsStreamForRead(); using var bitmap = new Bitmap(stream);
        var red = bitmap.GetPixel(bitmap.Width / 2, (int)(bitmap.Height * .3));
        Check(red.R > 220 && red.G < 50 && red.B < 50, $"Page {i} annotation is misplaced: {red}");
        bitmap.Save(Path.Combine(root, $"rotation-{i * 90}.png"), ImageFormat.Png);
    }
}
async Task PdfTools()
{
    var source = PdfFile("pdf-tools.pdf");
    var rectangle = Mark("rect"); rectangle.Points = new() { new() { X = .1, Y = .4 }, new() { X = .9, Y = .6 } };
    var highlighter = Mark("highlighter"); highlighter.Points = new() { new() { X = .1, Y = .8 }, new() { X = .9, Y = .8 } };
    var text = Mark("text"); text.Width = .01; text.Points = new() { new() { X = .1, Y = .2 } };
    var output = await service.ExportPdfAsync("pdf-tools.pdf", source, new[] { rectangle, highlighter, text });
    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(output);
    var document = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
    using var page = document.GetPage(0);
    using var pixels = new Windows.Storage.Streams.InMemoryRandomAccessStream();
    await page.RenderToStreamAsync(pixels, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 600 });
    pixels.Seek(0);
    using var stream = pixels.AsStreamForRead(); using var bitmap = new Bitmap(stream);
    var border = bitmap.GetPixel((int)(bitmap.Width * .1), bitmap.Height / 2);
    var highlight = bitmap.GetPixel(bitmap.Width / 2, (int)(bitmap.Height * .8));
    Check(border.R > 200 && border.G < 100, "PDF rectangle missing");
    Check(highlight.R > 220 && highlight.G > 100 && highlight.G < 230, "PDF highlighter missing or opaque");
    var textPixels = 0;
    for (var x = (int)(bitmap.Width * .1); x < bitmap.Width / 2; x++)
        for (var y = (int)(bitmap.Height * .2); y < (int)(bitmap.Height * .3); y++)
            if (bitmap.GetPixel(x, y).G < 150) textPixels++;
    Check(textPixels > 30, "Chinese PDF text did not render");
    bitmap.Save(Path.Combine(root, "pdf-tools.png"), ImageFormat.Png);
}
async Task Validation()
{
    var image = ImageFile("validation.png");
    foreach (var value in new[] { double.NaN, double.PositiveInfinity, -.01, 1.01 })
    {
        var mark = Mark(); mark.Points[0].X = value;
        try { await service.RenderImagePngAsync(image, new[] { mark }); throw new InvalidOperationException("Invalid coordinate accepted"); } catch (ArgumentException) { }
    }
    var invalidPage = Mark(page: 1);
    try { await service.RenderImagePngAsync(image, new[] { invalidPage }); throw new InvalidOperationException("Invalid page accepted"); } catch (ArgumentException) { }
}
async Task Cancellation()
{
    var source = PdfFile("cancel.pdf"); var before = Directory.GetFiles(Path.Combine(root, "exports")).Length;
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await service.ExportPdfAsync("cancel.pdf", source, new[] { Mark() }, cancellation.Token); throw new InvalidOperationException("Cancellation ignored"); } catch (OperationCanceledException) { }
    Check(Directory.GetFiles(Path.Combine(root, "exports")).Length == before, "Cancelled export created a file");
}
async Task WicSnapshot()
{
    var source = Path.Combine(root, "orientation.jpg"); var original = File.ReadAllBytes(source);
    using var snapshot = await ImagePreviewSnapshot.CreateAsync(source, CancellationToken.None);
    Check(snapshot.Width == 80 && snapshot.Height == 40, "WIC ignored EXIF orientation");
    var bytes = File.ReadAllBytes(snapshot.Path);
    Check(original.SequenceEqual(File.ReadAllBytes(source)), "WIC changed the source");
    File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
    Check(bytes.SequenceEqual(File.ReadAllBytes(snapshot.Path)), "Snapshot depends on later original changes");
    using var image = new Bitmap(snapshot.Path);
    Check(image.Width == 80 && image.Height == 40, "Snapshot PNG dimensions disagree");
}
async Task WicLongImage()
{
    var source = ImageFile("wic-long.png", 100, 40000);
    using var snapshot = await ImagePreviewSnapshot.CreateAsync(source, CancellationToken.None);
    Check(snapshot.Width * (ulong)snapshot.Height <= 16_000_000 && Math.Max(snapshot.Width, snapshot.Height) <= 32768, "Snapshot dimension cap exceeded");
    Check(Math.Abs(snapshot.Height / (double)snapshot.Width - 400) < 5, "Long image was cropped or distorted");
    using var image = new Bitmap(snapshot.Path);
    var bottom = image.GetPixel(image.Width / 2, image.Height - 2);
    Check(bottom.B > 200 && bottom.R < 100, "Bottom of full long image was clipped");
    var large = ImageFile("wic-large.png", 5000, 4000);
    using var bounded = await ImagePreviewSnapshot.CreateAsync(large, CancellationToken.None);
    Check(bounded.Width * (ulong)bounded.Height <= 16_000_000, "Pixel cap exceeded");
    Check(Math.Abs(bounded.Width / (double)bounded.Height - 1.25) < .001, "Pixel-bounded image aspect changed");
}
async Task WicDisposal()
{
    var source = ImageFile("wic-dispose.png");
    var first = await ImagePreviewSnapshot.CreateAsync(source, CancellationToken.None); var firstPath = first.Path;
    using var second = await ImagePreviewSnapshot.CreateAsync(source, CancellationToken.None);
    first.Dispose(); first.Dispose();
    Check(!File.Exists(firstPath) && !Directory.Exists(Path.GetDirectoryName(firstPath)), "Disposed snapshot remains");
    Check(File.Exists(second.Path), "Disposal swept another snapshot");
    var guarded = await ImagePreviewSnapshot.CreateAsync(source, CancellationToken.None); var guardedPath = guarded.Path;
    var foreign = Path.Combine(Path.GetDirectoryName(guardedPath)!, "unrelated.txt"); File.WriteAllText(foreign, "keep");
    guarded.Dispose();
    Check(!File.Exists(guardedPath) && File.ReadAllText(foreign) == "keep", "Cleanup touched unrelated content");
}
async Task WicCancellation()
{
    var cache = @"D:\codex\Orielfolder-development\preview-cache\images";
    var before = Directory.Exists(cache) ? Directory.GetDirectories(cache) : Array.Empty<string>();
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { using var snapshot = await ImagePreviewSnapshot.CreateAsync(Path.Combine(root, "tools.png"), cancellation.Token); throw new InvalidOperationException("WIC ignored cancellation"); } catch (OperationCanceledException) { }
    var after = Directory.Exists(cache) ? Directory.GetDirectories(cache) : Array.Empty<string>();
    Check(!after.Except(before).Any(), "Cancelled WIC snapshot left folders");
}
async Task RotatedImage()
{
    var source = ImageFile("rotated-image.png", 200, 100); var before = File.ReadAllBytes(source);
    var mark = Mark(); mark.Color = "#ff0000"; mark.Width = .04; mark.Points = new() { new() { X = .2, Y = .3 } };
    using var stream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { mark }, CancellationToken.None, 90));
    using var bitmap = new Bitmap(stream);
    Check(bitmap.Width == 100 && bitmap.Height == 200, "PNG rotation dimensions wrong");
    var dot = bitmap.GetPixel(69, 40); Check(dot.R > 220 && dot.G < 40, "Annotation did not rotate with the image");
    Check(before.SequenceEqual(File.ReadAllBytes(source)), "Rotation changed original image");
}
async Task RotatedImagePdf()
{
    var source = ImageFile("rotated-pdf.png", 100, 3000);
    var output = await service.ExportImagePdfAsync("rotated-pdf.png", source, new[] { Mark() }, CancellationToken.None, 90);
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import); var page = pdf.Pages[0];
    Check(Math.Abs(page.Width.Point / page.Height.Point - 30) < .001, "Rotated PDF image aspect is wrong");
}

PreviewAnnotation Mask(string tool, int page = 0) => new()
{
    Tool = tool, Color = "#00ff00", Width = .025, PageIndex = page,
    Points = tool.EndsWith("Rect")
        ? new() { new() { X = .25, Y = .25 }, new() { X = .75, Y = .75 } }
        : new() { new() { X = .25, Y = .5 }, new() { X = .75, Y = .5 } }
};
string PatternImage(string name)
{
    var path = Path.Combine(root, name);
    using var image = new Bitmap(200, 100);
    for (var y = 0; y < 100; y++) for (var x = 0; x < 200; x++)
        image.SetPixel(x, y, ((x + y) % 2 == 0) ? Color.White : Color.Blue);
    image.Save(path, ImageFormat.Png);
    return path;
}
async Task<Bitmap> RenderPdf(string path, uint index = 0, uint width = 600)
{
    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
    var document = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
    using var page = document.GetPage(index);
    using var pixels = new Windows.Storage.Streams.InMemoryRandomAccessStream();
    await page.RenderToStreamAsync(pixels, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = width });
    pixels.Seek(0);
    using var stream = pixels.AsStreamForRead(); using var image = new Bitmap(stream);
    return new Bitmap(image);
}
void CheckMaskPixel(Bitmap image, string tool, int x, int y)
{
    var color = image.GetPixel(x, y);
    if (tool.StartsWith("redact"))
        Check(color.A == 255 && Math.Abs(color.R - 17) <= 2 && Math.Abs(color.G - 17) <= 2 && Math.Abs(color.B - 17) <= 2, tool + " is not opaque #111111: " + color);
    else
        Check(color.R > 40 && color.R < 215 && Math.Abs(color.R - color.G) < 3 && color.B > 240, tool + " did not replace source detail with averaged block pixels: " + color);
}
async Task ImageMask(string tool)
{
    var source = PatternImage(tool + "-source.png"); var original = File.ReadAllBytes(source);
    var mark = Mask(tool);
    var bytes = await service.RenderImagePngAsync(source, new[] { mark });
    using var stream = new MemoryStream(bytes); using var bitmap = new Bitmap(stream); using var untouched = new Bitmap(source);
    CheckMaskPixel(bitmap, tool, 100, 50);
    Check(bitmap.GetPixel(5, 5) == untouched.GetPixel(5, 5), "Pixels outside mask changed");
    Check(bitmap.GetPixel(100, 5) == untouched.GetPixel(100, 5), "Brush escaped its shape");
    if (tool.EndsWith("Pen")) CheckMaskPixel(bitmap, tool, 100, 65); // .025 * 200 * 8 = 40px diameter.
    File.WriteAllBytes(Path.Combine(root, tool + "-clipboard.png"), bytes);
    var output = await service.ExportImagePdfAsync(tool + ".png", source, new[] { mark });
    using var rendered = await RenderPdf(output, width: 200);
    CheckMaskPixel(rendered, tool, 100, 50);
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    Check(pdf.Pages[0].Contents.Elements.Count == 1, "Image PDF added a recoverable overlay mask");
    var objects = pdf.Pages[0].Resources.Elements.GetDictionary("/XObject")!;
    Check(objects.Elements.Count == 1, "Image PDF retained an additional unmasked image");
    Check(original.SequenceEqual(File.ReadAllBytes(source)), "Mask export modified source image");
    // Single point brushes must create an opaque dot, including the zero-length path.
    if (tool.EndsWith("Pen"))
    {
        mark.Points.RemoveAt(1);
        using var dotStream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { mark }));
        using var dot = new Bitmap(dotStream); CheckMaskPixel(dot, tool, 50, 50);
    }
}
async Task PdfMask(string tool)
{
    const string secret = "MASKED_SECRET_7419";
    var path = Path.Combine(root, tool + "-source.pdf");
    using (var document = new PdfDocument())
    {
        document.Options.CompressContentStreams = false;
        document.Info.Title = secret; document.Info.Subject = secret;
        using var attachment = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(secret));
        document.AddEmbeddedFile("unredacted.txt", attachment);
        var page = document.AddPage(); page.Width = XUnit.FromPoint(200); page.Height = XUnit.FromPoint(100);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            using var pattern = XImage.FromFile(PatternImage(tool + "-pdf-pattern.png"));
            graphics.DrawImage(pattern, 0, 0, 200, 100);
            graphics.DrawString(secret, new XFont("Arial", 8), XBrushes.Black, 52, 51);
        }
        var other = document.AddPage(); other.Width = XUnit.FromPoint(300); other.Height = XUnit.FromPoint(400);
        using (var graphics = XGraphics.FromPdfPage(other)) graphics.DrawString("KEEP_UNMASKED", new XFont("Arial", 12), XBrushes.Black, 20, 30);
        document.Save(path);
    }
    var original = File.ReadAllBytes(path);
    using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    var output = await service.ExportPdfAsync(tool + ".pdf", path, new[] { Mask(tool) });
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    Check(pdf.PageCount == 2, "Masked export lost another page");
    Check(Contents(source.Pages[1]).SequenceEqual(Contents(pdf.Pages[1])), "Unmasked page stream changed");
    Check(!pdf.Pages[0].Resources.Elements.ContainsKey("/Font"), "Masked PDF retained text fonts");
    Check(pdf.Pages[0].Resources.Elements.GetDictionary("/XObject")!.Elements.Count == 1, "Masked page did not become one image");
    foreach (var obj in pdf.Internals.GetAllObjects().OfType<PdfDictionary>())
        if (obj.Stream != null)
            Check(!System.Text.Encoding.Latin1.GetString(obj.Stream.UnfilteredValue).Contains(secret), "Secret remains in a PDF stream");
    Check(!System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)).Contains(secret), "Secret retained in output bytes/metadata");
    Check(pdf.Info.Title != secret && pdf.Info.Subject != secret, "Source metadata retained");
    using var rendered = await RenderPdf(output, width: 600);
    var pixel = rendered.GetPixel(300, 170); // Away from fixture text, inside rectangle and 40px brush.
    if (tool.StartsWith("redact")) Check(pixel.R < 22 && pixel.G < 22 && pixel.B < 22, "PDF black mask missing");
    else Check(pixel.R > 40 && pixel.R < 215 && pixel.B > pixel.R + 40, "PDF mosaic missing");
    rendered.Save(Path.Combine(root, tool + "-flattened.png"), ImageFormat.Png);
    Check(original.SequenceEqual(File.ReadAllBytes(path)), "Mask export modified source PDF");
}
async Task MaskOrderAndOrientation()
{
    var source = ImageFile("mask-order.png");
    var pen = Mark(); pen.Color = "#ff0000"; pen.Width = .02;
    pen.Points = new() { new() { X = .3, Y = .5 }, new() { X = .7, Y = .5 } };
    var mask = Mask("redactRect");
    using var firstStream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { pen, mask }));
    using var first = new Bitmap(firstStream); CheckMaskPixel(first, "redactRect", 100, 50);
    using var lastStream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { mask, pen }));
    using var last = new Bitmap(lastStream); Check(last.GetPixel(100, 50).R > 240, "Marks were reordered around mask");
    // EXIF fixture from the earlier test is 80 x 40 after orientation.
    var oriented = Path.Combine(root, "mask-exif.jpg"); var jpegPath = ImageFile("mask-exif-base.jpg", 40, 80); var jpeg = File.ReadAllBytes(jpegPath);
    byte[] exif = { 0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 };
    File.WriteAllBytes(oriented, jpeg.Take(2).Concat(exif).Concat(jpeg.Skip(2)).ToArray());
    var original = File.ReadAllBytes(oriented);
    mask.Points = new() { new() { X = .1, Y = .1 }, new() { X = .3, Y = .3 } };
    using var rotatedStream = new MemoryStream(await service.RenderImagePngAsync(oriented, new[] { mask }, rotationDegrees: 90));
    using var rotated = new Bitmap(rotatedStream);
    Check(rotated.Width == 40 && rotated.Height == 80, "EXIF and display rotation not preserved");
    CheckMaskPixel(rotated, "redactRect", 31, 16);
    Check(original.SequenceEqual(File.ReadAllBytes(oriented)), "EXIF mask changed original");
}
async Task MaskedPdfGeometry()
{
    var source = Path.Combine(root, "masked-geometry.pdf");
    using (var document = new PdfDocument())
    {
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var page = document.AddPage();
            page.MediaBox = new PdfRectangle(new XPoint(20, 30), new XPoint(320, 430));
            page.CropBox = new PdfRectangle(new XPoint(50, 70), new XPoint(250, 370)); page.Rotate = rotation;
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(XBrushes.Blue, 0, 0, 400, 500);
            graphics.DrawRectangle(XBrushes.Blue, 50, 60, 40, 40);
            graphics.DrawString("GEOMETRY_SECRET", new XFont("Arial", 12), XBrushes.Black, 70, 150);
        }
        document.Save(source);
    }
    var annotations = Enumerable.Range(0, 4).SelectMany(i =>
    {
        var mask = Mask("redactRect", i); mask.Points = new() { new() { X = .4, Y = .4 }, new() { X = .6, Y = .6 } };
        var pen = Mark(page: i); pen.Color = "#ff0000"; pen.Width = .01;
        pen.Points = new() { new() { X = .4, Y = .5 }, new() { X = .6, Y = .5 } };
        return new[] { mask, pen };
    }).ToArray();
    var output = await service.ExportPdfAsync("masked-geometry.pdf", source, annotations);
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    for (uint i = 0; i < 4; i++)
    {
        using var before = await RenderPdf(source, i); using var after = await RenderPdf(output, i);
        Check(before.Width == after.Width && before.Height == after.Height, "Cropped rotated page dimensions changed");
        Check(Math.Abs(pdf.Pages[(int)i].Width.Point / pdf.Pages[(int)i].Height.Point - before.Width / (double)before.Height) < .001, "Flat page aspect changed");
        var black = after.GetPixel(after.Width / 2, (int)(after.Height * .45));
        Check(black.R < 22 && black.G < 22, "Mask did not align with displayed crop and rotation");
        var red = after.GetPixel(after.Width / 2, after.Height / 2);
        Check(red.R > 220 && red.G < 40, "Normal mark following mask lost or reordered");
        foreach (var fraction in new[] { .1, .9 })
        {
            var outside = before.GetPixel((int)(before.Width * fraction), (int)(before.Height * fraction));
            var untouched = after.GetPixel((int)(after.Width * fraction), (int)(after.Height * fraction));
            Check(Math.Abs(outside.R - untouched.R) < 4 && Math.Abs(outside.B - untouched.B) < 4, "Source crop content shifted at " + fraction);
        }
        before.Save(Path.Combine(root, $"mask-geometry-source-{i * 90}.png"), ImageFormat.Png);
        after.Save(Path.Combine(root, $"mask-geometry-{i * 90}.png"), ImageFormat.Png);
    }
}
async Task MaskValidation()
{
    var image = ImageFile("mask-validation.png");
    foreach (var tool in new[] { "mosaicRect", "mosaicPen", "redactRect", "redactPen" })
    {
        foreach (var invalid in new Action<PreviewAnnotation>[]
        {
            mark => mark.Points.Clear(), mark => mark.Points[0].X = double.NaN,
            mark => mark.Points[0].Y = double.PositiveInfinity, mark => mark.Points[0].X = -.01,
            mark => mark.Points[0].Y = 1.01, mark => mark.Width = double.NaN,
            mark => mark.Width = 0, mark => mark.Width = 1.01, mark => mark.PageIndex = 1,
        })
        {
            var mark = Mask(tool); invalid(mark);
            try { await service.RenderImagePngAsync(image, new[] { mark }); throw new InvalidOperationException("Invalid mask accepted"); } catch (ArgumentException) { }
        }
        if (tool.EndsWith("Rect"))
            foreach (var count in new[] { 1, 3 })
            {
                var mark = Mask(tool); if (count == 1) mark.Points.RemoveAt(1); else mark.Points.Add(new() { X = .5, Y = .5 });
                try { await service.RenderImagePngAsync(image, new[] { mark }); throw new InvalidOperationException("Malformed mask rectangle accepted"); } catch (ArgumentException) { }
            }
    }
}
async Task MaskCancellation()
{
    var image = PatternImage("mask-cancel.png"); var pdf = PdfFile("mask-cancel.pdf");
    var count = Directory.GetFiles(Path.Combine(root, "exports")).Length;
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    foreach (var tool in new[] { "mosaicRect", "mosaicPen", "redactRect", "redactPen" })
    {
        try { await service.ExportPdfAsync("cancel.pdf", pdf, new[] { Mask(tool) }, cancellation.Token); throw new InvalidOperationException("PDF cancellation ignored"); } catch (OperationCanceledException) { }
        try { await service.RenderImagePngAsync(image, new[] { Mask(tool) }, cancellation.Token); throw new InvalidOperationException("PNG cancellation ignored"); } catch (OperationCanceledException) { }
    }
    Check(Directory.GetFiles(Path.Combine(root, "exports")).Length == count, "Cancelled mask export created files");
}

PreviewAnnotation OpacityMark(string tool, double? opacity)
{
    var mark = Mark(tool); mark.Color = "#ff0000"; mark.Width = tool == "text" ? .01 : .04; mark.Opacity = opacity;
    mark.Points = tool switch
    {
        "rect" => new() { new() { X = .15, Y = .3 }, new() { X = .85, Y = .7 } },
        "text" => new() { new() { X = .15, Y = .3 } },
        _ => new() { new() { X = .15, Y = .45 }, new() { X = .85, Y = .45 } }
    };
    mark.Text = "MMMM";
    return mark;
}
void CheckOpacityPixel(Bitmap bitmap, string tool, double? opacity)
{
    Color pixel;
    if (tool == "text")
    {
        pixel = Color.White;
        for (var x = (int)(bitmap.Width * .15); x < (int)(bitmap.Width * .8); x++)
            for (var y = (int)(bitmap.Height * .3); y < (int)(bitmap.Height * .7); y++)
                if (bitmap.GetPixel(x, y).G < pixel.G) pixel = bitmap.GetPixel(x, y);
    }
    else pixel = bitmap.GetPixel((int)(bitmap.Width * (tool == "rect" ? .15 : .5)), (int)(bitmap.Height * (tool == "rect" ? .5 : .45)));
    var alpha = opacity.HasValue ? (int)Math.Round(opacity.Value * 255, MidpointRounding.AwayFromZero) : tool == "highlighter" ? 77 : 255;
    Check(pixel.A == 255 && pixel.R >= 250 && Math.Abs(pixel.G - (255 - alpha)) <= 5 && Math.Abs(pixel.B - (255 - alpha)) <= 5,
        $"{tool} opacity {opacity?.ToString() ?? "legacy"} expected green {255 - alpha}, got {pixel}");
}
string BlankPdf(string name)
{
    var path = Path.Combine(root, name);
    using var pdf = new PdfDocument();
    var page = pdf.AddPage(); page.Width = XUnit.FromPoint(400); page.Height = XUnit.FromPoint(200);
    pdf.Save(path); return path;
}
async Task ImageOpacity()
{
    var source = ImageFile("opacity.png", 400, 200); var before = File.ReadAllBytes(source);
    foreach (var tool in new[] { "pen", "rect", "highlighter", "text" })
        foreach (var opacity in new double?[] { .25, .75, null, 0, 1 })
        {
            using var stream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { OpacityMark(tool, opacity) }));
            using var bitmap = new Bitmap(stream); CheckOpacityPixel(bitmap, tool, opacity);
        }
    Check(before.SequenceEqual(File.ReadAllBytes(source)), "Opacity drawing modified source image");
}
async Task HighlighterDefaultOpacity()
{
    var source = ImageFile("highlighter-default-opacity.png", 400, 200);
    using var explicitStream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { OpacityMark("highlighter", .3) }));
    using var explicitImage = new Bitmap(explicitStream);
    using var legacyStream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { OpacityMark("highlighter", null) }));
    using var legacyImage = new Bitmap(legacyStream);
    Check(explicitImage.GetPixel(200, 90) == legacyImage.GetPixel(200, 90) && explicitImage.GetPixel(200, 90).G == 178,
        "Explicit 30 percent highlighter differs from legacy 77/255 alpha");
}
async Task PdfOpacity()
{
    var source = BlankPdf("opacity-source.pdf"); var before = File.ReadAllBytes(source);
    var image = ImageFile("opacity-pdf-source.png", 400, 200); var imageBefore = File.ReadAllBytes(image);
    foreach (var tool in new[] { "pen", "rect", "highlighter", "text" })
        foreach (var opacity in new double?[] { .25, .75, .3, null, 0, 1 })
        {
            var mark = OpacityMark(tool, opacity);
            foreach (var output in new[]
            {
                await service.ExportPdfAsync("opacity-vector.pdf", source, new[] { mark }),
                await service.ExportImagePdfAsync("opacity-image.png", image, new[] { mark })
            })
            {
                using var rendered = await RenderPdf(output, width: 400); CheckOpacityPixel(rendered, tool, opacity);
                if (opacity is > 0 and < 1 || opacity == null && tool == "highlighter")
                {
                    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
                    var states = pdf.Pages[0].Resources.Elements.GetDictionary("/ExtGState");
                    var expected = (opacity.HasValue ? (int)Math.Round(opacity.Value * 255, MidpointRounding.AwayFromZero) : 77) / 255d;
                    Check(states != null && states.Elements.Values.OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                        .Select(value => value.Value).OfType<PdfDictionary>().Any(state =>
                            state.Elements.ContainsKey(tool is "pen" or "rect" or "highlighter" ? "/CA" : "/ca") &&
                            Math.Abs(state.Elements.GetReal(tool is "pen" or "rect" or "highlighter" ? "/CA" : "/ca") - expected) < .005),
                        tool + " PDF alpha graphics state missing");
                }
            }
        }
    Check(before.SequenceEqual(File.ReadAllBytes(source)), "Opacity drawing modified source PDF");
    Check(imageBefore.SequenceEqual(File.ReadAllBytes(image)), "Opacity PDF modified source image");
}
async Task FlattenedPdfOpacity()
{
    var source = BlankPdf("opacity-flatten-source.pdf"); var before = File.ReadAllBytes(source);
    foreach (var tool in new[] { "pen", "rect", "highlighter", "text" })
        foreach (var opacity in new double?[] { .25, .75, null })
        {
            var mask = Mask("redactRect"); mask.Opacity = 0;
            mask.Points = new() { new() { X = .02, Y = .05 }, new() { X = .08, Y = .15 } };
            var output = await service.ExportPdfAsync("opacity-flatten.pdf", source, new[] { mask, OpacityMark(tool, opacity) });
            using var rendered = await RenderPdf(output, width: 400); CheckOpacityPixel(rendered, tool, opacity);
            CheckMaskPixel(rendered, "redactRect", 20, 20);
        }
    Check(before.SequenceEqual(File.ReadAllBytes(source)), "Flattened opacity export modified source PDF");
}
async Task MaskOpacity()
{
    var source = PatternImage("mask-opacity.png"); var before = File.ReadAllBytes(source);
    var pdfSource = Path.Combine(root, "mask-opacity-source.pdf");
    using (var pdf = new PdfDocument())
    {
        var page = pdf.AddPage(); page.Width = XUnit.FromPoint(200); page.Height = XUnit.FromPoint(100);
        using (var graphics = XGraphics.FromPdfPage(page))
        using (var image = XImage.FromFile(source)) graphics.DrawImage(image, 0, 0, 200, 100);
        pdf.Save(pdfSource);
    }
    var pdfBefore = File.ReadAllBytes(pdfSource);
    foreach (var tool in new[] { "mosaicRect", "mosaicPen", "redactRect", "redactPen" })
    {
        var mark = Mask(tool); mark.Opacity = 0;
        using var stream = new MemoryStream(await service.RenderImagePngAsync(source, new[] { mark }));
        using var bitmap = new Bitmap(stream); CheckMaskPixel(bitmap, tool, 100, 50);
        using var imagePdf = await RenderPdf(await service.ExportImagePdfAsync("mask-opacity.png", source, new[] { mark }), width: 200);
        CheckMaskPixel(imagePdf, tool, 100, 50);
        using var maskedPdf = await RenderPdf(await service.ExportPdfAsync("mask-opacity.pdf", pdfSource, new[] { mark }), width: 200);
        CheckMaskPixel(maskedPdf, tool, 100, 50);
    }
    Check(before.SequenceEqual(File.ReadAllBytes(source)) && pdfBefore.SequenceEqual(File.ReadAllBytes(pdfSource)), "Mask opacity modified source");
}
async Task OpacityValidation()
{
    var source = ImageFile("invalid-opacity.png"); var pdf = BlankPdf("invalid-opacity.pdf");
    var count = Directory.GetFiles(Path.Combine(root, "exports")).Length;
    foreach (var opacity in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -.01, 1.01 })
        foreach (var tool in new[] { "pen", "rect", "highlighter", "text", "mosaicRect", "mosaicPen", "redactRect", "redactPen" })
        {
            var mark = tool is "pen" or "rect" or "highlighter" or "text" ? OpacityMark(tool, opacity) : Mask(tool); mark.Opacity = opacity;
            try { await service.RenderImagePngAsync(source, new[] { mark }); throw new InvalidOperationException("Invalid PNG opacity accepted"); } catch (ArgumentException) { }
            try { await service.ExportPdfAsync("invalid-opacity.pdf", pdf, new[] { mark }); throw new InvalidOperationException("Invalid PDF opacity accepted"); } catch (ArgumentException) { }
            try { await service.ExportImagePdfAsync("invalid-opacity.png", source, new[] { mark }); throw new InvalidOperationException("Invalid image PDF opacity accepted"); } catch (ArgumentException) { }
        }
    Check(Directory.GetFiles(Path.Combine(root, "exports")).Length == count, "Invalid opacity created exports");
}
