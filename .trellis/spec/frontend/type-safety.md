# Type Safety (C#)

> No TypeScript here; this covers C# typing conventions used across UI and services.

- `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` in the main app.
  New code must be null-annotated; do not add `#nullable disable` or blanket `!` suppression.
- File-scoped namespaces (`namespace OrielFolder.Helpers;`) in newer files; block namespaces in older
  upstream files — follow the file you are editing.
- Namespaces are `OrielFolder.*`. Do not introduce new `Span.*` namespaces.
- Models implement `IFileSystemItem` (`DriveItem`, `FolderItem`, `FileItem`). Platform-specific model parts
  live in `*.WinUI.cs` partials (`DriveItem.WinUI.cs`, `ShelfItem.WinUI.cs`) so the core part can be
  compiled by `OrielFolder.Tests` / PowerShell `Add-Type` without WinUI types. Keep that split.
- Pure logic intended for testing (e.g. `Models/PaneLayoutGeometry.cs`, helpers in `Helpers/`) must avoid
  WinUI/WinRT dependencies — tests compile them standalone.
- DTOs for persistence end in `Dto` (`TabStateDto`, `WorkspaceDto`) and use System.Text.Json.
- Paths: compare with `StringComparison.OrdinalIgnoreCase` (~360 existing uses); never culture-sensitive
  comparison for paths. Natural sort via `NaturalStringComparer`. UNC/long paths via `UncPathHelper`.
- Enums for modes (`ViewMode`, `ActivePane`, `PreviewType`); do not pass mode strings around.
