$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Views\QuickLookWindow.xaml.cs'))
$handler = [regex]::Match($source,'(?sm)^        private void OnContentPreviewKeyDown\(.*?^        }').Value
if (-not $handler) { throw 'Cannot locate actual Quick Look keyboard handler.' }
$hostSource = @'
using System;
namespace Windows.System { public enum VirtualKey { Escape, Space, Right, Down, Left, Up, Delete, Shift } }
namespace Windows.UI.Core { [Flags] public enum CoreVirtualKeyStates { None, Down } }
namespace Microsoft.UI.Input {
    public static class InputKeyboardSource {
        public static Windows.UI.Core.CoreVirtualKeyStates GetKeyStateForCurrentThread(Windows.System.VirtualKey key) => Windows.UI.Core.CoreVirtualKeyStates.None;
    }
}
public enum PreviewType { Image, Text, Pdf, Media, Office }
public class KeyRoutedEventArgs { public Windows.System.VirtualKey Key; public bool Handled; }
public class Vm { public PreviewType CurrentPreviewType; public bool IsScrollableContent; }
public class Browser { public bool HasBrowserFocus; }
public class Harness {
    public Vm ViewModel = new Vm();
    public Browser DocumentViewer = new Browser();
    public int Closed, SiblingDelta, Deletes;
    public void Close() { Closed++; }
    private void NavigateSibling(int delta) { SiblingDelta += delta; }
    private void RequestDeleteCurrentFile(bool permanent) { Deletes++; }
    public bool Key(Windows.System.VirtualKey key) { var e = new KeyRoutedEventArgs { Key = key }; OnContentPreviewKeyDown(this,e); return e.Handled; }
'@
Add-Type -TypeDefinition ($hostSource + $handler + "`n}")
foreach($kind in @('Image','Text','Pdf','Media','Office')) {
    $window = [Harness]::new()
    $window.ViewModel.CurrentPreviewType = [PreviewType]::$kind
    if (-not $window.Key([Windows.System.VirtualKey]::Space) -or $window.Closed -ne 1 -or $window.SiblingDelta -ne 0) {
        throw "Space must close $kind preview rather than navigating or starting playback."
    }
}
'PASS: Space closes every Quick Look content type without changing the selected file'
$window = [Harness]::new()
$window.Key([Windows.System.VirtualKey]::Right) | Out-Null
if($window.SiblingDelta -ne 1 -or $window.Closed -ne 0){throw 'Right-arrow file browsing regressed.'}
$window.Key([Windows.System.VirtualKey]::Left) | Out-Null
if($window.SiblingDelta -ne 0 -or $window.Closed -ne 0){throw 'Left-arrow file browsing regressed.'}
$window.ViewModel.IsScrollableContent = $true
if($window.Key([Windows.System.VirtualKey]::Down) -or $window.Key([Windows.System.VirtualKey]::Up)) {throw 'Scrollable preview consumed vertical navigation.'}
$window.Key([Windows.System.VirtualKey]::Escape) | Out-Null
if($window.Closed -ne 1){throw 'Escape no longer closes preview.'}
'PASS: arrow browsing, content scrolling and Escape behavior are preserved'
$window = [Harness]::new()
$window.DocumentViewer.HasBrowserFocus = $true
foreach($key in @('Space','Escape','Right','Left','Up','Down','Delete')) {
    if($window.Key([Windows.System.VirtualKey]::$key)){throw "Quick Look intercepted $key while a browser editor had focus."}
}
if($window.Closed -ne 0 -or $window.SiblingDelta -ne 0 -or $window.Deletes -ne 0){throw 'Browser text editing triggered a window, navigation or file-deletion action.'}
'PASS: browser focus preserves editing keys and cannot delete the selected file'
$viewSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\Span\Span\Views\DocumentPreviewView.cs'))
$focusGetter = [regex]::Match($viewSource,'(?sm)^    public bool HasBrowserFocus\s*\{.*?^    }').Value
if(-not $focusGetter){throw 'Cannot locate actual browser focus property.'}
$focusHost = @'
public class DependencyObject { public DependencyObject Parent; }
public static class FocusManager { public static DependencyObject Current; public static object GetFocusedElement(object root) => Current; }
public static class VisualTreeHelper { public static DependencyObject GetParent(DependencyObject value) => value.Parent; }
public class FocusHarness {
    public DependencyObject _browser = new DependencyObject();
    public object XamlRoot = new object();
'@
Add-Type -TypeDefinition ($focusHost + $focusGetter + "`n}")
$focus = [FocusHarness]::new()
[FocusManager]::Current = $focus._browser
if(-not $focus.HasBrowserFocus){throw 'Direct WebView focus was missed.'}
[FocusManager]::Current = [DependencyObject]::new()
[FocusManager]::Current.Parent = $focus._browser
if(-not $focus.HasBrowserFocus){throw 'WebView descendant focus was missed.'}
[FocusManager]::Current = [DependencyObject]::new()
if($focus.HasBrowserFocus){throw 'Unrelated native focus was treated as browser focus.'}
$focus.XamlRoot = $null
if($focus.HasBrowserFocus){throw 'Unloaded XamlRoot reported browser focus.'}
'PASS: the production focus getter recognizes the WebView and its descendants only'
$failureHandler = [regex]::Match($viewSource,'(?sm)^    internal void ReportImagePreviewFailure\(.*?^    }').Value
if(-not $failureHandler){throw 'Cannot locate actual image preview failure handler.'}
$failureHost = @'
using System;
#pragma warning disable CS0067
public class FailureHarness {
    public int _version = 1;
    public bool _unloaded;
    public bool _isImage = true;
    public int _failedImageVersion = -1;
    public event Action ImagePreviewFailed;
    public int Failures;
    public FailureHarness() { ImagePreviewFailed += () => Failures++; }
    private bool IsCurrent(int version) => !_unloaded && version == _version;
    public void Fail(int version) { ReportImagePreviewFailure(version); }
'@
Add-Type -TypeDefinition ($failureHost + $failureHandler + "`n}")
$failure = [FailureHarness]::new()
$failure.Fail(0)
if($failure.Failures -ne 0){throw 'Stale image failure affected the current file.'}
$failure._isImage = $false
$failure.Fail(1)
if($failure.Failures -ne 0){throw 'Document failure incorrectly triggered image fallback.'}
$failure._isImage = $true
$failure.Fail(1)
$failure.Fail(1)
if($failure.Failures -ne 1){throw 'Current image failure must signal exactly once.'}
$failure._version = 2
$failure._unloaded = $true
$failure.Fail(2)
if($failure.Failures -ne 1){throw 'Unloaded image signalled a failure.'}
$failure._unloaded = $false
$failure.Fail(2)
if($failure.Failures -ne 2){throw 'A later image cannot trigger its own native fallback.'}
'PASS: image fallback is current-image-only, once per load and suppressed after unloading'
foreach($file in @('QuickLookWindow.xaml.cs','PreviewPanelView.xaml.cs')) {
    $view = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "..\src\Span\Span\Views\$file"))
    $hostHandler = [regex]::Match($view,'(?sm)^        private void OnImagePreviewFailed\(.*?^        }').Value
    if(-not $hostHandler){throw "Cannot locate native fallback handler in $file."}
    $name = if($file -eq 'QuickLookWindow.xaml.cs'){'QuickLookFallbackHarness'}else{'PanelFallbackHarness'}
    $hostCode = "public class ${name}Vm { public bool IsImageVisible; public string DocumentPreviewPath = `"image.png`"; }`npublic class $name { public ${name}Vm ViewModel = new ${name}Vm(); public void Fail() { OnImagePreviewFailed(); }`n" + $hostHandler + "`n}"
    Add-Type $hostCode
    $hostView = New-Object -TypeName $name
    $hostView.Fail()
    if($hostView.ViewModel.DocumentPreviewPath -ne 'image.png'){throw "Non-image path changed in $file."}
    $hostView.ViewModel.IsImageVisible = $true
    $hostView.Fail()
    if($hostView.ViewModel.DocumentPreviewPath -ne ''){throw "Native image fallback did not restore in $file."}
}
'PASS: both production host handlers restore the native image without changing document previews'
