# Implement plan — Dev uninstall option

Branch `rename/orielfolder` (dev-only). No release identity / packaging / `main` changes.

## Ordered checklist

1. **Pure logic + unit tests (TDD)**
   - `Services/DevUninstall/StaleFolderOpenRule.cs` — rule from R5. Tests mirror `tests/test-stale-folder-open.ps1` cases
     plus: existing exe → not stale; quoted path with spaces; case-insensitive; missing command → no-op.
   - `Services/DevUninstall/DevUninstallPlan.cs` — base dir, backup/plan/result paths, delete/keep lists, allow-list validation
     (rejects `marked-files`, `backups`, drive roots, paths not ending in an allowed suffix).
   - Add both files to `OrielFolder.Tests.csproj` `<Compile Include>`; tests in `OrielFolder.Tests/Services/DevUninstall*Tests.cs`.
   - Gate: `dotnet test` (restore + `--no-restore`) green.
2. **Helper script** `Assets/DevUninstall/Uninstall-OrielFolderDev.ps1` + `Content` include in `OrielFolder.csproj`.
   - Params: `-Plan <json>`; optional `-ClassesRoot` (default `HKCU:\Software\Classes`) for tests.
   - Same stale rule as C#; `reg export` before delete; allow-list re-check; SID check; result JSON; no package removal.
   - `tests/test-dev-uninstall-script.ps1`: dot-source functions, test the rule + allow-list without registry writes
     (pattern of `test-stale-folder-open.ps1`); Windows PowerShell 5.1 compatible (no modern C# `Add-Type`).
3. **Service** `Services/DevUninstall/DevUninstallService.cs` — backup, plan JSON, elevated start, result parsing,
   `RemovePackageAsync` + fallback. Logs via `DebugLogger` with `[DevUninstall]` prefix.
4. **UI** — Advanced section last card in `SettingsModeView.xaml` (red button, existing card styles), partial
   `SettingsModeView.DevUninstall.cs` (visibility check, ContentDialog with CheckBox, progress, results).
5. **Localization** — new keys in `LocalizationData.cs` for 9 languages; XAML default text like other cards.
6. **Quality check** — `build-dev.ps1` (Dev app will be closed; ask first if it is running), unit tests, new static script
   test, re-run `test-stale-folder-open.ps1`, review diff.
7. **Runtime verification (needs user OK at that time, it really uninstalls Dev):**
   a. `start-dev.ps1`; open Settings → Advanced; confirm button visible; Cancel path → nothing changed.
   b. With user approval, plant a disposable stale override (exported first) — or skip if the user prefers — run
      uninstall; check AC list in prd.md (`AssocQueryString`, folders, backup JSON, `Get-AppxPackage`).
   c. `start-dev.ps1` again → app registers and starts; import the backup → settings/workspaces restored.
8. **Spec update** — `spec/project/build-test-verification.md` (new test), `rename-migration-status.md` not affected;
   add a short note to `backend/` or `project/architecture.md` about MSIX virtualisation + elevated helper pattern.
9. **Commit** on `rename/orielfolder` only when the user asks.

## Technical unknowns (resolve in step 3/7, do not change MVP behaviour)

- Whether `PackageManager.RemovePackageAsync` on its own package works from a packaged full-trust process without
  extra capability → fallback already designed.
- Whether `Environment.GetFolderPath(LocalApplicationData)` returns the real path (yes, path string is real; only file
  I/O is redirected) — the helper runs outside the container, so it acts on the real folder.

## Rollback points

- Each step is in separate files except `SettingsModeView.xaml`, `LocalizationData.cs`, csproj includes → revert by
  removing the card/keys/includes. Registry changes during testing are backed up as `.reg` under `D:\codex`.
