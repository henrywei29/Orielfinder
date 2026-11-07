# Handoff — 11-07-folder-open-perf

## Current State
- **TASK_ID:** 11-07-folder-open-perf
- **Updated:** 2026-11-07 (local)
- **Agent:** Claude Code
- **Task & phase:** Implemented and measured.
  - Part 1 (startup) is committed in `7ac7541`.
  - Part 2 (GC ballast, row template x:Bind, density style guard, frame watch) is committed in `27e72ca` on `rename/orielfolder`, together with the zip task. Completed and archived.
  - Not pushed. Dev only.
- **Git:** Committed together with `11-07-archive-open-extract` in `27e72ca`. `.agent/`, `.codex/` and `agent-handoffs/` hold other people's uncommitted files and are not part of this task.

### Root causes (measured, Debug Dev)
1. **Late window on cold start.** The window was uncloaked from a Low-priority callback. With a folder argument, that queue was busy loading and realizing rows, so the window appeared 1–2 s after the content was ready.
2. **Startup work on the UI thread.** The hidden `SettingsModeView` was built at startup (~250 ms). Quick Access was read synchronously on the UI thread (~400 ms).
3. **Induced GCs on new tabs.**
   - WinUI reports each XAML element's native memory through `GC.AddMemoryPressure`. This induces Gen2 GCs, 1–2 per frame while rows realize.
   - Each Gen2 walks every live XAML object, hidden tabs included. So the pause per Gen2 grows with the number of tabs: 22 ms at 1 System32 tab, 141 ms at 6.
4. **Rows built twice on cold start.** `ApplyDensity` assigned a new `ItemContainerStyle` again after the first rows appeared. WinUI then discarded and rebuilt every realized row.

### What changed

**Committed in `7ac7541`:**
- `Helpers/StartupRevealGate.cs`: uncloak the window on the 2nd `Rendering` event.
- `SettingsView x:Load=False` plus `EnsureSettingsView()`.
- Favorites snapshot, and Quick Access read in the background (`Helpers/FavoritesSnapshot.cs`).
- `Helpers/PerfTrace.cs`: the [Perf] log.

**Committed in `27e72ca`:**
- `Helpers/GcBallast.cs` + `Program.cs`:
  - Allocate an untouched array: 512 MB with 8 GB of RAM or more, 256 MB with 4 GB or more.
  - This raises the threshold at which memory pressure triggers a GC.
  - The array's pages are never touched, so it does not add to the working set.
- `Views/DetailsModeView.xaml`:
  - Row-template font sizes changed from `{Binding …, Source={StaticResource FontScale}}` to `{x:Bind helpers:FontScaleService.Instance.*, Mode=OneWay}`.
  - Tag dot gets a constant `x:Load="False"`.
- `Views/DetailsModeView.xaml.cs`, `ListModeView.xaml.cs`, `IconModeView.xaml.cs`:
  - Replace `ItemContainerStyle` only when the row height changes (for Icon, the spacing).
  - In Details, write the header and row heights only when they change.
- `Helpers/PerfTrace.cs` + `MainWindow.xaml.cs`:
  - DEBUG-only `WatchFrames`. It logs frame gaps, GC pause, Gen2 count and rows realized.
  - It runs for 2.4 s after a redirected folder and for 3 s after uncloak.
- Tests:
  - `GcBallastTests`.
  - `DetailsRowTemplateTests`: no `{Binding` in the row template, font sizes are OneWay, no data-driven `x:Load`.
  - The csproj links `GcBallast.cs`.
- Spec: `.trellis/spec/frontend/layout-window-performance.md` now covers GC, the ballast, x:Bind, the style rebuild and x:Load.

### Verification (Debug Dev)

| Scenario | Before | After |
|---|---|---|
| Cold start, window appears, no args / Downloads / System32 (median) | 2.99 / 3.56 / 4.52 s | 2.40 / 2.82 / 2.35 s |
| 6 System32 tabs: total stall | 12.4–13.4 s | 5.0–5.2 s |
| 6 tabs: GC pause / worst frame | 6.7–7.2 s / 614–799 ms | 2.1–2.3 s / ~300 ms |
| 6 tabs: working set | 525 MB | ~610 MB |
| Cold start System32: rows created / stall in first 3 s | 208 / 849 ms | 159 / 454 ms |

- Each change alone, at 6 tabs:
  - Ballast alone: 6.0–6.5 s total stall.
  - x:Bind font sizes alone: no effect.
- Checked in the app:
  - Font-scale live update works: row height 28 → 33 → 28.
  - Git M/? and tag dots are still visible.
- `build-dev.ps1` builds with 0 errors.
- Unit tests: 1215 passed, 0 failed, 3 skipped; 1235 passed with the zip task.
- Dev was restarted with `start-dev.ps1`.

### Files outside the repo
- `D:\codex\perf-open\` holds:
  - `measure-startup.ps1`, `measure-redirect.ps1`, `tabsum.py`, `coldwin.py`, `rowpasses.py` and `set-gc.ps1`.
  - The UIA helpers and the `results-*.txt` files.
  - `folderload-harness`, which runs the assertions of `test-folder-loading-performance.ps1` without PowerShell 7.

### Failed approaches (do not retry)
- **Server GC or non-concurrent GC:** no effect. The XAML reference walk is single-threaded.
- **Data-driven `x:Load="{x:Bind …}"` in row templates:**
  - Ceiling test: big win, per-Gen2 pause 141 → 42 ms.
  - Broken: a new or recycled row bound to an item whose condition is already true never creates the element. The Git M/? marks disappeared.
- **No visible gain, reverted:**
  - Merging the two UI-thread round trips in `EnsureChildrenLoadedAsync`.
  - Sending one Reset instead of per-item `VisibleRows` notifications.
  - Removing the XAML `ItemsSource` binding.
  - Changing the font (Noto Sans SC vs Segoe).
- **Not done (the user decides, because both have trade-offs):**
  - Set the Details `CacheLength` to 1 and release hidden-tab panels.
  - Measured gain: per-Gen2 pause 138 → 45 ms.
  - Cost: fast scrolling is slower, and switching back to an old tab rebuilds it.

### Incident
- On 2026-11-07, re-registering Dev from the Release layout ran `Remove-AppxPackage`. This wiped the Dev settings and LocalState.
- Debug was re-registered afterwards.
- Backup to import from Settings: `C:\Users\whq\Downloads\SPAN Dev-config-20261104-214805.json`.

### Next step
None. Task completed and archived.

## Checkpoint Log
- 2026-11-07 [Claude Code] Startup bottlenecks measured and fixed; committed `7ac7541`.
- 2026-11-07 [Claude Code] Found the new-tab stall cause: Gen2 GCs induced by AddMemoryPressure walk all XAML objects.
- 2026-11-07 [Claude Code] Kept GC ballast + x:Bind font sizes. Rejected server/non-concurrent GC and data-driven x:Load.
- 2026-11-07 [Claude Code] Density style guard removes the duplicate row build on cold start. Tests: 1215 passed, 0 failed, 3 skipped.
- 2026-11-07 [Claude Code] Zip flow re-checked in Dev with 7 screenshots for the user (`D:\codex\zip-demo-run`); user approved; committed `27e72ca` (perf part 2 + zip together); archived.
