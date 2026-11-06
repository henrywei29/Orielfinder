# Handoff — 11-06-window-drag-stutter

## Current State
- **TASK_ID:** 11-06-window-drag-stutter   - **Updated:** 2026-11-06 (local)
- **Agent:** Claude Code
- **Task & phase:** Implementation done and **runtime-verified by the user** in Dev (drag, tabs/tear-off, caption
  buttons incl. red close + snap layouts, F11, themes/inactive, Quick Look, second monitor: "都测试了，通过").
  Spec updated. Committed on `rename/orielfolder` (not pushed). Trellis finish/archive only with user approval.
- **Git:** branch `rename/orielfolder`. Dev-uninstall task committed separately (`00bfa7e`); this task committed
  after the drag recording at the user's request ("录制一下速度再提交").

### What changed (product)
- Own title-bar frame (no extended mode): `Helpers/CustomWindowFrame.cs`, `Helpers/WindowFrameGeometry.cs`,
  `Views/Controls/CaptionButtons.xaml(.cs)`, `MainWindow.xaml(.cs)`, `MainWindow.TabManager.cs`,
  `MainWindow.SettingsHandler.cs`, `MainWindow.KeyboardHandler.cs`, `Views/QuickLookWindow.xaml(.cs)`.
- Found during the runtime check (pre-existing bug, not caused by the frame): every other Home card got a
  black/white frame after a theme or system-setting change — `InlineTreeViewHelper.RefreshStripeTree` striped every
  `ListViewBase`, including the Home GridViews. Now only lists that opted in via `ApplyStripe` are refreshed.
- User-requested Home card hover redesign: container hover chrome hidden, soft rounded shadow on hover
  (`Helpers/HoverShadow.cs`, `Views/HomeModeView.xaml`). First version could leave the shadow stuck after a fast
  exit (the exit reported the last point inside) → one hovered card at a time + window-level PointerMoved watcher.
  User: "不卡了".

### Verification
- `build-dev.ps1`: 0 errors. Unit tests (`.tools` dotnet, restore + `test --no-restore`): 1183 passed / 0 failed /
  3 skipped (incl. 7 `WindowFrameGeometryTests`).
- `tests/test-titlebar-explicit-regions.ps1` (rewritten): PASS through the .NET SDK fixture harness
  `D:\codex\Orielfolder-dev\window-frame-20261106\fixture-harness\run.ps1`.
- `tests/test-row-stripe-scroll-cost.ps1` (+ assertion: tree refresh must not touch non-striped lists): FAIL with the
  HEAD helper, PASS with the new one, through `D:\codex\Orielfolder-dev\window-frame-20261106\stripe-harness`.
- **Deviation from AC:** both scripts compile production C# (C# 6+ / .NET Core APIs) and need PowerShell 7; they do
  not run on Windows PowerShell 5.1 (PS7 is not installed here) — verified via the .NET 8 SDK harnesses instead.
- Drag rate re-recorded in OrielFolder Dev (sampler, `D:\codex\Orielfolder-dev\window-frame-20261106\runtime-2`):
  main window **461 moves/s** (26.8 s), Quick Look **460/s** (15.5 s), Explorer same session 426/s; interval p50
  2.0 ms; 1 freeze (Explorer also 1). Before: Dev 85–122/s. Target ≥ 300/s met.
- Home card shadow / stripe fix: checked by the user in Dev (light and dark, theme switches, fast sweeps).

### Files outside the repo
- `D:\codex\Orielfolder-dev\window-drag-stutter-20261106\` — sampler, analysis, recordings, test app.
- `D:\codex\Orielfolder-dev\window-frame-20261106\` — fixture-harness, stripe-harness, runtime-1 (empty recording).

### Failed approaches
- Mica off (no change); manual `WM_NCLBUTTONDOWN` from XAML pointer (no change / unreliable); Win32 `HTTRANSPARENT`
  pass-through through the WinUI island (does not drag); `AppWindow.TitleBar.ExtendsContentIntoTitleBar` (also capped).
- Home frame: first assumed the keyboard focus visual / container corner radius — a focus log showed only Pointer
  focus; the real cause was the stripe refresh. Hover shadow: PointerExited + inside check alone gets stuck.
- Checking HKCU/AppData from the Claude shell is virtualised (see uninstall task) — use WMI-started processes.

### Next step
User decides on committing (both tasks share the working tree) and on Trellis finish/archive.

## Checkpoint Log
- 2026-11-06 [Claude Code] Measured drag (Dev 85–122/s, Explorer ~300/s, release 64–70/s); Mica ruled out (temporary Dev diagnostic, reverted).
- 2026-11-06 [Claude Code] Test app rounds 1–9: extended title bar is the cap; W3 recipe (own frame, own caption buttons, HTMAXBUTTON) verified with user.
- 2026-11-06 [Claude Code] Planning artifacts written; D1/D2 recorded; awaiting approval.
- 2026-11-06 [Claude Code] Implemented own frame + caption buttons (MainWindow, Quick Look); build/unit/static tests green.
- 2026-11-06 [Claude Code] Drag recording in OrielFolder: main 461/s, Quick Look 460/s, Explorer 426/s.
- 2026-11-06 [Claude Code] User runtime check passed. Fixed Home card frame (stripe refresh opt-in, test assertion added), redesigned Home hover as soft shadow, fixed stuck shadow. Specs updated.
