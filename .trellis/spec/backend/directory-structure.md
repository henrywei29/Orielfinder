---
name: Backend directory structure
description: Services, file-system providers, file operations and worker processes in OrielFolder
paths:
  - "src/OrielFolder/OrielFolder/Services/**"
  - "src/OrielFolder/OrielFolder.Thumbs/**"
  - "src/OrielFolder/OrielFolder.DocumentPreview.Worker/**"
  - "src/OrielFolder/Listary.FileAppPlugin.OrielFolder/**"
---

# Backend Directory Structure

```
src/OrielFolder/OrielFolder/
├── Services/
│   ├── I*.cs                       # interfaces for mockable services (IFileSystemService, ISettingsService, IShellService, ...)
│   ├── FileSystemRouter.cs         # path → provider dispatch
│   ├── LocalFileSystemProvider.cs  FtpProvider.cs (FluentFTP)  SftpProvider.cs   (IFileSystemProvider)
│   ├── FileSystemService.cs  FileSystemWatcherService.cs  FolderContentCache.cs
│   ├── FolderSizeService.cs  FolderChildCountService.cs  RecursiveSearchService.cs
│   ├── FileOperationManager.cs     # queue/progress/undo orchestration
│   ├── FileOperations/             # IFileOperation impls: Copy, Move, Delete (ShellDeleteWithProgress), Rename,
│   │                               #   NewFile, NewFolder, Compress, Extract, BatchRename; OperationResult; FileOperationHistory
│   ├── Archive/                    # OtterZipEngine + native interop (third_party otterzip_ffi.dll, x64 only)
│   ├── Thumbnails/                 # ThumbnailClientService, WorkerProcess (NamedPipe client), IpcProtocol, ThumbnailDiskCache
│   ├── Preview*, OfficeDocumentPreviewService, OcrService, PreviewAnnotationExportService
│   ├── Shell*: ShellService, ShellContextMenu, ShellNewService, ShellStaWorker, ContextMenuService
│   ├── SettingsService, WorkspaceService, FolderTagService, FavoritesService, ShelfService, KeyBindingService,
│   │   ConfigurationBackupService, ConnectionManagerService (connections.json / credentials.dat)
│   ├── ListaryWindowBridge, DefaultFileManagerService, JumpListService, TrayIconService
│   ├── Localization{Service,Data,Polish}, CrashReportingService (Sentry, DSN empty in fork), ActionLogService
│   └── Cloud*, GitStatusService, NetworkBrowserService, WslDistributionService, RecycleBinService
├── Helpers/   # AppIdentity, AppSettings, DebugLogger, DispatcherHelper, NativeMethods, LaunchArguments,
│              # DevelopmentPackageActivation, DesktopShellIdentity, JobObjectHelper, UncPathHelper, ...
└── Models/    # IFileSystemItem family, DTOs, PaneLayoutGeometry

src/OrielFolder/OrielFolder.Thumbs/                # worker exe; --pipe <name>; NamedPipeServerStream event loop
src/OrielFolder/OrielFolder.DocumentPreview.Worker/ # net48, Office automation → preview output
src/OrielFolder/Listary.FileAppPlugin.OrielFolder/  # net48 plugin; manifest Id "OrielFolderDev", entry Listary.FileAppPlugin.OrielFolderDev.dll
```

## Rules

- New service: interface in `Services/I<Name>Service.cs` if it will be mocked, implementation
  `Services/<Name>Service.cs`, registration in `App.xaml.cs::ConfigureServices()`.
- New remote/virtual file system: implement `IFileSystemProvider` and route in `FileSystemRouter`; do not
  special-case paths throughout the UI.
- New file operation: implement `IFileOperation` in `Services/FileOperations/`, return `OperationResult`,
  support `CanUndo` when reversible, report progress via `FileOperationProgress`, and run through
  `FileOperationManager` so history/undo/progress UI work.
- Worker executables use `AppIdentity.*ExecutableName` constants; worker names must stay Dev-isolated
  (`OrielFolderDev.*`) in this checkout. The thumbnail worker is tied to the app's lifetime via `JobObjectHelper` (`WorkerProcess.cs`).
- Worker IPC changes must update both sides (`IpcProtocol.cs` and `OrielFolder.Thumbs/Program.cs`) together.
