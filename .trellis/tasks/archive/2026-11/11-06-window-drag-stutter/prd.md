# Smooth window dragging for OrielFolder Dev (own title-bar frame)

## Goal

Dragging the OrielFolder **Dev** main window and Quick Look window should be as smooth as a normal window
(Explorer / plain WinUI window), keeping the tabs-in-title-bar design. Dev only
(`.trellis/spec/project/dev-release-boundary.md`).

## Background (2026-11-06, evidence in `research/drag-measurement-20261106.md`)

- Measured on the user's 240 Hz panel: OrielFolder Dev moves 85–122 times/s while dragging, Explorer 268–313/s,
  installed release OrielFolder 64–70/s. UI thread not blocked; no app code runs per move.
- Root cause (minimal WinUI 3 test app, Windows App SDK 1.8.260209005): any WinUI 3 window in **extended title-bar mode**
  (`Window.ExtendsContentIntoTitleBar` or `AppWindow.TitleBar.ExtendsContentIntoTitleBar`) is capped at 110–150 moves/s.
  Ruled out: Mica, OrielFolder code, `InputNonClientPointerSource`, starting the move manually.
- Verified recipe (test windows W/W2/W3, user-checked): keep `Microsoft.UI.Xaml.Window` without extended mode; subclass
  `WM_NCCALCSIZE` (drop caption, border inset when maximized) and `WM_NCHITTEST` (`HTMAXBUTTON` → snap layouts); XAML
  title row; `InputNonClientPointerSource` regions Caption/Passthrough/Minimize/Maximize/Close; own caption buttons.
  Result 395–419 moves/s; tabs clickable; min/max/restore/double-click/close work; maximized not clipped; snap layouts
  shown. Prototype bug: close button not red on hover (event arrives; visual only).
- Current code anchors: extended mode `MainWindow.xaml.cs:581-589`; `RightInset` `MainWindow.TabManager.cs:1520,1531,1605`;
  `ExtendsContentIntoTitleBar` gates `MainWindow.TabManager.cs:1511,1594`, `MainWindow.xaml.cs:1428`; caption colors
  `MainWindow.SettingsHandler.cs:83`; F11 `MainWindow.KeyboardHandler.cs:1596`; existing subclass
  `MainWindow.xaml.cs:787-788,1648`; Quick Look `Views/QuickLookWindow.xaml.cs:118-131`, `QuickLookWindow.xaml:32-36`
  (close-only: not minimizable/maximizable). Region logic is asserted by `tests/test-titlebar-explicit-regions.ps1`
  (PowerShell 7 only today). Settings/Onboarding windows use the system title bar (unaffected).

## Decisions

- D1 (2026-11-06, user: "保留窗口，自己做标题栏"): keep `Window`; replace extended mode with our own frame and own caption
  buttons. Not chosen: system title bar (look changes), Win32 host + `DesktopWindowXamlSource` (largest migration).
- D2 (2026-11-06, user: "主窗口和 Quick Look 一起改"): both MainWindow and Quick Look, with shared frame/caption code.
- Proposed with the final summary (need approval): caption buttons imitate the Windows 11 system buttons and use the
  existing per-theme caption colors; no runtime switch between old/new title bar (rollback = branch/backups).

## Requirements

- R1 MainWindow and Quick Look no longer use extended title-bar mode; drag speed like a normal window.
- R2 MainWindow: tabs, New Tab button, tab drag/tear-off keep working; empty title area drags; double-click maximizes/restores.
- R3 Own caption buttons: MainWindow min / max-restore / close, Quick Look close only; Win11 snap layouts on maximize hover;
  hover/pressed states; close hover red; colors follow theme (incl. custom themes) and inactive state.
- R4 Maximized window not clipped; F11 full screen hides caption buttons and restores them; DPI/monitor change keeps
  regions aligned; close-to-tray, window placement restore, multi-window and Quick Look positioning unchanged.
- R5 Shared, testable code: pure `WindowFrameGeometry` (unit tests), `CustomWindowFrame`, `CaptionButtons` control.
- R6 Dev only; no release identity / packaging / `main` changes.

## Acceptance Criteria

- [ ] Drag sampler: Dev main window and Quick Look ≥ 300 moves/s on the 240 Hz panel (today 85–122), no freezes > 40 ms
      while the app is idle. (R1)
- [ ] Tabs clickable/draggable/tear-off; New Tab works; empty title area drags; double-click maximizes/restores. (R2)
- [ ] Caption buttons work; snap layouts appear on maximize hover; close hover red; light, dark and one custom theme
      look right; inactive window dims the glyphs. (R3)
- [ ] Maximized window fully visible; F11 enters/leaves full screen correctly; moving to another DPI monitor keeps
      clicks/drag aligned; close-to-tray still works; Quick Look opens centered and closes with its button. (R4)
- [ ] New unit tests and updated `test-titlebar-explicit-regions.ps1` (runnable on PowerShell 5.1) pass; existing unit
      tests green; `build-dev.ps1` 0 errors. (R5)
- [ ] User accepts the look and feel. (R3)

## Out of Scope

- Release build, packaging, installer, `main`.
- Settings / Onboarding windows (system title bar).
- Other performance work (folder loading hitches seen in round 1).
