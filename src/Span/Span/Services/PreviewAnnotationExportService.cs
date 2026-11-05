using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Span.Models;

namespace Span.Services;

public sealed class PreviewAnnotationExportService
{
    public static string DefaultOutputDirectory => Helpers.AppIdentity.MarkedOutputRoot;
    readonly string _outputDirectory;

    public PreviewAnnotationExportService(string? outputDirectory = null)
    {
        _outputDirectory = Path.GetFullPath(outputDirectory ?? DefaultOutputDirectory);
        if (!string.Equals(_outputDirectory, Path.GetFullPath(DefaultOutputDirectory), StringComparison.OrdinalIgnoreCase)
            && !_outputDirectory.StartsWith(@"D:\codex\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Marked files must be saved in the application output folder.", nameof(outputDirectory));
    }

    public Task<string> ExportPdfAsync(string originalName, string pdfPath, IReadOnlyList<PreviewAnnotation> annotations, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(annotations);
        // A fresh document prevents source metadata, attachments, and original
        // masked-page objects from surviving as recoverable PDF data.
        if (annotations.Any(mark => mark != null && IsMask(mark.Tool)))
            return await ExportMaskedPdfAsync(originalName, pdfPath, annotations, cancellationToken);
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        Validate(annotations, document.PageCount);
        foreach (var group in annotations.GroupBy(mark => mark.PageIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawPdfPageAnnotations(document.Pages[group.Key], group, cancellationToken);
        }
        return SaveNewPdf(document, originalName, cancellationToken);
    }, cancellationToken);

    public Task<string> ExportImagePdfAsync(string originalName, string imagePath, IReadOnlyList<PreviewAnnotation> annotations, CancellationToken cancellationToken = default, int rotationDegrees = 0) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(annotations, 1);
        ValidateRotation(rotationDegrees);
        using var imageStream = new MemoryStream();
        var flatten = rotationDegrees != 0 || annotations.Any(mark => IsMask(mark.Tool));
        if (!flatten)
        {
            using var bitmap = LoadOrientedBitmap(imagePath);
            bitmap.Save(imageStream, ImageFormat.Png);
        }
        else
        {
            var png = await RenderImagePngAsync(imagePath, annotations, cancellationToken, rotationDegrees);
            imageStream.Write(png);
        }
        imageStream.Position = 0;
        using var image = XImage.FromStream(imageStream);
        using var document = new PdfDocument();
        var page = document.AddPage();
        // Fit the complete image, including unusually long images, within PDF's
        // 200-inch page limit. Both axes use the same scale; no pixels are cut.
        var scale = Math.Min(0.75, 14400d / Math.Max(image.PixelWidth, image.PixelHeight));
        page.Width = XUnit.FromPoint(image.PixelWidth * scale);
        page.Height = XUnit.FromPoint(image.PixelHeight * scale);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            graphics.IntersectClip(new XRect(0, 0, page.Width.Point, page.Height.Point));
            if (!flatten)
                DrawPdfAnnotations(graphics, page.Width.Point, page.Height.Point, annotations, cancellationToken);
        }
        return SaveNewPdf(document, originalName, cancellationToken);
    }, cancellationToken);

    public Task<byte[]> RenderImagePngAsync(string imagePath, IReadOnlyList<PreviewAnnotation> annotations, CancellationToken cancellationToken = default, int rotationDegrees = 0) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(annotations, 1);
        ValidateRotation(rotationDegrees);
        using var bitmap = LoadOrientedBitmap(imagePath);
        DrawBitmapAnnotations(bitmap, annotations, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        bitmap.RotateFlip(rotationDegrees switch
        {
            90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        });
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }, cancellationToken);

    static bool IsMask(string tool) => tool is "mosaicRect" or "mosaicPen" or "redactRect" or "redactPen";

    static int DrawingAlpha(PreviewAnnotation mark) => mark.Opacity.HasValue
        ? (int)Math.Round(mark.Opacity.Value * 255, MidpointRounding.AwayFromZero)
        : mark.Tool == "highlighter" ? 77 : 255;

    async Task<string> ExportMaskedPdfAsync(string originalName, string pdfPath, IReadOnlyList<PreviewAnnotation> annotations, CancellationToken cancellationToken)
    {
        using var source = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Validate(annotations, source.PageCount);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath)).AsTask(cancellationToken);
        var renderer = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken);
        using var document = new PdfDocument();
        for (var index = 0; index < source.PageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marks = annotations.Where(mark => mark.PageIndex == index).ToArray();
            if (!marks.Any(mark => IsMask(mark.Tool)))
            {
                var imported = document.AddPage(source.Pages[index]);
                if (marks.Length != 0) DrawPdfPageAnnotations(imported, marks, cancellationToken);
                continue;
            }
            using var page = renderer.GetPage((uint)index);
            var (width, height, _) = DisplayedPdfGeometry(source.Pages[index]);
            // Render only pages that contain masks, retaining the displayed crop
            // and rotation. Bound raster memory as for image preview snapshots.
            var scale = Math.Min(2, Math.Min(32768d / Math.Max(width, height), Math.Sqrt(16_000_000d / (width * height))));
            using var pixels = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(pixels, new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(height * scale))
            }).AsTask(cancellationToken);
            pixels.Seek(0);
            using var input = pixels.AsStreamForRead();
            using var rendered = new Bitmap(input);
            using var bitmap = new Bitmap(rendered.Width, rendered.Height, PixelFormat.Format32bppArgb);
            // WinRT PNGs carry the desktop DPI. Explicit pixel rectangles avoid
            // GDI's physical-size conversion shrinking the source into a corner.
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(rendered, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, rendered.Width, rendered.Height, GraphicsUnit.Pixel);
            DrawBitmapAnnotations(bitmap, marks, cancellationToken);
            using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png); png.Position = 0;
            using var image = XImage.FromStream(png);
            var flattened = document.AddPage();
            flattened.Width = XUnit.FromPoint(width); flattened.Height = XUnit.FromPoint(height);
            using var output = XGraphics.FromPdfPage(flattened);
            output.DrawImage(image, 0, 0, width, height);
        }
        return SaveNewPdf(document, originalName, cancellationToken);
    }

    static (double Width, double Height, XMatrix Transform) DisplayedPdfGeometry(PdfPage page)
    {
        // PDFsharp draws against the unrotated media box. Map the viewer's
        // cropped, clockwise-rotated page back to that coordinate space.
        var media = page.MediaBox; var crop = page.EffectiveCropBoxReadOnly;
        var left = Math.Max(media.X1, crop.X1); var bottom = Math.Max(media.Y1, crop.Y1);
        var right = Math.Min(media.X2, crop.X2); var top = Math.Min(media.Y2, crop.Y2);
        var width = right - left; var height = top - bottom;
        var rotation = ((page.Rotate % 360) + 360) % 360;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height) || rotation % 90 != 0)
            throw new InvalidDataException("The PDF page has invalid dimensions or rotation.");
        var transform = rotation switch
        {
            90 => new XMatrix(0, -1, 1, 0, left, page.Height.Point - bottom),
            180 => new XMatrix(-1, 0, 0, -1, right, page.Height.Point - bottom),
            270 => new XMatrix(0, 1, -1, 0, right, page.Height.Point - top),
            _ => new XMatrix(1, 0, 0, 1, left, page.Height.Point - top)
        };
        return rotation is 90 or 270 ? (height, width, transform) : (width, height, transform);
    }

    static void DrawPdfPageAnnotations(PdfPage page, IEnumerable<PreviewAnnotation> annotations, CancellationToken cancellationToken)
    {
        var (width, height, transform) = DisplayedPdfGeometry(page);
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        graphics.MultiplyTransform(transform);
        graphics.IntersectClip(new XRect(0, 0, width, height));
        DrawPdfAnnotations(graphics, width, height, annotations, cancellationToken);
    }

    static void DrawBitmapAnnotations(Bitmap bitmap, IEnumerable<PreviewAnnotation> annotations, CancellationToken cancellationToken)
    {
        foreach (var mark in annotations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMask(mark.Tool)) { DrawMask(bitmap, mark, cancellationToken); continue; }
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var color = Color.FromArgb(DrawingAlpha(mark), ParseColor(mark.Color));
            var width = (float)(mark.Width * bitmap.Width);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var points = mark.Points.Select(point => new PointF((float)(point.X * bitmap.Width), (float)(point.Y * bitmap.Height))).ToArray();
            if (mark.Tool == "rect")
            {
                graphics.DrawRectangle(pen, Math.Min(points[0].X, points[1].X), Math.Min(points[0].Y, points[1].Y), Math.Abs(points[1].X - points[0].X), Math.Abs(points[1].Y - points[0].Y));
            }
            else if (mark.Tool == "text")
            {
                using var font = new Font("Microsoft YaHei", width * 6, FontStyle.Regular, GraphicsUnit.Pixel);
                using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
                graphics.DrawString(mark.Text, font, brush, points[0], format);
            }
            else if (points.Length == 1)
                graphics.FillEllipse(brush, points[0].X - width / 2, points[0].Y - width / 2, width, width);
            else
                graphics.DrawLines(pen, points);
        }
    }

    static void DrawMask(Bitmap bitmap, PreviewAnnotation mark, CancellationToken cancellationToken)
    {
        var points = mark.Points.Select(point => new PointF((float)(point.X * bitmap.Width), (float)(point.Y * bitmap.Height))).ToArray();
        var width = (float)(mark.Width * bitmap.Width * 8);
        using var shape = new GraphicsPath();
        if (mark.Tool.EndsWith("Rect", StringComparison.Ordinal))
            shape.AddRectangle(new RectangleF(Math.Min(points[0].X, points[1].X), Math.Min(points[0].Y, points[1].Y), Math.Abs(points[1].X - points[0].X), Math.Abs(points[1].Y - points[0].Y)));
        else
        {
            // Explicit round discs also cover one-point/zero-length brush paths.
            foreach (var point in points) shape.AddEllipse(point.X - width / 2, point.Y - width / 2, width, width);
            if (points.Length > 1)
            {
                using var stroke = new GraphicsPath(); stroke.AddLines(points);
                using var pen = new Pen(Color.Black, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                stroke.Widen(pen); shape.AddPath(stroke, false);
            }
            shape.FillMode = FillMode.Winding;
        }
        if (mark.Tool.StartsWith("redact", StringComparison.Ordinal))
        {
            using var graphics = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(Color.FromArgb(255, 17, 17, 17));
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.FillPath(brush, shape);
            return;
        }
        // Read the current bitmap, so ordinary marks and masks keep their order.
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * data.Height]; var stride = data.Stride;
        try { Marshal.Copy(data.Scan0, bytes, 0, bytes.Length); }
        finally { bitmap.UnlockBits(data); }
        using var target = Graphics.FromImage(bitmap);
        target.CompositingMode = CompositingMode.SourceCopy;
        target.SetClip(shape);
        var block = Math.Max(8, (int)Math.Ceiling(bitmap.Width * .012));
        var bounds = Rectangle.Intersect(Rectangle.Ceiling(shape.GetBounds()), new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        for (var y = bounds.Top / block * block; y < bounds.Bottom; y += block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = bounds.Left / block * block; x < bounds.Right; x += block)
            {
                var right = Math.Min(x + block, bitmap.Width); var bottom = Math.Min(y + block, bitmap.Height);
                long r = 0, g = 0, b = 0, a = 0;
                for (var row = y; row < bottom; row++) for (var column = x; column < right; column++)
                {
                    var offset = row * stride + column * 4;
                    b += bytes[offset]; g += bytes[offset + 1]; r += bytes[offset + 2]; a += bytes[offset + 3];
                }
                var count = (right - x) * (bottom - y);
                using var brush = new SolidBrush(Color.FromArgb((int)(a / count), (int)(r / count), (int)(g / count), (int)(b / count)));
                target.FillRectangle(brush, x, y, right - x, bottom - y);
            }
        }
    }

    static void ValidateRotation(int rotationDegrees)
    {
        if (rotationDegrees is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees), "Rotation must be 0, 90, 180 or 270 degrees.");
    }

    static void DrawPdfAnnotations(XGraphics graphics, double pageWidth, double pageHeight, IEnumerable<PreviewAnnotation> annotations, CancellationToken cancellationToken)
    {
        foreach (var mark in annotations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rgb = ParseColor(mark.Color);
            var alpha = DrawingAlpha(mark);
            // PDFsharp omits the transparency state for zero alpha.
            if (alpha == 0) continue;
            var color = XColor.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
            var width = mark.Width * pageWidth;
            var pen = new XPen(color, width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
            var brush = new XSolidBrush(color);
            var points = mark.Points.Select(point => new XPoint(point.X * pageWidth, point.Y * pageHeight)).ToArray();
            if (mark.Tool == "rect")
                graphics.DrawRectangle(pen, Math.Min(points[0].X, points[1].X), Math.Min(points[0].Y, points[1].Y), Math.Abs(points[1].X - points[0].X), Math.Abs(points[1].Y - points[0].Y));
            else if (mark.Tool == "text")
            {
                // Installed YaHei is a TTC collection, which PDFsharp's font
                // resolver cannot embed. GDI outlines preserve its Chinese glyphs
                // as scalable vectors without changing any existing PDF text.
                using var family = new FontFamily("Microsoft YaHei");
                using var glyphs = new GraphicsPath();
                using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
                glyphs.AddString(mark.Text, family, (int)FontStyle.Regular, (float)(width * 6), new PointF((float)points[0].X, (float)points[0].Y), format);
                if (glyphs.PointCount > 0)
                    graphics.DrawPath(brush, new XGraphicsPath(glyphs.PathPoints, glyphs.PathTypes, XFillMode.Winding));
            }
            else if (points.Length == 1)
                graphics.DrawEllipse(brush, points[0].X - width / 2, points[0].Y - width / 2, width, width);
            else
                graphics.DrawLines(pen, points);
        }
    }

    static Bitmap LoadOrientedBitmap(string imagePath)
    {
        using var source = Image.FromFile(imagePath);
        const int orientationId = 0x0112;
        if (source.PropertyIdList.Contains(orientationId))
        {
            var value = source.GetPropertyItem(orientationId)?.Value;
            if (value is { Length: >= 2 })
            {
                var transform = BitConverter.ToUInt16(value, 0) switch
                {
                    2 => RotateFlipType.RotateNoneFlipX,
                    3 => RotateFlipType.Rotate180FlipNone,
                    4 => RotateFlipType.Rotate180FlipX,
                    5 => RotateFlipType.Rotate90FlipX,
                    6 => RotateFlipType.Rotate90FlipNone,
                    7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                };
                source.RotateFlip(transform);
            }
        }
        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return bitmap;
    }

    static void Validate(IReadOnlyList<PreviewAnnotation> annotations, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        foreach (var mark in annotations)
        {
            if (mark == null || mark.PageIndex < 0 || mark.PageIndex >= pageCount)
                throw new ArgumentException("An annotation refers to an unavailable page.", nameof(annotations));
            if (mark.Tool != "pen" && mark.Tool != "rect" && mark.Tool != "highlighter" && mark.Tool != "text" && !IsMask(mark.Tool))
                throw new ArgumentException("Unsupported annotation tool.", nameof(annotations));
            if (!double.IsFinite(mark.Width) || mark.Width <= 0 || mark.Width > 1)
                throw new ArgumentException("Annotation width must be between zero and one.", nameof(annotations));
            if (mark.Opacity.HasValue && (!double.IsFinite(mark.Opacity.Value) || mark.Opacity.Value < 0 || mark.Opacity.Value > 1))
                throw new ArgumentException("Annotation opacity must be between zero and one.", nameof(annotations));
            _ = ParseColor(mark.Color);
            if (mark.Points == null || mark.Points.Count == 0 || ((mark.Tool == "rect" || mark.Tool == "mosaicRect" || mark.Tool == "redactRect") && mark.Points.Count != 2) || (mark.Tool == "text" && (mark.Points.Count != 1 || string.IsNullOrWhiteSpace(mark.Text))))
                throw new ArgumentException("The annotation does not have the required points or text.", nameof(annotations));
            foreach (var point in mark.Points)
                if (point == null || !double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X < 0 || point.X > 1 || point.Y < 0 || point.Y > 1)
                    throw new ArgumentException("Annotation points must be finite coordinates between zero and one.", nameof(annotations));
        }
    }

    static Color ParseColor(string color)
    {
        if (color == null || color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Annotation color must use #RRGGBB.", nameof(color));
        return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    string SaveNewPdf(PdfDocument document, string originalName, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        document.Save(bytes, false);
        cancellationToken.ThrowIfCancellationRequested();
        var name = Path.GetFileNameWithoutExtension(originalName);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        if (name.Length > 120) name = name.Substring(0, 120);
        Directory.CreateDirectory(_outputDirectory);
        for (var number = 1; ; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = number == 1 ? "" : $" ({number})";
            var path = Path.Combine(_outputDirectory, $"{name}_标记{suffix}.pdf");
            FileStream output;
            try { output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            // The complete PDF is prepared before reserving its final name. Once
            // reserved, finish the write even if a cancellation arrives meanwhile.
            using (output) { bytes.Position = 0; bytes.CopyTo(output); output.Flush(true); }
            return path;
        }
    }
}
