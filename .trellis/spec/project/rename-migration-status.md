# Rename & Migration Status: Span → OrielFolder

> Updated 2026-11-06 by Claude Code (task `11-06-span-orielfolder-rename`) from the working tree
> (HEAD 59a4af3, branch `main`). **This file records state.** Items under "Needs user approval" must not
> change without an approved task. Task record (archived, complete): `.trellis/tasks/archive/2026-11/11-06-span-orielfolder-rename/handoff.md`.
> Committed on branch `rename/orielfolder` (2026-11-06, user-approved); `main` still has the pre-rename tree.

## Git state of the rename (not committed)

- 654 **staged** renames: `Span.sln → OrielFolder.sln`, `src/Span/** → src/OrielFolder/**`,
  `tests/annotation-export/Span.AnnotationExport.Tests.csproj → OrielFolder.AnnotationExport.Tests.csproj`.
  The index is untouched by later agents (staged diff sha1 `3793885a…`); content edits on top are unstaged (`RM`/` M`).
- Never "complete" the rename by re-staging, committing, or restoring `src/Span/` paths.

## Done (in working tree)

Antigravity / earlier agents (before 2026-11-06):
- Folder + project names: `OrielFolder`, `OrielFolder.Thumbs`, `OrielFolder.DocumentPreview.Worker`,
  `OrielFolder.Tests`, `OrielFolder.UITests`, `OrielFolder.ScreenshotAnalyzer`,
  `Listary.FileAppPlugin.OrielFolder` (`SpanDevPlugin.cs → OrielFolderPlugin.cs`), `OrielFolder.slnx`.
- Root namespace `OrielFolder` everywhere; XAML `x:Class`/`using:` namespaces.
- Dev identity: `LocalDevelopment.OrielFolderDev` (`CN=OrielFolder Dev`, family suffix `tvznpbczqragy`),
  AssemblyName `OrielFolderDev`, `%LOCALAPPDATA%\OrielFolderDev`, `AppIdentity.*` constants.
- Build/start/package scripts and most `tests/*.ps1` point to `src\OrielFolder\...`; README; Polish localization.

Claude Code, 2026-11-06 (backups of every touched file: `D:\codex\Orielfolder-dev\rename-task-20261106\backup\`):
- XAML theme resource keys `Span*Brush` / `Span*Color` (39 keys, ~1000 refs in XAML, C#, `tests/*.ps1`)
  → `OrielFolder*` (e.g. `OrielFolderAccentBrush`, `OrielFolderTextPrimaryBrush`), incl. the runtime palette
  overrides in `MainWindow.SettingsHandler.cs` / `App.xaml.cs`.
- XAML prefix `xmlns:spancontrols` → `xmlns:viewcontrols` (`MainWindow.xaml`; sibling of `appcontrols`).
- Define constant `SPAN_TESTS` → `ORIELFOLDER_TESTS` (`OrielFolder.Tests.csproj`, `FontScaleService.cs`).
- Main↔DocumentPreview.Worker stdout progress tokens `SPAN_PREVIEW:*` → `ORIELFOLDER_PREVIEW:*`
  (both ends ship in the same build; tests updated).
- Private names: `IsSpanExecutable` → `IsOrielFolderExecutable`, `OpenKeyHoldsOnlySpanCommand` →
  `OpenKeyHoldsOnlyOrielFolderCommand`; thread names `OrielFolder Shell STA`, `OrielFolder.FolderContextLaunch`;
  temp files `OrielFolder{SetDefault,RestoreDefault,CleanupOpenKeys}.reg`, `orielfolder_clip_*.db`.
- User-visible text: `LocalizationData` rows `Settings_ContextMenuDesc`, `Settings_BuyMeCoffeeDesc`, `Tray_Show`,
  `Tray_Exit`, `Settings_OpenLogsDesc`, `Settings_OnboardingReplayDesc` (9 languages, Korean particles adjusted);
  XAML default texts in `SettingsModeView.xaml`; onboarding `script.js`/`index.html` (`mc_span` → `mc_orielfolder`,
  `dfm_desc`), log-file header lines (`=== OrielFolder Debug Log`, `=== OrielFolder Thumbs Worker Log`).
- Paths/docs: `ScreenshotAnalyzer` default screenshot dir (was `src/Span/...`, functional), UITests doc comments,
  `CONTRIBUTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `FEATURES.md`, csproj comments (`OrielFolder.Thumbs`).
- Code comments that called the app "Span/SPAN"; stale path comments now name `AppIdentity.LocalDataRoot`
  (`WerHelper`, `ThumbnailDiskCache`, Thumbs `Program`, `CrashReportingService`, `ConnectionManagerService`).
- Mock data folder `MockDataService` "Span" → "OrielFolder"; test temp prefixes (`SpanTest_`, `SpanStress_`, …) → `OrielFolder*`.
- Stale test expectations: `test-packaged-settings-live.ps1` family name → `LocalDevelopment.OrielFolderDev_tvznpbczqragy`,
  `test-packaged-startup.ps1` stub key, `test-package-launch-arguments.ps1` samples, `test-listary-plugin-live.ps1`
  exe/process names (`OrielFolderDev`; "official" check now covers `Span` and `OrielFolder`).

## Intentionally kept `Span` references (do NOT rename)

| Location | Why it stays |
|----------|--------------|
| `Helpers/LaunchArguments.cs` (`Span.exe`, `Span.dll`, `spandev.exe`) + `LaunchArgumentsTests.cs` | Recognise legacy argv0 so it is not treated as a file argument |
| `Services/ConfigurationBackupService.cs` `"SPAN Dev Configuration"` + `tests/test-configuration-backup.ps1`, `test-preview-zoom-settings.ps1` | Legacy backups must stay importable |
| `tools/Repair-StaleFolderOpen.ps1` (`spanfinder.exe`, `Test-StaleSpanCommand`) + `tests/test-stale-folder-open.ps1` | Repairs registry overrides left by the upstream SPAN app — the name *is* the legacy target |
| `tests/test-oriel-profile.ps1` (`'SPAN Dev'` must be absent) | Guard against legacy UI text |
| Test fixture **content** (`"SPAN"` OCR word, `SPANDevOCRTest123`, `SPAN synthetic Word/Excel/PowerPoint`, `SPAN PDF selectable page one 123`, `IpcEnvelopeTests` sample path, `tests/manual-data/**`) | Data inside fixtures; changing it breaks fixtures/expected text |
| `LICENSE.md` branding line ("SPAN") | Legal text — not edited without the user |
| Historical comments with measured facts (`Span.exe`/`Span.dll` argv0, v1.4.x/v2.0.7 history) | Describe legacy behaviour |
| `Span<T>`, `ReadOnlySpan<T>`, `TimeSpan`, `AsSpan`, `Grid.ColumnSpan/RowSpan`, `<Span>`/`<span>`, pdf.js `Assets/PdfViewer/*`, `DiskSpanning` (Inno Setup), "Spanish" | Framework / third-party / unrelated words |

## Kept by user decision (2026-11-06: "Keep them for now") — persisted data / origins / external contracts

Rename only in a new, user-requested migration task.

| Identifier | Where | Risk if renamed |
|------------|-------|-----------------|
| Command IDs `span.*` (≈120, e.g. `span.edit.copy`) + test `AllCommandIds_StartWithSpanPrefix` | `Models/ShortcutCommands.cs`, `KeyBindingService`, `HelpFlyoutContent`, command palette | Saved custom key bindings (`KeyBindingService.ApplyAndSave`) reference these IDs → user shortcuts lost unless a load-time migration `span.` → new prefix is added |
| WebView2 virtual hosts `document.span.local`, `pdfpreview.span.local`, `onboarding.span.local` | `DocumentPreviewView.cs`, `OnboardingWindow.xaml.cs`, `tests/integration/*.mjs` | Origin change resets WebView2 per-origin storage (pdf.js view state/localStorage, permissions); all `.mjs` tests must change together |
| Log file names `Span_Debug_*.log`, legacy `Span_Debug.log`, `Span_Worker_*.log`, `Span_PreviousSession.log` | `DebugLogger.cs`, `WorkerLogger.cs`, `CrashReportingService.cs`, `DebugLoggerTests.cs` | Retention cleanup only matches the current prefix → old logs would never be rotated unless cleanup also matches the old prefix |
| Test env vars `SPAN_RUNTIME_PACKAGE_ROOT`, `SPAN_PDF_FIXTURE`, `SPAN_IMAGE_FIXTURE`, `SPAN_NATIVE_OCR_*`, `SPAN_MASK_RESULTS`, `SPAN_READER_LARGE_PDF`, `SPAN_PDF_PAGES`, `SPAN_PDF_EXPECTED_TEXT` | `tests/integration/*.mjs`, `OcrNativeAcceptanceTests.cs` | Developers' local scripts that set them would silently stop working (low risk) |
| `SPAN_SENTRY_DSN` (log message only) | `CrashReportingService.cs` | Crash-reporting configuration — out of scope |

## Unfinished / inconsistent (record only; separate tasks)

1. **`package-oriel.ps1` vs Dev AssemblyNames** (copies `OrielFolder.runtimeconfig.json`, `Listary.FileAppPlugin.OrielFolder.dll`;
   current AssemblyNames are `OrielFolderDev*`). Release packaging is blocked for the Dev manifest; HEAD's manifest had the
   release identity `OrielFolder.Desktop` / `CN=OrielFolder`. Resolve only inside an approved release task.
2. **Standalone EXE (`ORIEL_STANDALONE`) is partial**: `Helpers/AppSettings.cs` references `DesktopSettingsStore`, which does not exist.
3. `OrielFolder.sln` contains only 3 of the 7 projects (`OrielFolder.slnx` exists too).
4. A legacy dev package `LocalDevelopment.SpanDev` (`CN=SPAN Dev`) is still registered on the machine — not ours to remove.
5. `DebugLogger` comment mentions packaged family `LumiBearStudio.Orielfolder_*` (upstream publisher) — informational.
6. Many `tests/test-*.ps1` use `Add-Type` with modern C# and UTF-8-without-BOM text; they need **PowerShell 7**, which is
   not installed on this machine (Windows PowerShell 5.1 fails them before any assertion).

## Spelling

Code/identity uses `OrielFolder` (capital F). Some docs and the user use `Orielfolder`. Both refer to the
same product; do not rename one to the other unless asked.
