using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using IOPath = System.IO.Path;

namespace Span.Services;

public sealed class ImagePreviewSnapshot : IDisposable
{
    static string CacheRoot => IOPath.Combine(Helpers.AppIdentity.PreviewCacheRoot, "images");
    const long MaximumSourceBytes = 100L * 1024 * 1024;
    const double MaximumPixels = 16_000_000;
    const uint MaximumDimension = 32768;
    FileStream? _readLease;

    public string Path { get; }
    public uint Width { get; }
    public uint Height { get; }

    ImagePreviewSnapshot(string path, uint width, uint height)
    {
        Path = path;
        Width = width;
        Height = height;
        // Readers can consume the exact prepared PNG; edits are denied until the
        // snapshot is released. The original file's handle is already closed.
        _readLease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public static Task<ImagePreviewSnapshot> CreateAsync(string originalPath, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        await using (var input = new FileStream(originalPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (input.Length == 0 || input.Length > MaximumSourceBytes)
                throw new IOException("图片为空或超过 100 MB 预览大小限制。");
            bytes = new byte[(int)input.Length];
            await input.ReadExactlyAsync(bytes, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var memory = new MemoryStream(bytes, writable: false);
        using var source = memory.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(source).AsTask(cancellationToken);
        double width = decoder.PixelWidth;
        double height = decoder.PixelHeight;
        if (width <= 0 || height <= 0) throw new IOException("图片尺寸无效。");
        var scale = Math.Min(1, Math.Min(MaximumDimension / Math.Max(width, height), Math.Sqrt(MaximumPixels / (width * height))));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Floor(width * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Floor(height * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = new MemoryStream();
        using (var target = encoded.AsRandomAccessStream())
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target).AsTask(cancellationToken);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectoryNotRedirected(CacheRoot);
        string folder = IOPath.Combine(CacheRoot, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Image snapshot directory already exists.");
        Directory.CreateDirectory(folder);
        string path = IOPath.Combine(folder, "image.png");
        bool ownsFile = false;
        bool retained = false;
        try
        {
            EnsureDirectoryNotRedirected(folder);
            cancellationToken.ThrowIfCancellationRequested();
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                ownsFile = true;
                // AsRandomAccessStream owns its MemoryStream wrapper. ToArray
                // remains available after disposal and returns the encoded PNG.
                await output.WriteAsync(encoded.ToArray(), cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryNotRedirected(folder);
            var snapshot = new ImagePreviewSnapshot(path, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight);
            retained = true;
            return snapshot;
        }
        finally
        {
            if (!retained) Cleanup(folder, ownsFile);
        }
    }, cancellationToken);

    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _readLease, null);
        if (lease == null) return;
        lease.Dispose();
        Cleanup(IOPath.GetDirectoryName(Path)!, ownsFile: true);
    }

    static void EnsureDirectoryNotRedirected(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Image snapshot staging cannot use a redirected directory.");
    }

    static void Cleanup(string folder, bool ownsFile)
    {
        try
        {
            var fullPath = IOPath.TrimEndingDirectorySeparator(IOPath.GetFullPath(folder));
            if (!string.Equals(IOPath.GetDirectoryName(fullPath), CacheRoot, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(IOPath.GetFileName(fullPath), "N", out _) || !Directory.Exists(fullPath)) return;
            EnsureDirectoryNotRedirected(fullPath);
            var image = IOPath.Combine(fullPath, "image.png");
            if (ownsFile && File.Exists(image))
            {
                if ((File.GetAttributes(image) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return;
                File.Delete(image);
            }
            // Only this request's exact image is eligible; unfamiliar children
            // remain intact, and directory removal is always non-recursive.
            EnsureDirectoryNotRedirected(fullPath);
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any()) Directory.Delete(fullPath, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
