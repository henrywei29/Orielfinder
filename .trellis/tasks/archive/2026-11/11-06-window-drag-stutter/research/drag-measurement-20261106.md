# Window drag measurement — 2026-11-06 15:04–15:07

Tool: `D:\codex\Orielfolder-dev\window-drag-stutter-20261106\drag-sampler.ps1` (read-only; samples foreground window
rect + cursor every ~1 ms while the left button is held, and WM_NULL round-trip to the window every ~5 ms).
Analysis: `analyze.py`, `gaps2.py` in the same folder. Raw CSVs there too.

Subject: OrielFolder Dev (Debug build, PID 18284), user dragged it 18 times, ~91 s total. No Explorer / release
comparison was recorded (only Dev was dragged).

## Results
- Window position updates while dragging: ~102/s, median 8.2 ms, p95 12.0 ms, p99 22.2 ms.
- After removing hand pauses (cursor moved < 8 px) and button releases, only **one real freeze**: 79 ms at 15:07:06.4,
  ~1 s after navigating to Downloads (159 items, icons from cache). WM_NULL probe in that window: 64 ms → UI thread busy.
- UI-thread WM_NULL latency over the whole drag time: p50 2.2 ms, p95 5.8 ms, p99 8.3 ms, max 64 ms (11 samples > 16 ms of 11,597).
- Drag start delay (cursor moved 4 px → window moves): 0–29 ms.
- Window lag behind cursor: p50 5 px, p95 25 px (Claude window in a short sample: p95 17 px).
- Code on the move path: `AppWindow.Changed` only reacts to size/presenter/visibility; WndProc subclass handles theme,
  DPI and device change only. Nothing runs per move.

## Interpretation
- Position tracking is close to smooth; the measured stutter is rare and coincides with app work (folder load / layout).
- Not measured: content rendering/composition (Mica, swap chain) — a stutter that is visual only would not show here.
- Dev runs the **Debug** configuration (unoptimized JIT); release may behave differently. Needs a comparison run.

# Comparison run — 2026-11-06 (folder `...\window-drag-stutter-20261106\compare`, analysis `analyze2.py`)

Dragged: OrielFolder Dev (PID 18284, 81 s) and File Explorer (PID 27920, 24 s). Release OrielFolder was running but
not dragged → no release data.

| | Explorer | OrielFolder Dev |
|---|---|---|
| Window position updates per second | **313** | **122** |
| Interval median / p99 / max (cursor moving ≥ 8 px) | 4.3 / 23.8 / 30 ms | 8.0 / 13.4 / 36 ms |
| Freezes > 40 ms | 0 | 0 |
| UI-thread WM_NULL p99 / max | 7.8 / 29 ms | 7.7 / 30 ms |
| Lag behind cursor p50 / p90 / p99 | 5 / 13 / 45 px | 7 / 19 / 36 px |

Display: primary `\.\DISPLAY2`, 240 Hz (only screen reported in this session; Win32_VideoController also lists an
NVIDIA RTX 4060 Laptop 180 Hz output and a spacedesk virtual adapter 120 Hz — hybrid-graphics laptop).

## Finding
- No freezes and a responsive UI thread in this run. The difference is the **update rate**: the Dev window moves
  ~120 times/s, i.e. on about every second frame of the 240 Hz panel, while Explorer moves every frame. Uneven
  frame-to-frame motion = the "slight stutter" the user describes (hypothesis, consistent with the data).
- The ~8.3 ms cadence looks like a 120 Hz clock, not app work (per-move app code: none; UI thread idle).
- Candidates (not yet tested): (1) WinUI 3 / composition sync of the window move — would affect release too;
  (2) `MicaBackdrop` (set unconditionally in `MainWindow.xaml.cs:537`) resampling on move; (3) hybrid graphics —
  app rendering on one GPU, panel on the other; (4) a 120 Hz device in the display chain (spacedesk adapter).
- Next experiments: drag the release OrielFolder (same framework, Release build); Dev build with Mica off
  (Dev-only diagnostic change, needs approval); per-app GPU preference test (user changes Windows Graphics setting).

# Mica-off run — 2026-11-06 15:18–15:21 (folder `...\window-drag-stutter-20261106\nomica`)

Temporary Dev-only diagnostic in `MainWindow.xaml.cs` (marker `D:\codex\OrielFolderDev\diagnostics\no-mica` skipped
`MicaBackdrop`); confirmed by log line `[Diagnostics] Mica disabled`. Reverted afterwards (file identical to backup
`...\window-drag-stutter-20261106\backup\MainWindow.xaml.cs`, no git diff), marker deleted, Dev rebuilt + restarted (PID 50528).

| | Window moves/s | Interval median / p99 (ms) | UI-thread WM_NULL p95 / max |
|---|---|---|---|
| Dev, Mica on (earlier runs) | 122 | 8.0 / 13.4 | 4.8 / 30 |
| Dev, **Mica off** | 111 | 7.7 / 11.8 | 4.6 / 20 |
| Release OrielFolder 1.0.0.0 (two windows, Mica on) | **64–70** | 13.7–14.6 / 20–37 | 11.8–16.3 / 42 |
| File Explorer (comparison run) | 313 | 4.3 / 23.8 | 4.2 / 29 |

## Conclusions
- **Mica is not the cause** (rate unchanged within noise).
- **Dev is already smoother than the installed release** (≈ 1.7× the move rate, lower UI-thread latency) — matches the
  user's "比以前好很多了". The release build is older code from `main`.
- Remaining gap vs Explorer: the WinUI 3 window moves at ≈ 120/s on a 240 Hz panel. App code on the move path is
  negligible (`AppWindow.Changed` flag check, two subclass procs: MainWindow WndProc and `ListaryWindowBridge`, neither
  does work per move). Most likely framework/composition level (WinUI 3 window + DComp sync), not app logic — unproven.
- To prove it: ETW trace of the UI thread during a drag (WPR/WPA, needs elevation) or a minimal WinUI 3 sample window
  dragged on the same machine. Hybrid-GPU preference test is a user-side Windows setting.

# Minimal WinUI 3 comparison — 2026-11-06 (test app `D:\codex\...\window-drag-stutter-20261106\winui-test`, not in repo)

Unpackaged, Release, self-contained, same `Microsoft.WindowsAppSDK 1.8.260209005` as OrielFolder; launched outside the
Claude container (WMI). Each window shows one TextBlock. Recordings `winui-compare` (round 1) and `winui-compare2` (round 2).

| Window | Title bar | Mica | Window moves/s |
|---|---|---|---|
| A plain (round 1 / round 2) | system | no | **396 / 350** |
| D | system | yes | **266** |
| C | `Window.ExtendsContentIntoTitleBar` + `SetTitleBar(Border)` | no | **111** |
| E | `AppWindow.TitleBar.ExtendsContentIntoTitleBar` (Tall) | yes | **123** |
| B (round 1) | `Window.ExtendsContentIntoTitleBar` + `SetTitleBar` | yes | **135** |
| OrielFolder Dev (round 1; earlier runs 111–122) | extended (custom regions) | yes | 85 |
| Explorer / Claude (reference) | — | — | 268 / 293 |

## Confirmed
- The ~120 moves/s cap is caused by **extending content into the title bar** (both the Window and the AppWindow API),
  not by Mica, not by OrielFolder code. An empty WinUI 3 window with an extended title bar is capped the same way.
- Mica costs a little (350 → 266) but stays above the panel's 240 Hz-ish needs; not the stutter.
- Dev's extra drop (85–122 vs 111–135 for the empty window) = normal app load on top of the cap.

## Fix options (not tried)
1. Start the move ourselves: on PointerPressed in the title-bar area call `ReleaseCapture` +
   `SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)` so the system move loop runs on the main HWND (classic Win32
   trick) — needs a test variant F first to see if it escapes the cap.
2. System title bar — removes OrielFolder's tab/title-bar design; not acceptable without the user.
3. Accept the framework limit.

# Workaround test — round 3 (`winui-compare3`, test app source saved as `App.xaml.cs`; rounds 1/2 as `App.round*.cs.txt`)

G = Mica + `Window.ExtendsContentIntoTitleBar`, **no** WinUI caption region (`SetTitleBar(null)` +
`InputNonClientPointerSource.ClearRegionRects(Caption)`); PointerPressed on the bar → `ReleaseCapture` +
`SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION)` (system move loop on the top-level HWND).

| Window | Moves/s | Lag behind cursor p50 / p90 / p99 |
|---|---|---|
| A plain | **453** | 2 / 4 / 7 px |
| B like OrielFolder | **150** | 6 / 15 / 25 px |
| G manual move | **139** | 7 / 18 / 41 px |

**Result: the workaround does not help.** The cap follows the extended-title-bar window itself, not WinUI's caption
drag handling. Any WinUI 3 window with content extended into the title bar moves at ~110–150/s on this machine.
Remaining options: system title bar (design change, user decision) or accept. No change to OrielFolder.

# Other window hosts — rounds 4/5 (`winui-compare4`, `winui-compare5`; sources `App.round4.cs.txt`, `App.xaml.cs`)

Same WinUI 3 content (Windows App SDK 1.8), different outer window:

| Window | Outer window / drag | Moves/s | Lag p50 / p99 |
|---|---|---|---|
| A plain WinUI `Window` (rounds 4/5) | system caption | 514 / 441 | 2–3 / 12–49 px |
| J own Win32 window + `DesktopWindowXamlSource` | system caption | 493 / 474 | 2–3 / 8–12 px |
| **I2 own Win32 window, caption removed (`WM_NCCALCSIZE`), top 48 px strip returns `HTCAPTION` from `WM_NCHITTEST`, WinUI content in `DesktopWindowXamlSource` below** | custom strip, system drag | **466** | **2 / 9 px** |
| H WinUI `Window`, `SetBorderAndTitleBar(true,false)`, manual `WM_NCLBUTTONDOWN` | — | not measurable: drag barely worked | |
| I (round 4) Win32 custom frame + manual move from XAML | — | not measurable: drag barely worked | |

## Answer to "can another approach avoid the cap?"
- **Yes, without leaving WinUI 3 for the content:** hosting the WinUI 3 UI in our own Win32 window
  (`DesktopWindowXamlSource`) with a Win32-owned custom title area moves as fast as a plain window (≈ 470/s vs ≈ 120–150/s).
  The cap is specific to WinUI's `Window` with extended title bar.
- Starting the move from XAML pointer events (`ReleaseCapture` + `WM_NCLBUTTONDOWN`) is unreliable/ineffective; the drag
  area must be answered by the Win32 window's `WM_NCHITTEST`.

## Open for OrielFolder (not tested)
- OrielFolder puts XAML tabs in the title area. In I2 the drag strip is Win32-owned (no XAML on it). Needed: island
  covering the title area with drag gaps between tabs (non-client regions on the child site bridge, or hit-test
  passthrough) — must be measured, it may bring the cap back.
- Mica on a custom Win32 host (`DWMWA_SYSTEMBACKDROP_TYPE` + transparent island background), `ContentDialog`/`XamlRoot`,
  multi-window, tray, drag & drop, `AppWindow` usage in MainWindow partials — migration cost not estimated yet.

# Tabs in the title area of a Win32 host — round 6 (`winui-compare6`, source `winui-test\src\App.xaml.cs`, analysis `rate.py`)

Own Win32 window (caption removed via `WM_NCCALCSIZE`), one `DesktopWindowXamlSource` covering the whole client
(`ResizeContentToParentWindow`), 48 DIP title row with 4 XAML buttons as "tabs", empty part of the row must drag.

| Window | How the drag gap is defined | Result |
|---|---|---|
| **T2** | `InputNonClientPointerSource.GetForWindowId(host).SetRegionRects(Caption, gap)` (updated on LayoutUpdated) | **443 moves/s**, lag p50 2 / p99 10 px — same as plain window A (443/s) |
| T1 | island child windows return `HTTRANSPARENT` in the gap, host returns `HTCAPTION` | **does not drag** (user report; 5.4 s held, no movement). Hit-test probe (`hittest-probe.ps1`) showed the expected -1/2 answers, so WinUI input ignores classic HTTRANSPARENT routing |

## Conclusion
- The cap is specific to WinUI's own `Window` (`Microsoft.UI.Xaml.Window`) with content in the title bar. With our own
  Win32 window hosting the same WinUI 3 content, **tabs in the title bar + WinUI caption regions drag at full speed**.
- Viable direction for OrielFolder: replace the `Window` shell of MainWindow with a Win32 host + `DesktopWindowXamlSource`,
  keep the XAML, keep `InputNonClientPointerSource` regions (OrielFolder already sets caption regions explicitly in
  `QueueUpdateTitleBarRegions`). User confirmed (2026-11-06): tab buttons in T2 are clickable while the gaps drag. Not yet checked: caption buttons (min/max/close
  must be drawn by us or via regions), Mica on the host (`DWMWA_SYSTEMBACKDROP_TYPE`), snap layouts, `Window`-only APIs
  used by MainWindow partials (Activated, Closed, AppWindow, SystemBackdrop, XamlRoot users, multi-window, tray).

# Keep `Window`, remove the caption ourselves — round 7 (`winui-compare7`, source `App.round7.cs.txt`)

W = `Microsoft.UI.Xaml.Window` **without** `ExtendsContentIntoTitleBar`; top-level HWND subclassed, `WM_NCCALCSIZE`
keeps the original top (caption removed, borders kept); XAML title row with tabs; drag gap via
`InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(Caption, …)`.

| Window | Moves/s | Lag p50 / p99 |
|---|---|---|
| **W** | **419** | 4 / 20 px |
| T2 (Win32 host) | 438 | 3 / 14 px |

→ The cheap path works: OrielFolder can keep `MainWindow : Window` (no host rewrite). The cap comes from WinUI's
extended-title-bar mode itself, not from `Window` or from `InputNonClientPointerSource`.
Consequence: system caption buttons disappear (OrielFolder uses `AppWindow.TitleBar.RightInset` and
`ButtonForegroundColor…` in `MainWindow.TabManager.cs:1520,1531,1605` and `MainWindow.SettingsHandler.cs:83`) → need own
caption buttons. Round 8 (W2) tests own buttons as Minimize/Maximize/Close non-client regions.

# Own caption buttons — rounds 8/9 (`winui-compare8`, sources `App.round8.cs.txt` (W2), `App.xaml.cs` (W3), trace `winui-test\bin\trace.txt`)

W2 = W + XAML min/max/close buttons registered as `NonClientRegionKind.Minimize/Maximize/Close`, hover visuals from
`InputNonClientPointerSource.PointerEntered/Exited`, `WM_NCCALCSIZE` adds the border inset when maximized.
W3 = W2 + top-level subclass returns `HTMAXBUTTON` from `WM_NCHITTEST` over the maximize button.

| Check | W2 | W3 |
|---|---|---|
| Drag speed | **395 moves/s** (lag p50 2 / p99 13 px) | (unchanged code path) |
| Min / max / restore / double-click maximize / close | work (user) | work |
| Maximized window fully on screen | yes (user) | yes |
| Hover highlight min/max | yes | yes |
| Close turns red on hover | **no** (user) | **no** — `PointerEntered Close` event does fire (trace); visual-only bug, fix in implementation |
| **Win11 snap layouts on maximize hover** | **no** | **yes** (user) — needs the top-level `WM_NCHITTEST` → `HTMAXBUTTON` |

→ Full recipe for OrielFolder (all user-verified in the test app except close-red): keep `MainWindow : Window`,
stop using `ExtendsContentIntoTitleBar`, subclass `WM_NCCALCSIZE` (+ maximized inset) and `WM_NCHITTEST` (HTMAXBUTTON),
own caption buttons + `InputNonClientPointerSource` Caption/Passthrough/Minimize/Maximize/Close regions.

## After the fix — OrielFolder Dev with own frame (2026-11-06 20:21, `D:\codex\Orielfolder-dev\window-frame-20261106\runtime-2`)

| Window | moves/s | held | interval p50 / p99 | freezes | lag px p50 / p90 / p99 |
|---|---|---|---|---|---|
| OrielFolder main window | 461 | 26.8 s | 2.0 / 5.5 ms | 1 | 3 / 12 / 34 |
| OrielFolder Quick Look | 460 | 15.5 s | 2.1 / 4.3 ms | 0 | 6 / 13 / 26 |
| Explorer (same session) | 426 | 17.1 s | 2.1 / 6.1 ms | 1 | 7 / 16 / 64 |

Before the fix the Dev main window ran at 85–122 moves/s. The cap is gone; OrielFolder now matches Explorer.
