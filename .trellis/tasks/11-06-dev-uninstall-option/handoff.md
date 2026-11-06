# Handoff — 11-06-dev-uninstall-option

## Current State
- **TASK_ID:** 11-06-dev-uninstall-option   - **Updated:** 2026-11-06 15:00 (local)
- **Agent:** Claude Code (Trellis inline flow: `trellis-implement`/`trellis-check` agent types were not loadable in this session)
- **Task & phase:** Dev uninstall option. Phase 2 implemented, checked and **runtime-verified** (level 4). Remaining:
  user imports the backup in the re-registered Dev app (level 5), commit, Phase 3 finish/archive — all need the user.
- **Git:** branch `rename/orielfolder`, HEAD 343da11, nothing staged/committed for this task.

### Completed work
- Planning: prd.md (D1-D3, R1-R10, acceptance), design.md, implement.md; user approved ("Approve plan").
- Implementation (steps 1-6 of implement.md) and runtime verification (step 7).
- Machine repair: the stale `spanfinder.exe` folder-open overrides in the **real** HKCU were removed by the Dev uninstall
  helper on 2026-11-06 14:51 (backups `D:\codex\OrielFolderDev\backups\uninstall-20261106-145141\registry\*.reg`).
  The earlier repair with `tools/Repair-StaleFolderOpen.ps1` (backup `D:\codex\Orielfolder-dev\folder-open-fix-20261106`)
  ran inside the Claude desktop app's MSIX container and only changed that container's virtual registry view — see
  Failed approaches.

### Files modified by this task
Created:
- `src/OrielFolder/OrielFolder/Services/DevUninstall/{StaleFolderOpenRule,DevUninstallPlan,DevUninstallService}.cs`
- `src/OrielFolder/OrielFolder/Assets/DevUninstall/Uninstall-OrielFolderDev.ps1` (elevated helper)
- `src/OrielFolder/OrielFolder/Views/SettingsModeView.DevUninstall.cs` (UI flow)
- `src/OrielFolder/OrielFolder.Tests/Services/DevUninstallTests.cs` (17 tests)
- `tests/test-dev-uninstall-script.ps1` (31 checks, PS 5.1)
Modified:
- `Views/SettingsModeView.xaml` (card at end of Advanced), `Views/SettingsModeView.xaml.cs` (visibility + localization),
  `Services/LocalizationData.cs` + `LocalizationPolish.cs` (16 `DevUninstall_*` keys x 10 languages),
  `OrielFolder.csproj` (Content include), `OrielFolder.Tests.csproj` (2 Compile includes).
- Spec: `spec/project/architecture.md` (MSIX virtualisation section), `spec/project/build-test-verification.md`.
Backups of pre-edit files: `D:\codex\Orielfolder-dev\dev-uninstall-20261106\backup\`.

### Tests actually executed
- `dotnet test OrielFolder.Tests` (restore + --no-restore) -> 1176 passed, 0 failed, 3 skipped (+17 new).
- `tests/test-dev-uninstall-script.ps1` -> 31 PASS; `tests/test-stale-folder-open.ps1` -> 7 PASS.
- `build-dev.ps1` on the real tree -> exit 0, 0 errors, 178 warnings (unchanged). Helper present in `bin\...\Assets\DevUninstall\`.
- Runtime (2026-11-06, user clicked, Claude checked; evidence in `D:\codex\Orielfolder-dev\dev-uninstall-20261106\runtime\`):
  - `start-dev.ps1` -> Dev PID 27360; user ran Cancel, then the full flow incl. UAC and "移除应用".
  - Backup `D:\codex\OrielFolderDev\backups\OrielFolder-config-20261106-145141.json` ("OrielFolder Configuration" v1).
  - Helper `result.json`: Ok=true; folder-open Folder/Directory/Drive **done** (real stale spanfinder overrides, .reg
    backups written); listary-plugin skipped (not installed); `%LOCALAPPDATA%\OrielFolderDev` skipped (not present in the
    real profile); `D:\codex\OrielFolderDev\preview-cache` done.
  - Checked from a process outside any container (WMI `Win32_Process.Create`, `outside-check.json`): the three
    `HKCU\Software\Classes\*\shell\open` keys absent, `AssocQueryString(Folder, open)` = `C:\WINDOWS\Explorer.exe`.
  - AppX deployment log: Remove of `LocalDevelopment.OrielFolderDev_1.0.1.0_x64__tvznpbczqragy` started 14:51:54, succeeded
    14:52:28 → `PackageManager.RemovePackageAsync` on its own package works (fallback not needed). Package data folder gone.
  - `SpanDev` and release `OrielFolder.Desktop` still registered, untouched. `D:\codex\OrielFolderDev` keeps `backups`
    (`marked-files` did not exist before the test).
  - `start-dev.ps1` again -> re-registered, Dev PID 18284.
- NOT verified: Cancel path "nothing changed" was not snapshotted separately (user report only); backup import in the
  re-registered app (user step); acceptance "overrides with existing exe / DelegateExecute / extra values untouched"
  covered by unit + static tests only.

### Failed approaches
- `$plan` as a test variable after dot-sourcing the helper: the helper's `param([string]$Plan)` type constraint turned the
  object into a string. Renamed to `$testPlan`/`$testLayout`.
- **Checking or changing HKCU / %LOCALAPPDATA% from Claude's shell:** the Claude desktop app is itself MSIX-packaged, so its
  child processes (Bash, PowerShell, dotnet test) see and write a virtualised HKCU/AppData. The earlier "repair + verified
  Explorer.exe" only changed Claude's private view; the real hive still had the spanfinder overrides until the helper ran.
  `%LOCALAPPDATA%\OrielFolderDev` (Logs, ThumbCache) seen from Claude's shell is in
  `Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\OrielFolderDev` (written by unit tests), not the real profile.
  Use a process outside the container (WMI `Win32_Process.Create`) for real-state checks.
- Screenshot of a "设置" window was the release OrielFolder (it shows "Span 调试日志"); both apps title the window "设置".

### Known risks
- Helper script lives in the user-writable dev layout (`bin\`) — accepted for Dev (design.md).
- Listary running with the plugin loaded -> uninstall stops and asks to quit Listary (by design). Not exercised (no plugin).

### Next step
User imports the backup in Dev (Settings → configuration backup → Import). Then, on request: commit on
`rename/orielfolder`, Phase 3 finish/archive.

### Needs user approval
1. Commit on `rename/orielfolder`.
2. Phase 3 finish + archive.

## Checkpoint Log
- 2026-11-06 [Claude Code] Diagnosed folder-open failure (HKCU override → missing spanfinder.exe), repaired with backup, verified. (Correction 15:00: repair and check ran inside Claude's MSIX container; real repair happened in the runtime test below.)
- 2026-11-06 [Claude Code] Created task 11-06-dev-uninstall-option (planning); PRD seeded with facts.
- 2026-11-06 [Claude Code] D1: user chose cleanup + repair stale folder-open + remove Dev package.
- 2026-11-06 [Claude Code] D2: export config backup (default on), delete out-of-package caches/logs, keep marked-files.
- 2026-11-06 [Claude Code] Asked Q3 (placement/UX + backup location). Existing export uses FileSavePicker (SettingsModeView.ConfigurationBackup.cs); Settings has an Advanced ('고급') section.
- 2026-11-06 [Claude Code] D3: user chose recommended placement/UX (Advanced section, dialog, auto-saved backup, stop on error).
- 2026-11-06 [Claude Code] design.md + implement.md written; PRD requirements R1-R10 + acceptance; context manifests curated. Waiting for user approval of planning summary (no task.py start).
- 2026-11-06 [Claude Code] Implemented steps 1-6; self-review found the elevated helper trusted user-writable plan paths -> hardened (derived layout, backup-root check). Tests: unit 1176/0/3, static 31+7 PASS, copy compile 0 errors. Runtime pending approval.
- 2026-11-06 [Claude Code] User approved runtime test ("Run it now"). build-dev exit 0; start-dev PID 27360; user ran the flow; helper Ok, real stale overrides removed with .reg backups, preview-cache deleted, package removed (deployment log 14:52:28); outside-container check OK; start-dev re-registered (PID 18284). Found Claude-shell virtualisation pitfall.
