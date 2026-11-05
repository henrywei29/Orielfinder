using System;
using System.IO;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Span.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Span.Views;

/// <summary>Explicit, local-only OCR for the selected image or current PDF page.</summary>
public sealed class OcrTextPanel : UserControl
{
    private readonly OcrService _service = new();
    private readonly OcrRequestState _requests = new();
    private readonly Button _recognize = new();
    private readonly Button _copy = new() { IsEnabled = false };
    private readonly ProgressRing _progress = new() { Width = 16, Height = 16, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };
    private readonly TextBox _output = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 100,
        MaxHeight = 220,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private LocalizationService? _localization;
    private string _statusKey = "OcrHint";
    private string _statusFallback = "点击后使用本机 OCR 识别图片或当前 PDF 页。";
    private string? _languageTag;

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(OcrTextPanel), new PropertyMetadata("", OnSourceChanged));

    public string SourcePath
    {
        get => (string)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value ?? "");
    }

    public static readonly DependencyProperty PdfPageIndexProperty = DependencyProperty.Register(
        nameof(PdfPageIndex), typeof(int), typeof(OcrTextPanel), new PropertyMetadata(0, OnSourceChanged));

    public int PdfPageIndex
    {
        get => (int)GetValue(PdfPageIndexProperty);
        set => SetValue(PdfPageIndexProperty, value);
    }

    public OcrTextPanel()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_recognize);
        buttons.Children.Add(_copy);
        buttons.Children.Add(_progress);
        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_output);
        Content = root;
        ScrollViewer.SetVerticalScrollBarVisibility(_output, ScrollBarVisibility.Auto);
        _recognize.Click += OnRecognize;
        _copy.Click += OnCopy;
        _output.AddHandler(KeyDownEvent, new KeyEventHandler(OnOutputKeyDown), true);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ResetSource();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _localization = App.Current?.Services.GetService(typeof(LocalizationService)) as LocalizationService;
        if (_localization != null) _localization.LanguageChanged += Localize;
        Localize();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_localization != null) _localization.LanguageChanged -= Localize;
        _localization = null;
        ResetSource();
    }

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((OcrTextPanel)sender).ResetSource();

    private void ResetSource()
    {
        _requests.Invalidate();
        _languageTag = null;
        _output.Text = "";
        _output.Visibility = Visibility.Collapsed;
        _copy.IsEnabled = false;
        SetBusy(false);
        SetStatus("OcrHint", "点击后使用本机 OCR 识别图片或当前 PDF 页。");
    }

    private async void OnRecognize(object sender, RoutedEventArgs e)
    {
        var path = SourcePath;
        var page = PdfPageIndex;
        if (string.IsNullOrWhiteSpace(path)) return;
        var request = _requests.Begin();
        _languageTag = null;
        _output.Text = "";
        _output.Visibility = Visibility.Collapsed;
        _copy.IsEnabled = false;
        SetBusy(true);
        SetStatus("OcrWorking", "正在本机识别…");
        try
        {
            var result = string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)
                ? await _service.RecognizePdfPageAsync(path, page, request.CancellationToken)
                : await _service.RecognizeImageAsync(path, request.CancellationToken);
            if (!_requests.IsCurrent(request)) return;
            if (result.Error != OcrError.None)
            {
                var (key, fallback) = result.Error switch
                {
                    OcrError.NoLanguage => ("OcrNoLanguage", "未找到本机 OCR 语言资源。请在 Windows 语言设置中添加中文或英文 OCR。"),
                    OcrError.InvalidSource => ("OcrInvalidSource", "无法读取此文件，或文件为空。"),
                    OcrError.InvalidPage => ("OcrInvalidPage", "此 PDF 页面已不可用。"),
                    OcrError.FileTooLarge => ("OcrFileTooLarge", "文件超过 100 MB，无法识别。"),
                    _ => ("OcrFailed", "无法识别此文件。请检查图片或 PDF 是否有效。")
                };
                SetStatus(key, fallback);
                return;
            }
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                SetStatus("OcrEmpty", "未识别出文字。请尝试更清晰的图片。");
                return;
            }
            _output.Text = result.Text;
            _output.Visibility = Visibility.Visible;
            _copy.IsEnabled = true;
            _languageTag = result.LanguageTag;
            SetStatus("OcrComplete", "识别完成");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (_requests.IsCurrent(request)) SetStatus("OcrFailed", "无法识别此文件。请检查图片或 PDF 是否有效。");
        }
        finally
        {
            if (_requests.IsCurrent(request)) SetBusy(false);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_output.Text)) return;
        try
        {
            var content = new DataPackage();
            content.SetText(_output.Text);
            Clipboard.SetContent(content);
            SetStatus("OcrCopied", "已复制识别文字");
        }
        catch (Exception) { SetStatus("OcrCopyFailed", "剪贴板暂时不可用。请选中文字后按 Ctrl+C 重试。"); }
    }

    private void SetBusy(bool busy)
    {
        _recognize.IsEnabled = !busy && !string.IsNullOrWhiteSpace(SourcePath);
        _progress.IsActive = busy;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string key, string fallback)
    {
        _statusKey = key;
        _statusFallback = fallback;
        Localize();
    }

    private void Localize()
    {
        _recognize.Content = Text("OcrRecognize", "识别文字");
        _copy.Content = Text("OcrCopy", "复制全部");
        AutomationProperties.SetName(_output, Text("OcrResult", "识别结果"));
        _status.Text = Text(_statusKey, _statusFallback) + (string.IsNullOrEmpty(_languageTag) ? "" : $" · {_languageTag}");
    }

    private static string Text(string key, string fallback)
    {
        var localized = LocalizationService.L(key);
        return localized == key ? fallback : localized;
    }

    private static void OnOutputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Keep native Ctrl+C selection behavior. Plain Space/Escape must reach
        // Quick Look's parent key handler even when the read-only box has focus.
        if (e.Key is not (VirtualKey.Space or VirtualKey.Escape)) return;
        foreach (var modifier in new[] { VirtualKey.Control, VirtualKey.Shift, VirtualKey.Menu })
            if ((InputKeyboardSource.GetKeyStateForCurrentThread(modifier) & CoreVirtualKeyStates.Down) != 0) return;
        e.Handled = false;
    }
}
