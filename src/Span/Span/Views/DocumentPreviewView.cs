using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;
using Span.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace Span.Views;

/// <summary>Local, read-only PDF/Office preview. Only private staged copies are exposed to the browser.</summary>
public sealed partial class DocumentPreviewView : UserControl, IDisposable
{
    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(DocumentPreviewView), new PropertyMetadata("", OnSourceChanged));
    public string SourcePath { get => (string)GetValue(SourcePathProperty); set => SetValue(SourcePathProperty, value); }
    public string? CurrentPdfPath { get; private set; }
    public int CurrentPageIndex { get; private set; }
    public event Action? CloseRequested;
    public event Action<int>? NavigateRequested;
    public event Action? PageChanged;
    public event Action? ImagePreviewFailed;

    public bool HasBrowserFocus
    {
        get
        {
            if (_browser == null || XamlRoot == null) return false;
            for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                element != null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, _browser)) return true;
            return false;
        }
    }

    private int _failedImageVersion = -1;

    internal void ReportImagePreviewFailure(int version)
    {
        if (!IsCurrent(version) || !_isImage || _failedImageVersion == version) return;
        _failedImageVersion = version;
        ImagePreviewFailed?.Invoke();
    }

    private readonly Grid _root = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = false, Maximum = 5, Margin = new Thickness(8), Visibility = Visibility.Collapsed };
    private readonly OfficeDocumentPreviewService _service = new();
    private ISettingsService? _settings;
    private WebView2? _browser;
    private Task? _initializeTask;
    private CancellationTokenSource? _loadCancellation;
    private DocumentPreviewResult? _currentResult;
    private ImagePreviewSnapshot? _currentImageSnapshot;
    private bool _isImage;
    private string _sourceForThisLoad = "";
    private int _version;
    private bool _unloaded = true;
    private string? _viewerUri;
    private int _viewerOwnsLoadingVersion = -1;
    private int _preparationProgressVersion = -1;
    private double _preparationProgressValue;

    public DocumentPreviewView()
    {
        Content = _root;
        var loading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        loading.Children.Add(_status); loading.Children.Add(_progress);
        _root.Children.Add(loading);
        Loaded += (_, _) =>
        {
            _unloaded = false;
            _settings ??= App.Current.Services.GetService<ISettingsService>();
            if (_settings != null)
            {
                _settings.SettingChanged -= OnPreviewSettingChanged;
                _settings.SettingChanged += OnPreviewSettingChanged;
            }
            ActualThemeChanged -= OnPreviewThemeChanged;
            ActualThemeChanged += OnPreviewThemeChanged;
            UpdatePreviewTheme();
            _ = LoadAsync();
        };
        Unloaded += (_, _) => Cleanup();
        KeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewSettingChanged(string key, object? value)
    {
        if (key == nameof(ISettingsService.PreviewZoomModifier) || key == PreviewLabelPresets.SettingsKey || key == PreviewToolbarOrder.SettingsKey)
            DispatcherQueue.TryEnqueue(SendPreviewPreferences);
    }

    private void OnPreviewThemeChanged(FrameworkElement sender, object args)
    {
        UpdatePreviewTheme();
        SendPreviewPreferences();
    }

    private void UpdatePreviewTheme()
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        var background = dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        _root.Background = new SolidColorBrush(background);
        _status.Foreground = new SolidColorBrush(dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
        if (_browser != null) _browser.DefaultBackgroundColor = background;
    }

    private void SendPreviewPreferences() => PostBrowserMessage(new
    {
        type = "preferences", zoomModifier = Math.Clamp(_settings?.PreviewZoomModifier ?? 0, 0, 2),
        theme = ActualTheme == ElementTheme.Dark ? "dark" : "light",
        supportsFileNavigation = NavigateRequested != null,
        labelPresets = ReadPreviewLabelPresets(), toolbarOrder = ReadPreviewToolbarOrder()
    });

    private System.Collections.Generic.List<PreviewLabelPreset> ReadPreviewLabelPresets()
    {
        try { return PreviewLabelPresets.Parse(_settings?.Get(PreviewLabelPresets.SettingsKey, "[]") ?? "[]"); }
        catch (InvalidDataException) { return new(); }
    }

    private System.Collections.Generic.List<string> ReadPreviewToolbarOrder()
    {
        try { return PreviewToolbarOrder.Parse(_settings?.Get(PreviewToolbarOrder.SettingsKey, "[]") ?? "[]"); }
        catch (InvalidDataException) { return new(PreviewToolbarOrder.DefaultOrder); }
    }

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (DocumentPreviewView)sender;
        if (!view._unloaded) _ = view.LoadAsync();
    }

    private void ClearPdf()
    {
        CurrentPdfPath = null; CurrentPageIndex = 0; PageChanged?.Invoke();
    }

    private void UpdatePreparationProgress(DocumentPreviewProgress progress, int version)
    {
        if (!IsCurrent(version) || _viewerOwnsLoadingVersion == version) return;
        if (_preparationProgressVersion != version) { _preparationProgressVersion = version; _preparationProgressValue = 0; }
        int completed = progress.Stage switch
        {
            DocumentPreviewStage.Checking => 1,
            DocumentPreviewStage.Queued or DocumentPreviewStage.Converting => 2,
            DocumentPreviewStage.OpeningDocument => 3,
            DocumentPreviewStage.Exporting => 4,
            DocumentPreviewStage.Ready => 5,
            _ => 0
        };
        double value = completed;
        if (progress.Stage == DocumentPreviewStage.Copying && progress.TotalBytes > 0)
            value += Math.Clamp((double)progress.BytesCopied / progress.TotalBytes, 0, 1);
        if (value < _preparationProgressValue) return;
        _preparationProgressValue = value;
        _status.Visibility = Visibility.Visible;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = false;
        _progress.Maximum = 5;
        _progress.Value = value;
        string detail = progress.Stage switch
        {
            DocumentPreviewStage.Copying when progress.TotalBytes > 0 => $"正在复制文档 {Math.Max(0, progress.BytesCopied):N0}/{progress.TotalBytes:N0} 字节",
            DocumentPreviewStage.Copying => "正在复制文档…",
            DocumentPreviewStage.Checking => "正在检查文档链接…",
            DocumentPreviewStage.Queued => "正在等待上一项 Office 预览完成…",
            DocumentPreviewStage.Converting => "正在启动 Office…",
            DocumentPreviewStage.OpeningDocument => "正在用 Office 打开文档…",
            DocumentPreviewStage.Exporting when progress.OutputBytes > 0 => $"正在生成 PDF，已写出 {progress.OutputBytes:N0} 字节",
            DocumentPreviewStage.Exporting => "文档已打开，正在生成 PDF…",
            _ => "预览文件已准备好，正在显示页面…"
        };
        _status.Text = $"文档准备：已完成 {(int)value}/5 步 · {detail}";
    }

    private void HideNativeLoading(int version)
    {
        if (!IsCurrent(version)) return;
        _viewerOwnsLoadingVersion = version;
        _status.Visibility = Visibility.Collapsed;
        _progress.Visibility = Visibility.Collapsed;
    }

    private async Task LoadAsync()
    {
        int version = ++_version;
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellation = _loadCancellation.Token;
        ReleasePreview();
        _sourceForThisLoad = SourcePath;
        _isImage = System.IO.Path.GetExtension(SourcePath).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".webp" or ".ico" or ".jfif";
        _status.Visibility = Visibility.Visible;
        _status.Text = "正在加载文档…";
        _progress.IsIndeterminate = false;
        _progress.Maximum = 5;
        _progress.Value = 0;
        _progress.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(SourcePath)) { _status.Text = ""; _progress.Visibility = Visibility.Collapsed; return; }
        try
        {
            // WebView startup overlaps the private snapshot and Office conversion.
            _initializeTask ??= InitializeBrowserAsync();
            // Preparation can fail before it awaits the browser; observe startup faults in that case too.
            _ = _initializeTask.ContinueWith(task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            if (_isImage)
            {
                var image = await ImagePreviewSnapshot.CreateAsync(_sourceForThisLoad, cancellation);
                if (version != _version || _unloaded) { image.Dispose(); return; }
                _currentImageSnapshot = image;
                await OpenPreviewBrowserAsync(image.Path, "image.html", "image.png", version);
                return;
            }
            var progress = new Progress<DocumentPreviewProgress>(value => UpdatePreparationProgress(value, version));
            var result = await _service.PrepareCachedAsync(_sourceForThisLoad, cancellation, progress);
            if (version != _version || _unloaded) { result.Dispose(); return; }
            _currentResult = result;
            if (result.PdfPath == null)
            {
                _status.Text = string.IsNullOrWhiteSpace(result.Limitation) ? "无法显示此文档的原排版预览。" : result.Limitation;
                _status.Visibility = Visibility.Visible;
                _progress.Visibility = Visibility.Collapsed;
                return;
            }
            CurrentPdfPath = result.PdfPath; CurrentPageIndex = 0; PageChanged?.Invoke();
            await OpenPreviewBrowserAsync(result.PdfPath, "index.html", Path.GetFileName(result.PdfPath), version);
        }
        catch (OperationCanceledException) { }
        catch (IOException error) when (error.Message == "文档超过 100 MB 预览大小限制。")
        {
            if (version == _version && !_unloaded)
            {
                _status.Text = error.Message;
                _progress.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            if (version == _version && !_unloaded)
            {
                ReleasePreview();
                _status.Text = "无法预览此文档。文件可能无法访问、受密码保护或已损坏。";
                _status.Visibility = Visibility.Visible;
                _progress.Visibility = Visibility.Collapsed;
                ReportImagePreviewFailure(version);
            }
        }
    }

    private async Task OpenPreviewBrowserAsync(string path, string entry, string fileName, int version)
    {
        _initializeTask ??= InitializeBrowserAsync();
        await _initializeTask;
        if (version != _version || _unloaded) return;
        var core = _browser!.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("document.span.local", Path.GetDirectoryName(path)!, CoreWebView2HostResourceAccessKind.Allow);
        string url = "https://document.span.local/" + fileName;
        _viewerUri = "https://pdfpreview.span.local/" + entry + "?file=" + Uri.EscapeDataString(url) + "&request=" + version;
        _browser.Visibility = Visibility.Visible;
        core.Navigate(_viewerUri);
    }

    private async Task InitializeBrowserAsync()
    {
        var browser = new WebView2
        {
            Visibility = Visibility.Collapsed,
            DefaultBackgroundColor = ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White
        };
        _browser = browser; _root.Children.Insert(0, browser);
        await browser.EnsureCoreWebView2Async();
        if (_unloaded || _browser != browser)
        {
            try { browser.Close(); } catch (InvalidOperationException) { }
            return;
        }
        var core = browser.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping("pdfpreview.span.local", Path.Combine(AppContext.BaseDirectory, "Assets", "PdfViewer"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) &&
                ((uri.Scheme == "https" && uri.Host == "pdfpreview.span.local") ||
                 (uri.Scheme == "https" && uri.Host == "document.span.local" && uri.AbsolutePath is "/source.pdf" or "/preview.pdf" or "/image.png") ||
                 uri.Scheme is "blob" or "data")) return;
            args.Response = core.Environment.CreateWebResourceResponse(new Windows.Storage.Streams.InMemoryRandomAccessStream(), 403, "Blocked", "Content-Type: text/plain");
        };
        core.NavigationStarting += (_, args) =>
        {
            if (args.Uri != "about:blank" && args.Uri != _viewerUri) args.Cancel = true;
        };
        core.FrameNavigationStarting += (_, args) => args.Cancel = true;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += (sender, args) => _ = HandleBrowserMessageAsync(args.Source, args.WebMessageAsJson);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (HasBrowserFocus) return;
        if (args.Key is not (VirtualKey.Space or VirtualKey.Escape)) return;
        var modifiers = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) |
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) |
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((modifiers & CoreVirtualKeyStates.Down) != 0) return;
        args.Handled = true; CloseRequested?.Invoke();
    }

    private void ReleasePreview()
    {
        CancelLiveTextRequests();
        // Clear the OCR source before releasing the browser and its staged PDF.
        _viewerUri = null; ClearPdf();
        if (_browser != null)
        {
            var browser = _browser; _browser = null;
            try { browser.Close(); } catch (InvalidOperationException) { }
            _root.Children.Remove(browser);
        }
        _initializeTask = null;
        RetireOrDispose(_currentResult); _currentResult = null;
        RetireOrDispose(_currentImageSnapshot); _currentImageSnapshot = null;
    }

    public void Cleanup()
    {
        _unloaded = true; ++_version;
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose(); _loadCancellation = null;
        if (_settings != null) _settings.SettingChanged -= OnPreviewSettingChanged;
        ActualThemeChanged -= OnPreviewThemeChanged;
        ReleasePreview();
    }

    public void Dispose() => Cleanup();
}
