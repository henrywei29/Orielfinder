# Implement plan — own title-bar frame (Dev only)

Branch: a non-`main` branch (see "Before start"). No release identity / packaging / `main` changes.
Backups of every pre-edit file: `D:\codex\Orielfolder-dev\window-frame-20261106\backup\`.

## Before start

- Git: the Dev-uninstall task (`11-06-dev-uninstall-option`) is still uncommitted on `rename/orielfolder`. Ask the user
  whether to commit it first so the two changes stay separable. Do not commit without approval.
- `task.py start`; read `spec/frontend/layout-window-performance.md`, `spec/project/build-test-verification.md`.

## Ordered checklist

1. **Pure geometry + unit tests (TDD)** — `Helpers/WindowFrameGeometry.cs`; tests in
   `OrielFolder.Tests/Helpers/WindowFrameGeometryTests.cs` (normal / maximized inset / no-caption style untouched /
   hit test edges); add `<Compile Include>` to `OrielFolder.Tests.csproj`. Gate: `dotnet restore --source` + `dotnet test --no-restore`.
2. **`CustomWindowFrame`** — subclass (`WM_NCCALCSIZE`, `WM_NCHITTEST`→`HTMAXBUTTON`, `WM_NCDESTROY`), `SWP_FRAMECHANGED`.
3. **`CaptionButtons` control** — XAML + code-behind, region-kind hover/pressed visuals, close red via `Border`,
   palette setter, `ShowMinimize`/`ShowMaximize`, max/restore glyph.
4. **MainWindow** — anchors in design.md: frame attach, XAML column, `UpdateTitleBarRegions` /
   `ExpandTitleBarPassthrough` / teardown, caption colors, F11.
5. **Quick Look** — frame attach, close-only buttons, explicit Caption/Close regions, remove fixed padding.
6. **Tests** — update `tests/test-titlebar-explicit-regions.ps1` to the new geometry (caption minus caption-buttons width,
   Minimize/Maximize/Close writes) and rewrite its fixture in C# 5 syntax so it runs on Windows PowerShell 5.1; add a
   static check that neither window sets `ExtendsContentIntoTitleBar` or calls `SetTitleBar(`.
7. **Quality check** — `build-dev.ps1` (closes Dev; tell the user first), unit tests, static tests, diff review.
8. **Runtime verification (user + sampler)** — `start-dev.ps1`; drag sampler
   (`D:\codex\Orielfolder-dev\window-drag-stutter-20261106\drag-sampler.ps1`, analysis `rate.py`) on the main window and
   Quick Look; user checklist = acceptance criteria in prd.md (tabs, tear-off, caption buttons, snap layouts, maximized,
   F11, themes incl. a custom theme, DPI/monitor move, close-to-tray, multi-window).
9. **Spec update** — `spec/frontend/layout-window-performance.md` title-bar section (no extended mode; own frame;
   five region kinds; HTMAXBUTTON override allowed because `SetTitleBar` is not used), `spec/project/architecture.md` note.
10. **Handoff + journal**; commit only when the user asks.

## Validation commands

- Unit: `.tools` dotnet `restore --source <nuget-packages>` then `dotnet test src\OrielFolder\OrielFolder.Tests\OrielFolder.Tests.csproj -p:Platform=x64 --no-restore`
- Static: `powershell -File tests\test-titlebar-explicit-regions.ps1` (after step 6 runs on 5.1)
- Build: `powershell -NoProfile -ExecutionPolicy Bypass -File build-dev.ps1`
- Runtime: `start-dev.ps1`, sampler + `rate.py`

## Risky files / rollback points

- `MainWindow.TabManager.cs` region code (tested by `test-titlebar-explicit-regions.ps1`), `MainWindow.xaml` title row,
  `QuickLookWindow.xaml(.cs)`. Each step is revertible from the backups; new files can be deleted.

## Technical unknowns (verify in step 8, MVP unchanged)

- Mica together with the removed caption (prototype W had no Mica; Mica alone cost ~25 % move rate in round 2 but stayed > 260/s).
- Region alignment when the sidebar changes the title-bar origin, and after DPI change.
- Inactive-window caption colors (system used `ButtonInactiveForegroundColor`); implement via window `Activated` state.
