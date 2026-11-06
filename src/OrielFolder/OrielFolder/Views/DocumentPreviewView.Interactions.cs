using Microsoft.UI.Xaml;
using OrielFolder.Models;
using OrielFolder.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace OrielFolder.Views;

public sealed partial class DocumentPreviewView
{
    private static readonly JsonSerializerOptions MessageJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static InMemoryRandomAccessStream? _clipboardImage;
    private readonly Dictionary<string, List<PreviewAnnotation>> _annotationDrafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _retiredResults = new();
    private readonly HashSet<int> _scannedPages = new();
    private readonly Dictionary<int, OcrRecognitionResult> _recognizedPages = new();
    private CancellationTokenSource? _liveTextCancellation;
    private int _textVersion = -1;
    private int _restoreVersion = -1;
    private int _recognizingPage = -1;
    private bool _isExporting;

    public event Action<string>? StatusChanged;

    public static readonly DependencyProperty RotationAngleProperty = DependencyProperty.Register(
        nameof(RotationAngle), typeof(double), typeof(DocumentPreviewView), new PropertyMetadata(0d, OnRotationChanged));
    public double RotationAngle { get => (double)GetValue(RotationAngleProperty); set => SetValue(RotationAngleProperty, value); }

    private static void OnRotationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (DocumentPreviewView)sender;
        if (double.IsFinite((double)args.NewValue))
            view.PostBrowserMessage(new { type = "rotation", angle = (double)args.NewValue });
    }

    private async Task HandleBrowserMessageAsync(string source, string json)
    {
        if (_unloaded || _viewerUri == null || source != _viewerUri ||
            json.Length > 10 * 1024 * 1024 || Encoding.UTF8.GetByteCount(json) > 10 * 1024 * 1024) return;
        int version = _version;
        try
        {
            using var message = JsonDocument.Parse(json);
            var root = message.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String) return;
            if (_textVersion != version)
            {
                CancelLiveTextRequests();
                _scannedPages.Clear(); _recognizedPages.Clear(); _textVersion = version;
            }
            switch (type.GetString())
            {
                case "close":
                    DispatcherQueue.TryEnqueue(() => { if (IsCurrent(version)) CloseRequested?.Invoke(); });
                    break;
                case "navigate":
                    if (root.TryGetProperty("direction", out var direction) && direction.ValueKind == JsonValueKind.Number && direction.TryGetInt32(out int delta) && delta is -1 or 1)
                        DispatcherQueue.TryEnqueue(() => { if (IsCurrent(version)) NavigateRequested?.Invoke(delta); });
                    break;
                case "page":
                    if (TryReadPage(root, out int page) && page != CurrentPageIndex)
                    {
                        CancelLiveTextRequests();
                        CurrentPageIndex = page; PageChanged?.Invoke();
                        if (_scannedPages.Contains(page)) await RecognizeCurrentPageAsync(version);
                    }
                    break;
                case "viewerReady":
                    HideNativeLoading(version);
                    SendPreviewPreferences();
                    break;
                case "painted":
                    HideNativeLoading(version);
                    break;
                case "rendered":
                    HideNativeLoading(version);
                    if (_restoreVersion != version)
                    {
                        _restoreVersion = version;
                        PostBrowserMessage(new { type = "annotations", annotations = GetAnnotations(_sourceForThisLoad) });
                        PostBrowserMessage(new { type = "rotation", angle = RotationAngle });
                        SendPreviewPreferences();
                    }
                    break;
                case "annotationsChanged":
                    if (TryReadAnnotations(root, out var annotations)) _annotationDrafts[_sourceForThisLoad] = annotations;
                    break;
                case "labelPresetsChanged":
                    SavePreviewLabelPresets(root);
                    break;
                case "toolbarOrderChanged":
                    SavePreviewToolbarOrder(root);
                    break;
                case "ocr":
                    if (TryReadPage(root, out int index))
                    {
                        _scannedPages.Add(index);
                        if (index == CurrentPageIndex) await RecognizeCurrentPageAsync(version);
                    }
                    break;
                case "copyImage":
                case "exportPdf":
                    if (TryReadAnnotations(root, out var snapshot))
                    {
                        _annotationDrafts[_sourceForThisLoad] = snapshot;
                        await CopyOrExportAsync(type.GetString() == "copyImage", version);
                    }
                    break;
                case "error":
                    _status.Text = _isImage ? "无法显示此图片。" : "无法显示此 PDF，文件可能受密码保护或已损坏。";
                    _status.Visibility = Visibility.Visible;
                    _progress.Visibility = Visibility.Collapsed;
                    if (_isImage) ReportImagePreviewFailure(version);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (JsonException) { }
        catch (Exception)
        {
            if (IsCurrent(version)) ReportStatus("预览操作未能完成，请重试。");
        }
    }

    private bool TryReadPage(JsonElement root, out int page)
    {
        page = -1;
        return root.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out page) &&
            page >= 0 && page < 100_000 && (!_isImage || page == 0);
    }

    private void SavePreviewLabelPresets(JsonElement root)
    {
        try
        {
            if (_settings == null || !root.TryGetProperty("presets", out var items)) throw new InvalidDataException();
            var presets = PreviewLabelPresets.Parse(items.GetRawText());
            string json = JsonSerializer.Serialize(presets, PreviewLabelPresets.JsonOptions);
            _settings.Set(PreviewLabelPresets.SettingsKey, json);
            if (_settings.Get(PreviewLabelPresets.SettingsKey, "[]") != json) throw new IOException();
            PostBrowserMessage(new { type = "labelPresetsSaved", success = true });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            PostBrowserMessage(new { type = "labelPresetsSaved", success = false, message = "请检查标签内容；若内容过多，请缩短后重试。" });
        }
    }

    private void SavePreviewToolbarOrder(JsonElement root)
    {
        try
        {
            if (_settings == null || !root.TryGetProperty("order", out var items)) throw new InvalidDataException();
            string json = JsonSerializer.Serialize(PreviewToolbarOrder.Parse(items.GetRawText()));
            _settings.Set(PreviewToolbarOrder.SettingsKey, json);
            if (_settings.Get(PreviewToolbarOrder.SettingsKey, "[]") != json) throw new IOException();
            PostBrowserMessage(new { type = "toolbarOrderSaved", success = true });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            PostBrowserMessage(new { type = "toolbarOrderSaved", success = false });
        }
    }

    private bool TryReadAnnotations(JsonElement root, out List<PreviewAnnotation> annotations)
    {
        annotations = new();
        if (!root.TryGetProperty("annotations", out var items) || items.ValueKind != JsonValueKind.Array ||
            items.GetArrayLength() > 10_000) return false;
        var parsed = items.Deserialize<List<PreviewAnnotation>>(MessageJson);
        if (parsed == null) return false;
        int points = 0;
        foreach (var mark in parsed)
        {
            if (mark == null || mark.PageIndex < 0 || mark.PageIndex >= 100_000 || (_isImage && mark.PageIndex != 0) ||
                mark.Tool is not ("pen" or "rect" or "highlighter" or "text" or "mosaicRect" or "mosaicPen" or "redactRect" or "redactPen") ||
                !double.IsFinite(mark.Width) || mark.Width <= 0 || mark.Width > 1 ||
                (mark.Opacity.HasValue && (!double.IsFinite(mark.Opacity.Value) || mark.Opacity.Value < 0 || mark.Opacity.Value > 1)) ||
                mark.Color == null || mark.Color.Length != 7 || mark.Color[0] != '#' ||
                !int.TryParse(mark.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ||
                mark.Text == null || mark.Text.Length > 10_000 || mark.Points == null || mark.Points.Count == 0 ||
                (mark.Tool is "rect" or "mosaicRect" or "redactRect" && mark.Points.Count != 2) ||
                (mark.Tool == "text" && (mark.Points.Count != 1 || string.IsNullOrWhiteSpace(mark.Text)))) return false;
            points += mark.Points.Count;
            if (points > 200_000 || mark.Points.Any(point => point == null || !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) || point.X < 0 || point.X > 1 || point.Y < 0 || point.Y > 1)) return false;
        }
        annotations = parsed;
        return true;
    }

    private List<PreviewAnnotation> GetAnnotations(string source) =>
        !_annotationDrafts.TryGetValue(source, out var annotations) ? new() : annotations.Select(mark => new PreviewAnnotation
        {
            Tool = mark.Tool, Color = mark.Color, Width = mark.Width, Opacity = mark.Opacity, Text = mark.Text, PageIndex = mark.PageIndex,
            Points = mark.Points.Select(point => new AnnotationPoint { X = point.X, Y = point.Y }).ToList()
        }).ToList();

    private async Task RecognizeCurrentPageAsync(int version)
    {
        int page = CurrentPageIndex;
        if (!IsCurrent(version)) return;
        if (_recognizedPages.TryGetValue(page, out var cached)) { SendOcr(page, cached); return; }
        if (_recognizingPage == page) return;
        string? path = _isImage ? _currentImageSnapshot?.Path : CurrentPdfPath;
        if (path == null) return;
        CancelLiveTextRequests();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation?.Token ?? CancellationToken.None);
        _liveTextCancellation = cancellation; _recognizingPage = page;
        try
        {
            var service = new OcrService();
            var result = _isImage ? await service.RecognizeImageAsync(path, cancellation.Token) :
                await service.RecognizePdfPageAsync(path, page, cancellation.Token);
            if (!IsCurrent(version) || page != CurrentPageIndex || cancellation.IsCancellationRequested) return;
            if (result.Error == OcrError.None)
            {
                _recognizedPages[page] = result; SendOcr(page, result);
            }
            else ReportStatus(result.Error == OcrError.NoLanguage ? "未安装可用的 OCR 识别语言。" : "当前页面的文字识别未能完成。");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_liveTextCancellation, cancellation)) { _liveTextCancellation = null; _recognizingPage = -1; }
            cancellation.Dispose();
        }
    }

    private void SendOcr(int page, OcrRecognitionResult result) => PostBrowserMessage(new
    {
        type = "ocr", index = page, words = result.Words ?? Array.Empty<OcrWordRegion>(),
        pixelWidth = result.PixelWidth, pixelHeight = result.PixelHeight
    });

    private void CancelLiveTextRequests()
    {
        _liveTextCancellation?.Cancel();
        _liveTextCancellation = null; _recognizingPage = -1;
    }

    private async Task CopyOrExportAsync(bool copyImage, int version)
    {
        if (_isExporting) { ReportStatus("正在处理上一项复制或导出操作，请稍候。"); return; }
        bool image = _isImage;
        string? path = image ? _currentImageSnapshot?.Path : CurrentPdfPath;
        if (path == null || (copyImage && !image)) return;
        string source = _sourceForThisLoad;
        var annotations = GetAnnotations(source);
        int rotation = double.IsFinite(RotationAngle) ? (int)(((RotationAngle % 360) + 360) % 360) : 0;
        _isExporting = true;
        try
        {
            var service = new PreviewAnnotationExportService();
            if (copyImage)
            {
                var bytes = await service.RenderImagePngAsync(path, annotations, CancellationToken.None, rotation);
                var stream = new InMemoryRandomAccessStream();
                try
                {
                    using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); writer.DetachStream();
                    }
                    stream.Seek(0);
                    var data = new DataPackage();
                    data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                    Clipboard.SetContent(data); Clipboard.Flush();
                    var previous = _clipboardImage; _clipboardImage = stream; previous?.Dispose();
                }
                catch { stream.Dispose(); throw; }
                if (IsCurrent(version)) ReportStatus("已复制图片。");
            }
            else
            {
                string output = image ? await service.ExportImagePdfAsync(Path.GetFileName(source), path, annotations, CancellationToken.None, rotation) :
                    await service.ExportPdfAsync(Path.GetFileName(source), path, annotations, CancellationToken.None);
                if (IsCurrent(version)) ReportStatus("已导出：" + output);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (IsCurrent(version)) ReportStatus(copyImage ? "复制图片失败，请重试。" : "导出 PDF 失败，请重试。");
        }
        finally
        {
            _isExporting = false;
            foreach (var result in _retiredResults) result.Dispose();
            _retiredResults.Clear();
        }
    }

    private void RetireOrDispose(IDisposable? result)
    {
        if (result == null) return;
        if (_isExporting) _retiredResults.Add(result);
        else result.Dispose();
    }

    private bool IsCurrent(int version) => !_unloaded && version == _version;

    private void ReportStatus(string text)
    {
        StatusChanged?.Invoke(text);
        PostBrowserMessage(new { type = "status", text });
    }

    private void PostBrowserMessage(object message)
    {
        if (_unloaded || _viewerUri == null) return;
        try { _browser?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, MessageJson)); }
        catch (InvalidOperationException) { }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
