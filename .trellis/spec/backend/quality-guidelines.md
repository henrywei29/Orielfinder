# Backend Quality Guidelines

## Conventions (from upstream CONTRIBUTING, still valid)

- Service methods return `Task`/`Task<T>`, never `async void` (only UI event handlers may).
- Path comparison always `StringComparison.OrdinalIgnoreCase`.
- Event handlers: `-=` before `+=`.
- Naming: `{Name}Service`, `I{Name}Service`, `{Name}Operation`, `{Name}Dto`, `{Name}Helper`.

## File-manager safety

- Delete defaults to Recycle Bin semantics where the existing operation does; permanent delete only through the
  existing explicit path. Never add silent overwrite.
- Copy/move must handle: same-folder targets, name conflicts, long paths, UNC/network, cloud placeholders
  (`CloudStorageProviderService`), read-only attributes, cancellation mid-way, partial failure reporting.
- Undo: keep `FileOperationHistory` consistent; operations with `CanUndo = true` must really be reversible.
- Shell operations (context menu, "New", properties) run on the STA worker and must not block the UI.
- Never change Windows default associations from Dev (`AppIdentity.AllowsDefaultAssociations = false`).

## PowerShell scripts (build, installer, tools, tests)

- `$ErrorActionPreference = 'Stop'`; use `-LiteralPath`; resolve paths from `$PSScriptRoot`.
- Destructive/system-changing tools are read-only by default and require an explicit `-Apply` plus backup
  (pattern: `tools/Repair-StaleFolderOpen.ps1`).
- Never broaden process kills beyond the exact Dev process names.
- Installer/signing/identity scripts are out of scope unless the task explicitly covers them.

## Tests

- MSTest + Moq in `OrielFolder.Tests` (`Unit/`, `Services/`, `Helpers/`, `Models/`, `ViewModels/`, `Integration/`, `Stress/`).
- `tests/test-*.ps1` for source-level invariants and fakes-based perf checks.
- A bug fix should come with a test that failed before the fix (or a stated reason why not).

## Forbidden

- Writing into the installed release app's data, package, Listary plugin folder or certificates.
- Adding telemetry endpoints/DSNs, network calls, or new third-party dependencies without approval.
- Dependency upgrades or TFM changes as a side effect of a feature.
