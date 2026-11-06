# Dev uninstall option with system-integration cleanup

## Goal

Uninstalling OrielFolder **Dev** must never leave Windows broken (e.g. folders failing to open with
"找不到应用程序"). Add an uninstall option inside the Dev build. Dev-only (see
`.trellis/spec/project/dev-release-boundary.md`): nothing in the release identity, packaging or `main`.

## Trigger / incident (2026-11-06)

After an uninstall, clicking any folder in Explorer showed "找不到应用程序".
- Cause (verified read-only): `HKCU\Software\Classes\{Folder,Directory,Drive}\shell\open\command` =
  `"%LOCALAPPDATA%\Microsoft\WindowsApps\spanfinder.exe" "%1"` (upstream **SPAN Finder** alias), alias file gone.
  MSIX packages get no uninstall hook, so the override survived the uninstall.
- Repaired at the user's request ("修复这个问题") with the existing `tools/Repair-StaleFolderOpen.ps1 -Apply`
  (its test passed first). Backups: `D:\codex\Orielfolder-dev\folder-open-fix-20261106\registry-backup\{Folder,Directory,Drive}-open.reg`.
  Only the three exact stale `open` keys were removed; other HKCU verbs (e.g. `Directory\shell\WeTypeFileTransfer`) kept.
  Verified: `AssocQueryString(Folder, open)` → `C:\WINDOWS\Explorer.exe`.
  **Correction (2026-11-06 15:00):** that repair and check ran inside the Claude desktop app's MSIX container and only
  changed its virtual HKCU view. The real overrides were removed by the Dev uninstall helper during the runtime test
  (backups `D:\codex\OrielFolderDev\backups\uninstall-20261106-145141\registry\`), verified from outside the container.

## Confirmed facts (repository + machine)

- Dev and release both have `AppIdentity.AllowsDefaultAssociations = false` → OrielFolder never writes the folder-open
  override itself; the bad override came from the upstream SPAN app.
- Dev's own system footprint:
  - MSIX package `LocalDevelopment.OrielFolderDev` (+ alias `orielfolder-dev.exe`, packaged LocalState: settings, logs).
  - `%LOCALAPPDATA%\OrielFolderDev` (`AppIdentity.LocalDataRoot`: thumb cache, crash dumps, worker logs, connections),
    `D:\codex\OrielFolderDev` (preview-cache, marked-files) when `D:\codex` exists.
  - WER LocalDumps keys `HKCU\...\Windows Error Reporting\LocalDumps\<exe>` via `WerHelper.EnsureRegistered`
    (currently present on this machine only for legacy `SpanDev.exe` / `SpanDev.Thumbs.exe`).
  - Listary plugin folder `<Listary>\FileAppPlugins\OrielFolderDev` when `connect-listary.ps1` was used.
- Existing building blocks: `DefaultFileManagerService.FindLeftoverOpenKeys/RemoveLeftoverOpenKeysAsync` (disabled in Dev
  by `CanChangeDefaultAssociations`), `tools/Repair-StaleFolderOpen.ps1` (exact-match stale SPAN override repair with backup).
- Data location (verified on disk): MSIX virtualises `%LOCALAPPDATA%` writes, so settings (package `Settings`),
  `workspaces.json`, `action_log.json`, folder tags, connections, crash dumps, WebView2 (`EBWebView`) and logs live in
  `Packages\LocalDevelopment.OrielFolderDev_tvznpbczqragy\{LocalState,LocalCache}` → **Windows deletes them automatically
  when the package is removed**; they cannot be "kept" by package removal.
  Outside the package (survive removal): real `%LOCALAPPDATA%\OrielFolderDev` (Logs, ThumbCache) and `D:\codex\OrielFolderDev`
  (`preview-cache`, `marked-files` = annotated files the user exported).
- `ConfigurationBackupService.Export` already produces an "OrielFolder Configuration" JSON (settings + workspaces).
- Also registered on this machine: legacy `LocalDevelopment.SpanDev` and the release `OrielFolder.Desktop` — never touched by Dev.

## Decisions

- D1 (2026-11-06, user: "Clean up and remove app"): the option (1) removes Dev's own system footprint, (2) repairs
  folder-open overrides that point to a missing executable (the incident above), then (3) removes the Dev package
  (the app closes).
- D2 (2026-11-06, user: "Export, then clean caches"): before removal, offer a configuration backup export
  (checked by default); delete Dev data outside the package (`%LOCALAPPDATA%\OrielFolderDev`, `D:\codex\OrielFolderDev\preview-cache`);
  **keep** `D:\codex\OrielFolderDev\marked-files` (user-created files). In-package data is removed by Windows.

- D3 (2026-11-06, user: "Use recommendation"): red "卸载 OrielFolder Dev" button at the bottom of Settings -> Advanced;
  confirmation dialog lists what happens and what is kept, checkbox "先导出配置备份" checked by default, default focus Cancel;
  backup saved without a picker to `D:\codex\OrielFolderDev\backups\OrielFolder-config-<yyyyMMdd-HHmmss>.json`
  (no `D:\codex` → `Documents\OrielFolderDev\backups\`, i.e. next to `MarkedOutputRoot`); any failure stops before
  package removal and reports the reason; package removal is the last step.

## Requirements

- R1 One uninstall action in the Dev build performs D1 steps in order; package removal happens last, only after
  every earlier step succeeded.
- R2 Visible only when the running package is `LocalDevelopment.OrielFolderDev` (runtime identity check), so the
  option can never appear in a release build even if the source is shared.
- R3 Confirmation dialog (D3) states: what will be removed, what is kept (`marked-files`, backup), that Windows will
  ask for administrator approval once (UAC, see design), and that the app will close. Default focus = Cancel.
- R4 Backup (D3/D2): when checked, export settings + workspaces with the existing `ConfigurationBackupService` to the
  backups folder before any destructive step; failure → stop.
- R5 Stale folder-open repair (D1-2): for `Folder`, `Directory`, `Drive` under `HKCU\Software\Classes\<class>\shell\open`,
  remove the `command` override only when it is exactly `"<path>" "%1"`, `<path>` does not exist, `DelegateExecute` is
  empty/absent, no other values and no subkeys (generalises `tools/Repair-StaleFolderOpen.ps1`); remove the then-empty
  `open` key; export each key to a `.reg` backup first; never touch other verbs or HKLM.
- R6 Delete out-of-package Dev data (D2): `%LOCALAPPDATA%\OrielFolderDev`, `<MarkedOutputRoot parent>\preview-cache`;
  never delete `marked-files` or `backups`.
- R7 Remove the Listary plugin folder `FileAppPlugins\OrielFolderDev` if present (Dev footprint from `connect-listary.ps1`).
- R8 Any failure (UAC cancelled, locked file, backup error) stops before package removal, shows which step failed, and
  leaves the app running. Cleanup steps are idempotent so the user can retry.
- R9 After cleanup succeeds, show the backup path, then remove the package `LocalDevelopment.OrielFolderDev` (current user).
- R10 All new UI text in the 9 existing languages (LocalizationData pattern).

## Acceptance Criteria

- [x] Button appears at the bottom of Settings -> Advanced in the Dev build only (R2).
- [ ] Cancel in the dialog or in UAC changes nothing (registry, files, package) (R8).
- [x] (real, not planted: 2026-11-06 runtime test) With a stale `spanfinder.exe`-style override, uninstall removes it, writes `.reg` backups, and
      `AssocQueryString(Folder, open)` returns Explorer afterwards (R5).
- [ ] Overrides that point to an existing exe, have a DelegateExecute, extra values or subkeys are left untouched (R5).
- [ ] Backup JSON exists in the backups folder and imports with the existing Import feature (R4).
- [x] `%LOCALAPPDATA%\OrielFolderDev` and `preview-cache` are gone; `marked-files` and `backups` remain (R6).
- [x] `Get-AppxPackage LocalDevelopment.OrielFolderDev` returns nothing after success; `start-dev.ps1` can register it again.
- [x] Unit tests for the detection/plan logic and a static test for the helper script pass; `build-dev.ps1` 0 errors.

## Open Questions (user-owned)

1. ~~Scope~~ -> D1.  2. ~~User data~~ -> D2.  3. ~~Placement/UX~~ -> D3.  (none open)

## Out of Scope

- Release identity / packaging / installer / `main` (dev-only rule).
- Removing other packages (`LocalDevelopment.SpanDev`, `OrielFolder.Desktop`).
