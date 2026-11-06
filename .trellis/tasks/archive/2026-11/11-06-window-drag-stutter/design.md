# Design — own title-bar frame for MainWindow and Quick Look

Evidence: `research/drag-measurement-20261106.md` (rounds 1–9, test app `D:\codex\Orielfolder-dev\window-drag-stutter-20261106\winui-test`,
recipe = `src\App.xaml.cs` window W3).

## Principle

Both windows stay `Microsoft.UI.Xaml.Window`. Extended title-bar mode is switched **off** everywhere (it is the cause of
the ~120 moves/s cap). We remove the system caption ourselves at Win32 level and keep using
`InputNonClientPointerSource` for every non-client region. Nothing else about the windows changes.

## Components

| Piece | Location | Role |
|---|---|---|
| `WindowFrameGeometry` (pure, no WinRT) | `Helpers/WindowFrameGeometry.cs` | `AdjustClientTop(origTop, defaultLeft, origLeft, isZoomed, hasCaptionStyle)` → new top for `WM_NCCALCSIZE`; `IsInRect(x, y, rect)` for hit tests. Unit-tested |
| `CustomWindowFrame` | `Helpers/CustomWindowFrame.cs` | Attaches to a window HWND with `SetWindowSubclass` (own id). `WM_NCCALCSIZE` (wParam≠0): call default, then restore top via `WindowFrameGeometry` (maximized → border inset; window without `WS_CAPTION`, e.g. F11 full screen → untouched). `WM_NCHITTEST`: return `HTMAXBUTTON` when the point is in the maximize-button rect (Win11 snap layouts), else default. `SetWindowPos(SWP_FRAMECHANGED)` once on attach. Removes the subclass on `WM_NCDESTROY` |
| `CaptionButtons` control | `Views/Controls/CaptionButtons.xaml(.cs)` | Min / Max-Restore / Close buttons (46 × title-bar height, Segoe Fluent glyphs E921/E922/E923/E8BB). `ShowMinimize`, `ShowMaximize` flags (Quick Look: close only). Visual states driven from `InputNonClientPointerSource.PointerEntered/Exited/Pressed/Released` by `RegionKind` (XAML does not see non-client pointers). Close hover = red `#C42B1C` + white glyph (prototype bug: background set but not visible → implement with a `Border` background, not `Button.Background`). Colors from a caption palette (normal/hover/pressed/inactive fg/bg) |
| MainWindow wiring | `MainWindow.xaml(.cs)`, `MainWindow.TabManager.cs`, `MainWindow.SettingsHandler.cs`, `MainWindow.KeyboardHandler.cs` | see below |
| Quick Look wiring | `Views/QuickLookWindow.xaml(.cs)` | see below |

## MainWindow changes (current anchors)

- `MainWindow.xaml.cs:581-589`: drop `ExtendsContentIntoTitleBar = true` (and `SetTitleBar(null)`); attach
  `CustomWindowFrame` after `_hwnd` is known (`:661`). Keep the `AppWindow.Changed` geometry-only handler.
- `MainWindow.xaml:479` `RightPaddingColumn` → hosts `CaptionButtons` (Auto width) instead of empty padding.
- `MainWindow.TabManager.cs` `UpdateTitleBarRegions()`: remove the `ExtendsContentIntoTitleBar` gate (`:1511`); caption
  rect = `AppTitleBar` bounds minus the caption-buttons width (replaces `AppWindow.TitleBar.RightInset`, `:1520,1531`);
  add `Minimize`/`Maximize`/`Close` region rects from the buttons; pass the maximize rect to `CustomWindowFrame`.
  Keep caching (only re-send changed rects), Passthrough for tabs and New Tab, DPI via `RasterizationScale`.
- `ExpandTitleBarPassthrough()` (`:1594-1616`): same gate/RightInset replacement; caption buttons keep their regions.
- `MainWindow.xaml.cs:1428` close teardown: clear all five region kinds (gate removed).
- `MainWindow.SettingsHandler.cs:83-…` caption colors: apply `GetCaptionColors(theme)` / light-dark defaults to
  `CaptionButtons` instead of `AppWindow.TitleBar`.
- `MainWindow.KeyboardHandler.cs:1596` F11: hide `CaptionButtons` (and their regions) in full screen, restore after.
- Max/restore glyph and button state follow `AppWindow.Changed` (`DidPresenterChange`/`DidSizeChange`) + `IsZoomed`.

## Quick Look changes

- `QuickLookWindow.xaml.cs:118-131`: keep Mica; drop both `ExtendsContentIntoTitleBar` lines and
  `SetTitleBar(QuickLookTitleBar)`; attach `CustomWindowFrame` (not maximizable → no `HTMAXBUTTON`).
- `QuickLookWindow.xaml:32-36`: `TitleRightPadding` column hosts `CaptionButtons` with close only.
- Explicit regions: Caption = `QuickLookTitleBar` minus close button, Close = close button; recompute on size/DPI change.
- `UpdateTitleBarPadding()` (fixed 138 px) removed.

## Safety / compatibility

- Spec rule "never mix `SetTitleBar()` with a `WM_NCHITTEST` override": satisfied — `SetTitleBar` is no longer used in
  either window; the override only answers `HTMAXBUTTON`.
- Close-to-tray: close region → `SC_CLOSE` → `AppWindow.Closing` (existing handler `MainWindow.xaml.cs:550`) unchanged.
- Window placement restore, outer-column window resize, tab tear-off, multi-window: use outer window rects / `AppWindow`
  and are unaffected; verify at runtime.
- Windows 10: no snap layouts by design of the OS; everything else works.
- Dev only. No identity, packaging or release change.

## Rollback

No runtime switch. All changes are on a non-`main` branch in a few files; revert = restore those files. Pre-edit
copies go to `D:\codex\Orielfolder-dev\window-frame-20261106\backup\`.

## Trade-offs

- Own caption buttons must imitate the system look; small visual differences are possible (accepted by D1).
- `InputNonClientPointerSource` events are the only hover source for the buttons; if a future Windows App SDK changes
  them, hover visuals degrade but clicks keep working (system handles the regions).
