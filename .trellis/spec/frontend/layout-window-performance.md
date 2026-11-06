---
name: Layout, window dragging and UI performance
description: Grid sizing, title bar regions, window/pane drag, virtualization and perf invariants locked by tests
paths:
  - "src/OrielFolder/OrielFolder/MainWindow*"
  - "src/OrielFolder/OrielFolder/Views/DetailsModeView*"
  - "src/OrielFolder/OrielFolder/Views/ListModeView*"
  - "src/OrielFolder/OrielFolder/Views/IconModeView*"
  - "src/OrielFolder/OrielFolder/Models/PaneLayoutGeometry.cs"
---

# Layout, Window Dragging & Performance

> Many rules here are asserted by `tests/test-*.ps1`. Before changing a method, `grep` its name in
> `tests/` — scripts extract production methods by regex and will fail if their shape changes.

## Title bar & window dragging (current fork implementation)

- The window extends content into the title bar. **This fork no longer uses `SetTitleBar(AppTitleBar)`**
  (`test-titlebar-explicit-regions.ps1` fails if it returns). Regions are explicit geometry in
  `MainWindow.TabManager.cs → UpdateTitleBarRegions()`:
  - one `NonClientRegionKind.Caption` rect = full `AppTitleBar` bounds minus `AppWindow.TitleBar.RightInset`
    (system caption buttons untouched) → the whole visual title-bar height is draggable;
  - `NonClientRegionKind.Passthrough` rects for each tab element (clipped to the tab ScrollViewer viewport)
    and the New Tab button;
  - rects are cached (`_lastTitleBarCaptionRect`, `_lastTitleBarRects`) and only re-sent when changed.
- Recompute via `QueueUpdateTitleBarRegions()` on `AppWindow.Changed` (size/presenter/visibility only —
  not position), `Loaded`, `SizeChanged`, tab add/remove/reorder/scroll.
- Use `TransformToVisual(null)` + `XamlRoot.RasterizationScale` for DPI-correct physical rects.
- Never mix `SetTitleBar()` with a `WM_NCHITTEST` override. Never leave interactive controls inside the
  Caption rect without a Passthrough rect (they stop receiving clicks).
- Upstream SPAN docs said "never call `SetRegionRects(Caption, ...)`" — that rule is **superseded** in this fork
  by the explicit-geometry design above. Do not "fix" it back.

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
- Details header Grid and row template Grid compute `*` independently; splitter widths are accumulated from
  header `ColumnDefinition`s in `RecalcCellTotalWidths()` — don't round per cell.
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

## Performance work rules

Measure before/after with the existing perf scripts or a new focused one. Record measured numbers vs.
hypotheses in the task `handoff.md`. Record regressions and failed experiments so the next agent does not repeat them.
