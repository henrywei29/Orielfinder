# Build, Test & Verification

> Report only what actually ran. "Edited the code" is not "verified". A build of the old
> `src/Span` tree does not validate the renamed `src/OrielFolder` tree.

## Toolchain

- .NET SDK 8.0.406 (`global.json`, rollForward latestFeature), Windows SDK 10.0.19041+, Windows App SDK 1.8.
- Build scripts prefer the local toolchain in the **parent** folder `D:\project\Orielfolder\.tools\`
  (`dotnet\dotnet.exe`, `nuget-packages`, `cli-home`, `InnoSetup\ISCC.exe`). When it exists they
  redirect `APPDATA`/`LOCALAPPDATA`/`NUGET_PACKAGES` into `.tools` and use it as the offline NuGet source.
- WinUI 3 apps cannot be started with `dotnet run`; the Dev app runs as a registered MSIX layout.

## Commands (run from the Git root, PowerShell)

| Purpose | Command | Side effects — know before running |
|---------|---------|-----------------------------------|
| Dev build (x64 Debug) | `powershell -NoProfile -ExecutionPolicy Bypass -File .\build-dev.ps1` | **Force-stops** `OrielFolderDev`, `OrielFolderDev.Thumbs`, `OrielFolderDev.DocumentPreview.Worker` processes. Only Dev names; the installed release app is not targeted. Tell the user before running if Dev is open |
| Register + launch Dev | `.\start-dev.ps1` | Registers `LocalDevelopment.OrielFolderDev` via `Add-AppxPackage -Register`, may remove a stale `LocalDevelopment.OrielFolder` dev registration, needs Developer Mode. Never touches release packages |
| Listary plugin | `.\build-listary-plugin.ps1` | Build only |
| Connect Listary | `.\connect-listary.ps1` | Writes into the user's Listary plugin setup — **ask first** |
| Release package | `.\package-oriel.ps1` | Creates/uses a signing certificate in `Cert:\CurrentUser\My`, signs MSIX/EXE, writes `D:\codex\OrielFolder\distribution-*`. **Blocked for the Dev manifest and requires explicit user approval** |
| Unit tests | `dotnet test src\OrielFolder\OrielFolder.Tests\OrielFolder.Tests.csproj -p:Platform=x64` | Use the `.tools` dotnet when present (same env vars as build-dev.ps1) |
| Single unit test | add `--filter "FullyQualifiedName~<Class>.<Method>"` | |
| UI tests (FlaUI) | `dotnet test src\OrielFolder\OrielFolder.UITests\OrielFolder.UITests.csproj -p:Platform=x64` | Needs the Dev app registered/running; drives the real UI |
| Identity isolation | `.\tests\test-oriel-profile.ps1` | Static check that Dev identity cannot collide with installed OrielFolder |

`CONTRIBUTING.md` / PR template commands were updated to `src/OrielFolder/...` on 2026-11-06; the table above is authoritative.

**Running without killing a running Dev app:** `OrielFolder.Tests` compiles the sources directly (no ProjectReference),
so the unit-test command above never touches `bin\` of the app. To compile the app while Dev is open, build a *copy*:
robocopy `src` + `third_party` + `global.json`/`LICENSE*`/`OpenSourceLicenses.md` to `D:\codex\...\buildcopy\` and run the
`build-dev.ps1` `dotnet build` line there (example: `D:\codex\Orielfolder-dev\rename-task-20261106\isolated-build.ps1`).
`dotnet test` does not accept `--source`; run `dotnet restore --source <nuget-packages>` first, then `dotnet test --no-restore`.

**PowerShell version:** many `tests/test-*.ps1` need PowerShell 7 (`Add-Type` with C# 8+ syntax, UTF-8 without BOM).
Windows PowerShell 5.1 fails them at compile/parse time (`Add-Type :` / ParserError) — that is an environment
failure, not a test failure. PowerShell 7 was not installed on this machine as of 2026-11-06.

## Regression scripts `tests/test-*.ps1` (~78) and `tests/**/*.mjs`

- The project's dominant test style: **static, fast PowerShell checks** that parse XAML/C# source
  (`[xml]` on `MainWindow.xaml`, regex on `.cs`) and compile pure classes with `Add-Type` (e.g.
  `Models/PaneLayoutGeometry.cs` in `test-pane-layout.ps1`). They print `PASS: ...` and `throw` on failure.
- Repo resolution pattern: `$repo = Split-Path $PSScriptRoot -Parent` or `Join-Path $PSScriptRoot '..\src\OrielFolder\OrielFolder'`.
- Some scripts are **live**/performance/packaged checks (e.g. `test-packaged-startup.ps1`,
  `test-packaged-settings-live.ps1`, `tests/integration/test-listary-plugin-live.ps1`,
  `test-document-preview-office.ps1`). Read a script before running it; if it registers packages,
  launches apps, touches Listary/Office, or writes outside the repo, ask the user first.
- `.mjs` tests exercise the pdf.js viewer assets with Node.
- When fixing a bug, prefer adding/adjusting a focused `tests/test-<behavior>.ps1` or an MSTest in
  `OrielFolder.Tests` that fails before the fix and passes after.

## Verification levels (state which one you reached)

1. **Static** — targeted `tests/test-*.ps1` passed.
2. **Compiled** — `build-dev.ps1` exit 0 on the current tree.
3. **Unit** — `OrielFolder.Tests` (or a filtered subset) passed.
4. **Runtime** — Dev app launched via `start-dev.ps1` and the behavior was observed (say how).
5. **User-accepted** — the user confirmed the behavior.

UI/layout/performance changes need at least level 2 plus a focused static/perf script; claim
level 4/5 only with evidence. Distinguish measured improvement from hypothesis.

## Outputs and temp files

Build outputs stay in `bin/`/`obj/` (gitignored). Logs, traces, screenshots, backups, temporary
profiles go under `D:\codex\...` — never drive C, never the repo root.
