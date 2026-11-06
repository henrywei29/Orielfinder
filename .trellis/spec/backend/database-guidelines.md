# Persistence & Settings (no database)

> OrielFolder has **no database/ORM**. This file covers what the template calls "database":
> persisted settings and JSON state files.

## Stores

| Data | Store |
|------|-------|
| Preferences | `AppSettings.LocalSettings` → packaged `Windows.Storage.ApplicationData` LocalSettings, wrapped by `SettingsService` / `ISettingsService` |
| Workspaces, tabs session | `workspaces.json` (`WorkspaceService`, `WorkspaceDto`, `TabStateDto`) |
| Folder tags / color labels | `folder-tags.json` (`FolderTagService`) — never writes `desktop.ini` into user folders |
| Action log | `action_log.json` |
| Remote connections | `connections.json` (+ `.bak`), secrets in `credentials.dat` (`ConnectionManagerService`) — sensitive, never print/commit |
| Thumbnail cache | `ThumbnailDiskCache`; preview cache under `AppIdentity.PreviewCacheRoot` |
| Logs | see logging-guidelines.md |

Serialization: `System.Text.Json`.

## Isolation (hard rule)

- Dev data lives in the Dev package's LocalState / `%LOCALAPPDATA%\OrielFolderDev`. It must **never** read or
  write the installed release app's package family (`OrielFolder.Desktop_*`) — `tests/test-oriel-profile.ps1`
  fails if `AppSettings.cs` references `OrielFolder.Desktop`.
- Migration *from* legacy data is copy-only: the previous installation keeps its own data
  (see `AppSettings.MigrateLegacyData`, `ConnectionManagerService` one-time migration from `%LOCALAPPDATA%\Span`).

## Rules

- Settings keys are a compatibility contract. Do not rename or delete keys; add new ones with defaults and,
  if needed, a one-time migration guarded by a `...MigrationCompleted` key.
- Write JSON atomically: temp file then `File.Move(tmp, path, overwrite: true)` — existing pattern in
  `ConnectionManagerService`, `FolderTagService`, `ConfigurationBackupService`. Keep existing `.bak` behavior.
- New persisted user-facing state should be included in `ConfigurationBackupService` export/import.
- Standalone (`ORIEL_STANDALONE`) storage is unfinished: `DesktopSettingsStore` is referenced but missing.
  Do not build on it until the standalone task is approved and active.
