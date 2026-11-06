# Design — Dev uninstall option

## Key constraint: MSIX virtualisation

OrielFolder Dev is a packaged full-trust app. Its writes to `HKCU` and `%LOCALAPPDATA%` are redirected into the
package (`DefaultFileManagerService.RemoveLeftoverOpenKeysAsync` documents this: "패키지 앱의 HKCU 쓰기는 가상화되므로
앱이 직접 지울 수 없다"). Consequences:

- Dev's own registry writes (WER LocalDumps) and AppData files already live inside the package → Windows removes them
  with the package. Nothing to clean for those.
- Anything in the **real** hive / real `%LOCALAPPDATA%` (stale `spanfinder.exe` override, real
  `%LOCALAPPDATA%\OrielFolderDev`) cannot be changed from inside the package. It must be done by a process outside the
  package container. The established pattern in this code base is an elevated process (`Verb = "runas"` → UAC prompt),
  which the AppInfo service creates outside the container.
- `D:\codex\...` and `Documents\...` are not virtualised; the app can write the backup there directly.

Decision: one elevated helper script does every real-system change in one UAC prompt. The app does UI, backup and
planning; the helper does registry + file cleanup; the app removes the package last.

## Components

| Piece | Location | Role |
|-------|----------|------|
| `DevUninstallPlan` (pure C#) | `Services/DevUninstall/DevUninstallPlan.cs` | Inputs: identity, paths. Outputs: backup dir, folders to delete, folders to keep, classes to inspect. No I/O → unit-testable |
| `StaleFolderOpenRule` (pure C#) | `Services/DevUninstall/StaleFolderOpenRule.cs` | `IsStale(command, delegateExecute, valueNames, subkeyNames, fileExists)` — same rule as `Test-StaleSpanCommand` but for any missing exe |
| `DevUninstallService` | `Services/DevUninstall/DevUninstallService.cs` | Orchestrates: backup → write plan JSON → start helper elevated → wait → read result JSON → remove package |
| Helper script | `Assets/DevUninstall/Uninstall-OrielFolderDev.ps1` (packaged Content) | Elevated. Reads plan JSON; per class re-checks the stale rule on the **real** hive, `reg export` backup, deletes `command` (+ empty `open`); deletes listed folders; removes Listary plugin folder; `SHChangeNotify`; writes result JSON. Never removes the package |
| UI | `Views/SettingsModeView.xaml` (Advanced section, last card) + new partial `Views/SettingsModeView.DevUninstall.cs` | Red button, ContentDialog with checkbox, progress, error/success dialogs |
| Strings | `Services/LocalizationData.cs` | ~12 new keys × 9 languages |

## Flow

1. Button visible only if `Package.Current.Id.Name == "LocalDevelopment.OrielFolderDev"` (R2). Otherwise collapsed.
2. ContentDialog (R3): lists steps / kept items / UAC / app closes; checkbox "先导出配置备份" (checked); Cancel default.
3. Backup (R4): `CaptureConfigurationAsync()` (existing) → `ConfigurationBackupService.WriteFileAsync` to
   `<base>\backups\OrielFolder-config-<yyyyMMdd-HHmmss>.json`, `<base>` = parent of `AppIdentity.MarkedOutputRoot`
   (`D:\codex\OrielFolderDev` or `Documents\OrielFolderDev`). Failure → stop.
4. Plan JSON written to `<base>\backups\uninstall-<ts>\plan.json`: classes, folders to delete
   (`%LOCALAPPDATA%\OrielFolderDev` resolved via `Environment.GetFolderPath` — the real path string, `<base>\preview-cache`),
   Listary plugin dir (from a running `Listary` process path, else common install dirs; optional), registry-backup dir,
   result path. Folders are validated in C# against an allow-list (must end with `\OrielFolderDev`, `\preview-cache`,
   `\FileAppPlugins\OrielFolderDev`; never `marked-files`/`backups`) and re-validated in the script.
5. Helper: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<package>\Assets\DevUninstall\Uninstall-OrielFolderDev.ps1" -Plan <plan.json>`
   via `ProcessStartInfo { UseShellExecute = true, Verb = "runas" }`. UAC cancelled → `Win32Exception` → stop (R8).
6. Helper result JSON: per step `ok/skipped/failed` + message. Any `failed` → app shows the failing step; package kept.
7. Success dialog: backup path + "移除应用" → `PackageManager.RemovePackageAsync(Package.Current.Id.FullName)`.
   The deployment service performs removal and terminates the app. If the call throws (e.g. access/capability),
   fallback: start `powershell.exe -NoProfile -Command Remove-AppxPackage <FullName>` (non-elevated, same user) and
   tell the user; if both fail, show the manual command. (Technical unknown → verified in Phase 2, see implement.md.)

## Safety properties

- Registry: only `HKCU\Software\Classes\{Folder,Directory,Drive}\shell\open[\command]`, exact rule, re-checked in the
  elevated process immediately before deletion, `.reg` exported first. HKLM never touched. Other verbs untouched.
- Files: allow-listed folder suffixes only; `marked-files` and `backups` explicitly excluded; no recursion outside.
- Package: only `Package.Current` (the Dev package). Release `OrielFolder.Desktop` / legacy `LocalDevelopment.SpanDev`
  are never referenced.
- **The elevated helper never trusts paths from `plan.json`** (it lives in user-writable `D:\codex`): it derives the
  only deletable folders and the backup root itself (`Get-ExpectedLayout`, same rules as `AppIdentity`), iterates its
  own list, and exits without writing anything if `ResultPath` / `RegistryBackupDirectory` are outside `…\backups\`.
  (Found in self-review during implementation; covered by `tests/test-dev-uninstall-script.ps1`.)
- Residual risk (Dev only): the helper script itself sits in the dev-registered layout under the repo `bin\`, which is
  user-writable; anything running as the user could alter it before the UAC prompt. Acceptable for a development build;
  a release variant would need the script inside a signed, installed package.
- Idempotent: rerunning after a partial failure finds less to do and continues.
- Dev-only: no change to release identity, packaging, installer, `dist/`, `main`.

## Trade-offs considered

- **Non-elevated breakaway process** (`PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY`) would avoid UAC but is new,
  undocumented in this code base and harder to verify → rejected for MVP.
- **Do cleanup in-app** → impossible for real hive/AppData (virtualisation).
- **Helper also removes the package** → removal would happen without the app being able to show the final message;
  kept in the app (last step) instead.

## Known limitations

- If the Windows account is a standard user, UAC asks for an admin account; the elevated helper would then see that
  admin's HKCU. The dialog tells the user; the helper refuses to run when its user SID differs from the app's (passed in
  the plan) → stop with a clear message.
- Removing a dev-registered package does not delete the build layout in `bin\` (expected; `start-dev.ps1` re-registers).
