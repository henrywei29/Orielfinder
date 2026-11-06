# Backend Layer — C# Services, File Operations, Workers, Integrations

> In OrielFolder "backend" means the **non-UI C# code inside the desktop app and its helper
> processes**: `Services/**`, `Helpers/**` (non-converter), `Models/**`, `Program.cs`,
> `OrielFolder.Thumbs`, `OrielFolder.DocumentPreview.Worker`, `Listary.FileAppPlugin.OrielFolder`,
> and PowerShell build/installer scripts. There is no web server, HTTP API, or database.

## Guidelines Index

| Guide | Description |
|-------|-------------|
| [Directory Structure](./directory-structure.md) | Services, providers, file operations, worker processes |
| [Persistence & Settings](./database-guidelines.md) | (template "database") LocalSettings, JSON stores, migrations, data isolation |
| [Error Handling](./error-handling.md) | Exceptions, OperationResult, crash reporting, native safety |
| [Logging](./logging-guidelines.md) | DebugLogger, log locations, what not to log |
| [Quality Guidelines](./quality-guidelines.md) | File-operation safety, tests, forbidden patterns |

## Pre-Development Checklist

- [ ] Read `../project/index.md` (Git safety, rename status, handoff) first.
- [ ] Locate the owning service; reuse it instead of adding a parallel one (see `../guides/code-reuse-thinking-guide.md`).
- [ ] For file operations: know undo (`FileOperationHistory`), conflict dialogs, progress, cancellation, Recycle Bin semantics.
- [ ] For anything touching identity, package family, data folders, Listary, installer or signing: check
      `../project/architecture.md` — Dev/release isolation is a hard requirement.
- [ ] Find the `tests/test-*.ps1` / MSTest classes that cover the code.

## Quality Check

- [ ] No user data loss path (overwrite/delete) without confirmation, conflict handling and undo where the existing operation supports it.
- [ ] Paths compared `OrdinalIgnoreCase`; long/UNC/network/cloud placeholders considered.
- [ ] I/O off the UI thread, cancellable, exceptions logged.
- [ ] Dev identity/data isolation intact (`tests/test-oriel-profile.ps1`).
- [ ] Tests run and reported honestly.
