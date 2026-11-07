---
name: Layout, window dragging and UI performance
description: Grid sizing, title bar regions, window/pane drag, virtualization and perf invariants locked by tests
paths:
  - "src/OrielFolder/OrielFolder/MainWindow*"
  - "src/OrielFolder/OrielFolder/Views/DetailsModeView*"
  - "src/OrielFolder/OrielFolder/Views/ListModeView*"
  - "src/OrielFolder/OrielFolder/Views/IconModeView*"
  - "src/OrielFolder/OrielFolder/Views/HomeModeView*"
  - "src/OrielFolder/OrielFolder/Views/QuickLookWindow*"
  - "src/OrielFolder/OrielFolder/Views/Controls/CaptionButtons*"
  - "src/OrielFolder/OrielFolder/Helpers/CustomWindowFrame.cs"
  - "src/OrielFolder/OrielFolder/Helpers/WindowFrameGeometry.cs"
  - "src/OrielFolder/OrielFolder/Helpers/InlineTreeViewHelper.cs"
  - "src/OrielFolder/OrielFolder/Helpers/HoverShadow.cs"
  - "src/OrielFolder/OrielFolder/Models/PaneLayoutGeometry.cs"
---

# Layout, Window Dragging & Performance

> Many rules here are asserted by `tests/test-*.ps1`. Before changing a method, `grep` its name in
> `tests/` — scripts extract production methods by regex and will fail if their shape changes.

## Title bar & window dragging (current fork implementation)

- **No extended title-bar mode** (`ExtendsContentIntoTitleBar` stays false, no `SetTitleBar()`), in MainWindow and
  QuickLookWindow. WinUI's extended mode caps window dragging at ~110–150 moves/s whatever the content (measured
  with a blank WinUI 3 window, task `11-06-window-drag-stutter`, `research/drag-measurement-20261106.md`);
  without it dragging runs at normal-window speed. `test-titlebar-explicit-regions.ps1` fails if either returns.
- Own frame instead: `Helpers/CustomWindowFrame.cs` subclasses the HWND —
  `WM_NCCALCSIZE` keeps the original client top (caption removed, resize borders kept; maximized → inset by the
  border via `WindowFrameGeometry.AdjustedClientTop`), `WM_NCHITTEST` answers `HTMAXBUTTON` over our maximize
  button (Windows 11 snap layouts). Everything else is declared with `InputNonClientPointerSource.SetRegionRects`.
- Own caption buttons: `Views/Controls/CaptionButtons` (Win11 look, colours from `CaptionPalette`, set by
  `MainWindow.SettingsHandler` for light/dark/custom themes; close hover `#C42B1C`). XAML gets no pointer input
  over non-client regions, so hover/pressed visuals come from `InputNonClientPointerSource.PointerEntered/Exited/
  Pressed/Released` (`RegionKind`). Quick Look shows Close only. Full screen (F11) collapses them.
- Regions are explicit geometry in `MainWindow.TabManager.cs → UpdateTitleBarRegions()` /
  `UpdateCaptionButtonRegions()` (all five kinds in `TitleBarRegionKinds`):
  - one `Caption` rect = `AppTitleBar` bounds minus `AppCaptionButtons.ActualWidth` → whole title-bar height draggable;
  - `Passthrough` rects for each tab element (clipped to the tab ScrollViewer viewport) and the New Tab button;
  - `Minimize` / `Maximize` / `Close` rects from `CaptionButtons.GetRegionRects(scale)` (empty when hidden); the
    maximize rect is also handed to `CustomWindowFrame.MaximizeButtonRect`;
  - rects are cached and only re-sent when changed; window close clears every kind.
- Recompute via `QueueUpdateTitleBarRegions()` on `AppWindow.Changed` (size/presenter/visibility only —
  not position), `Loaded`, `SizeChanged`, tab add/remove/reorder/scroll.
- Use `TransformToVisual(null)` + `XamlRoot.RasterizationScale` for DPI-correct physical rects.
- Never mix `SetTitleBar()` with a `WM_NCHITTEST` override (our override is allowed only because `SetTitleBar` is
  unused). Never leave interactive controls inside the Caption rect without a Passthrough rect.
- Failed approaches (do not retry): moving the window from XAML pointer events (barely drags); answering
  `HTTRANSPARENT` from the hit test (no drag); keeping extended mode and tuning content (cap is in the framework).
- Upstream SPAN docs said "never call `SetRegionRects(Caption, ...)`" — that rule is **superseded** in this fork
  by the explicit-geometry design above. Do not "fix" it back.

## Home page cards

- `HomeModeView` card GridViews: the container's own hover/pressed chrome is made transparent via lightweight
  resources (`GridViewItemBackgroundPointerOver/Pressed`, `GridViewItemPointerOverBorderBrush`) and
  `GridViewItemCornerRadius` = 10 (keyboard focus ring matches the cards). Hover feedback is a soft rounded shadow
  (`Helpers/HoverShadow.cs`, Composition `DropShadow` masked by a rounded rect, offset 0) on an empty `Border`
  behind each card; GridViews have `Padding="20" Margin="-20"` so the shadow is not clipped.
- Hover clearing must not rely on `PointerExited` alone (a fast exit can report the last point inside the card and
  left the shadow stuck): one hovered card at a time + a `PointerMoved`/`PointerExited` watcher (handledEventsToo)
  on the window content.
- Row stripes (`InlineTreeViewHelper`): `RefreshStripeTree` only re-stripes lists that opted in by calling
  `ApplyStripe` (Miller, Details, List). It used to stripe every `ListViewBase` under the pane host, which framed
  every other Home card after a theme/system-setting change (`test-row-stripe-scroll-cost.ps1` locks this).

## Pane dividers & column resize

- The split-pane divider is the existing **1-px `Border x:Name="PaneDivider"`** in `MainWindow.xaml`
  (`IsHitTestVisible="True"`, no children, pointer handlers `OnPaneDividerDragStarted/Drag/DragCompleted/
  CaptureLost` in `MainWindow.PaneSizing.cs`, SizeWestEast cursor). Do not replace it with a wide splitter,
  add children, or change its width (`test-pane-layout.ps1`, `test-pane-divider-drag.ps1`).
- Dragging captures the pointer, resizes both panes, stops on capture loss, and only affects the tab it started on.
- Ratios go through `PaneLayoutGeometry.ClampSplitRatio` (min visible share 0.15/0.85, 0.5 when too narrow).
- Outer column drag resizes the **window** via `PaneLayoutGeometry.WindowWidthForColumnDrag` clamped to the
  display work area (multi-monitor negative coordinates included) — `MainWindow.OuterColumnResize.cs`.
- Quad dividers and z-order: divider above pane layers; activating a floating pane raises it without
  accumulating Z indices; inactive cached panes unchanged (`test-workspace-z-order.ps1`, `test-quad-divider-events.ps1`).
- Single pane must not leave an empty gap; the split line must have layout space.

## Grid sizing

- Miller column area = `Star`, preview = `Pixel`. Making both `Star` causes proportional jumps on window resize
  (`ApplyMillerColumnWidth()` in SplitPreviewManager).
- Details header Grid and row template Grid compute `*` independently; row cells get their widths from the header
  `ColumnDefinition.ActualWidth`s (`RefreshColumnWidths` → `ApplyCellWidths`) — don't round per cell.
- Details column resize is Explorer-style (`DetailsModeView.Columns.cs`, rules in `Helpers/DetailsColumnLayout`):
  the separator between two columns only resizes the column on its left; every column to its right keeps its width
  and moves. Name stretches (`*`) until the first separator drag, which fixes Name to its current width; free space
  then goes to a trailing filler column. Separators are our own 8-px hit areas over the 2-px gap columns (rows keep
  the matching 2-px right margin) with a visible 1-px line (`ColumnSeparatorLineStyle`) — no `GridSplitter`
  (its PreviousAndNext behaviour moved the neighbour instead). Widths are saved in `DetailsViewSort.ColumnWidths`
  and broadcast to all Details views like the column order. Wider columns scroll horizontally: the ListView has
  horizontal scrolling and the header sits in `HeaderScroller` (MinWidth = view width), kept at the list's
  horizontal offset from `ViewChanging`/`ViewChanged`.
- Header menu: every column except search-only Location can be toggled (at least one stays visible); labels are
  localized. The Git menu entry is a preference (`GitColumnWanted`): the column still appears only in Git folders.
- `GridSplitter` / sizers must account for `RasterizationScale`.

## Virtualization & hot paths (performance budgets enforced by tests)

- In `ContainerContentChanging` (measure pass) never change `Margin` of row elements (COMException 0x8000FFFF);
  only `Width` changes are safe there.
- **Unchanged row layout must issue zero DependencyProperty writes**; a width change updates only that cell
  (`test-detail-row-layout-cost.ps1`). Row stripes must not re-invalidate unchanged rows, and system contrast
  (`AccessibilitySettings`) must not be read per row — cache the palette, refresh on theme/contrast change
  (`test-row-stripe-scroll-cost.ps1`).
- Phased row display: first frame lays out names only; metadata in a later phase without re-layout; thumbnail
  decoding never in the first-frame phase; recycled rows must not load discarded content
  (`test-details-scroll-phases.ps1`).
- Folder loading: cache-first, cancellable, stale loads must not populate folders
  (`test-folder-loading-performance.ps1`); folder sizes/child counts are subscriptions, not blocking work.
- Context menus, Shell "New", tab menus must stay responsive (dedicated `test-*-responsiveness.ps1`,
  `test-tab-menu-dispose-performance.ps1`, `test-workspace-activation-performance.ps1`).
- Thumbnails are produced by the out-of-process `OrielFolder.Thumbs` worker; never decode large images on the UI thread.
- GC and XAML element count (measured 2026-11-07, Debug, 6 System32 tabs): WinUI reports each XAML element's
  native memory via `GC.AddMemoryPressure`, which induces Gen2 GCs (1–2 per frame while rows realize); each Gen2
  walks every live XAML object, hidden tabs included, so its pause grows with tab count (22→141 ms).
  `Helpers/GcBallast` (512 MB untouched array, Program.Main) raises the pressure threshold: GC time per new tab
  1.0–1.4 s → 0.1–0.6 s. Server GC / non-concurrent GC: no effect. The Details row template binds font sizes
  with `x:Bind helpers:FontScaleService.Instance.*` (no `{Binding}` objects): alone no effect, but with the
  ballast fewer objects pile up between Gen2s (per-Gen2 ~220 → ~155 ms, working set ~650 → ~610 MB at 6 tabs
  vs 525 MB originally). Guarded by `DetailsRowTemplateTests`.
- Assigning `ItemContainerStyle` discards and rebuilds every realized row. `ApplyDensity` in Details/List/Icon
  only replaces the style when the row height (or Icon spacing) changes; startup re-applies density after the
  first rows are shown, which used to build the first screen twice (cold start System32: rows 208 → 159,
  stuck time in the first 3 s 849 → 454 ms).
- Do **not** use data-driven `x:Load="{x:Bind …}"` in list row templates: a new or recycled container bound to an
  item whose condition is already true never creates the element (Git M/? disappeared), and re-raising
  PropertyChanged does not recover it. Fewer row elements would roughly halve each Gen2 pause; it needs another
  mechanism.

## Performance work rules

Measure before/after with the existing perf scripts or a new focused one. Record measured numbers vs.
hypotheses in the task `handoff.md`. Record regressions and failed experiments so the next agent does not repeat them.
