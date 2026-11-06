---
name: OrielFolder architecture
description: Projects, processes, identities and data flow of the current OrielFolder working tree
---

# Architecture (current working tree, verified 2026-11-06)

> Source of truth is the **working tree**, not `HEAD`. `HEAD` (59a4af3) still contains
> `src/Span/...`; the renamed tree under `src/OrielFolder/...` is staged/unstaged work.

## What the product is

OrielFolder is a Windows 10/11 desktop file manager: **WinUI 3 / Windows App SDK 1.8,
.NET 8 (`net8.0-windows10.0.19041.0`), C#, MVVM with CommunityToolkit.Mvvm**, packaged
as MSIX. Main features: Miller columns + Details/List/Icon views with inline folder
tree, multi-tab, dual/quad-pane workspace, Shelf, preview panel + Quick Look
(images, PDF with annotations/export, Office, code, archives, OCR), folder tags and
color labels, Listary 6 integration, configuration backup, product icon themes.

## Projects (`src/OrielFolder/`)

| Project | TFM | AssemblyName (current) | Role |
|---------|-----|------------------------|------|
| `OrielFolder/OrielFolder.csproj` | net8.0-windows10.0.19041.0, WinUI, self-contained, win-x64 | `OrielFolderDev` | Main app (WinExe) |
| `OrielFolder.Thumbs` | net8.0-windows10.0.19041.0 | `OrielFolderDev.Thumbs` | Out-of-process thumbnail worker, NamedPipe IPC (`Services/Thumbnails/WorkerProcess.cs` ↔ `OrielFolder.Thumbs/Program.cs`). Built self-contained and packaged into a subfolder by a custom target in the main csproj |
| `OrielFolder.DocumentPreview.Worker` | net48 | `OrielFolderDev.DocumentPreview.Worker` | Office → preview conversion worker (needs local Microsoft Office) |
| `Listary.FileAppPlugin.OrielFolder` | net48 | `Listary.FileAppPlugin.OrielFolderDev` | Listary 6 file-app plugin bridge |
| `OrielFolder.Tests` | net8.0-windows10.0.19041.0 | — | MSTest + Moq unit tests |
| `OrielFolder.UITests` | net8.0-windows | — | FlaUI UI automation (needs running app) |
| `OrielFolder.ScreenshotAnalyzer` | — | — | Dev tool for UI test screenshots |

`OrielFolder.sln` currently lists only `OrielFolder`, `OrielFolder.Tests`, `OrielFolder.Thumbs`.
Other projects are built directly by path (see build-test-verification.md).

Also: `tests/annotation-export/OrielFolder.AnnotationExport.Tests.csproj` (console test),
`third_party/` native archive engine `otterzip_ffi.dll` (x64 only).

## Identity: this checkout is the isolated **Dev** build

Defined in `src/OrielFolder/OrielFolder/Helpers/AppIdentity.cs` and `Package.appxmanifest`:

- Package identity `LocalDevelopment.OrielFolderDev`, publisher `CN=OrielFolder Dev`,
  display name `OrielFolder Dev`, executable `OrielFolderDev.exe`.
- Data folder `%LOCALAPPDATA%\OrielFolderDev`; preview cache / marked output go to
  `D:\codex\OrielFolderDev\...` when `D:\codex` exists.
- `AllowsDefaultAssociations = false` — Dev must never take over default file/folder associations.
- `start-dev.ps1` refuses to register anything except this identity.
- `package-oriel.ps1` (release) refuses to run unless the manifest is the release identity
  `OrielFolder.Desktop` / `CN=OrielFolder`. So **release packaging is intentionally blocked
  in this checkout**. Do not flip identities without explicit user approval.
- The user's machine also has an installed **official/release OrielFolder**. Never modify,
  uninstall, re-register or overwrite it (see git-workspace-safety.md).

## Main app internal structure (`src/OrielFolder/OrielFolder/`)

- `Program.cs` — custom entry (`DISABLE_XAML_GENERATED_MAIN`), single-instance, `ORIEL_STANDALONE` branches.
- `App.xaml.cs` — DI container (`ConfigureServices()`, `Microsoft.Extensions.DependencyInjection`), crash handlers, multi-window registry. Access services via `App.Current.Services.GetRequiredService<T>()`.
- `MainWindow.xaml(.cs)` (~7,100 lines code-behind) split into ~20 partials:
  `MainWindow.{TabManager, NavigationManager, KeyboardHandler, DragDropHandler,
  FileOperationHandler, SettingsHandler, SplitPreviewManager, WorkspaceLayout,
  WorkspaceRouting, PaneSizing, OuterColumnResize, MillerColumnSizing, ShelfHandler,
  RecycleBinHandler, CommandPaletteHandler, ListaryIntegration, FolderTags,
  ColorLabelCategories}.cs`. Add new MainWindow behavior to the matching partial.
- `ViewModels/` — `MainViewModel` (+ `.TabManagement`, `.ViewMode`, `.MultiPane`, `.FileOperations`)
  → `ExplorerViewModel` (Miller engine, + `.InlineTree`, `.ColorLabels`) → `FolderViewModel` / `FileViewModel`
  (base `FileSystemViewModel`), plus `PreviewPanelViewModel`, `QuickLookViewModel`, dialogs.
- `Views/` — `DetailsModeView`, `ListModeView`, `IconModeView`, `HomeModeView`, `SettingsModeView`,
  `RecycleBinModeView`, `LogModeView`, `PreviewPanelView`, `QuickLookWindow`, `SettingsWindow`,
  `WorkspaceFolderPane`, `DocumentPreviewView`; `Views/Controls/`, `Views/Dialogs/`; `Controls/AddressBarControl`.
- `Services/` (~66) — `FileSystemService`, `FileSystemRouter` → `LocalFileSystemProvider` / `FtpProvider`
  (FluentFTP) / `SftpProvider`; `Services/FileOperations/` (copy/move/delete/rename/compress/extract/batch
  rename via `FileOperationManager`); `Services/Archive/` (`OtterZipEngine`); `Services/Thumbnails/`;
  `PreviewService`, `OfficeDocumentPreviewService`, `OcrService`; `SettingsService`, `WorkspaceService`,
  `FolderTagService`, `ConfigurationBackupService`, `ShellService`/`ShellContextMenu`/`ShellStaWorker`,
  `ListaryWindowBridge`, `CrashReportingService` (Sentry), `LocalizationService` (+ `LocalizationData`).
- `Helpers/` (~60) — converters, `DebugLogger`, `DispatcherHelper.SafeEnqueue`, `NativeMethods`,
  `NaturalStringComparer`, search parsing, `AppIdentity`, `AppSettings`, `LaunchArguments`,
  `DevelopmentPackageActivation`.
- `Models/` — `IFileSystemItem` → `DriveItem`/`FolderItem`/`FileItem`, `TabItem`, workspace DTOs,
  `PaneLayoutGeometry` (pure layout math, unit-tested from PowerShell via `Add-Type`).
- `Assets/` — icons, fonts (Remix/Phosphor/Tabler/Pretendard), WebView2 assets (`Onboarding/`, `PdfViewer/` = pdf.js).

## Packaging / distribution

- Dev: `build-dev.ps1` (build only) → `start-dev.ps1` (register Dev package + activate via AUMID).
- Release: `package-oriel.ps1` → MSIX (makeappx) + signtool + Inno Setup (`installer/OrielFolder.iss`)
  → `OrielFolder-1.0.1-x64-Setup.exe` (+ `.bin` slices) in `dist/`. Install helpers
  `installer/Install-OrielFolder*.ps1`, `installer/Initialize-InstallerPowerShell.ps1`.
- Standalone (unpackaged EXE) conversion via `-p:OrielStandalone=true` / `ORIEL_STANDALONE` is
  **partial and unfinished** (see rename-migration-status.md).

## Code comment language

Inherited upstream code has many **Korean** comments; new fork code uses English/Chinese.
Do not mass-translate or reformat comments — it creates huge diffs in an already-dirty tree.
