# Directory Structure (working tree, 2026-11-06)

> The repository root is `D:\project\Orielfolder\Orielfolder` (Git top-level).
> The parent `D:\project\Orielfolder\` holds `.tools\` (local dotnet SDK, NuGet cache,
> Inno Setup, WebView2 bootstrapper) used by the build scripts. It is **outside** Git.

```
Orielfolder/                       # Git root
├── OrielFolder.sln                # renamed from Span.sln (staged rename + edits)
├── global.json                    # .NET SDK 8.0.406, rollForward latestFeature
├── README.md  FEATURES.md  CONTRIBUTING.md  PRIVACY.md  SECURITY.md  LICENSE*  OpenSourceLicenses.md
├── build-dev.ps1                  # Dev build only (stops Dev processes first!)
├── start-dev.ps1                  # Register + activate LocalDevelopment.OrielFolderDev
├── build-listary-plugin.ps1  connect-listary.ps1
├── package-oriel.ps1              # Release MSIX + signing + Inno Setup (blocked for Dev identity)
├── prepare-oriel-icons.ps1
├── src/OrielFolder/               # renamed from src/Span/ (see rename-migration-status.md)
│   ├── OrielFolder/               # main WinUI app (Views, ViewModels, Services, Helpers, Models, Controls, Assets)
│   ├── OrielFolder.Thumbs/        # thumbnail worker (NamedPipe)
│   ├── OrielFolder.DocumentPreview.Worker/   # net48 Office conversion worker
│   ├── Listary.FileAppPlugin.OrielFolder/    # net48 Listary 6 plugin (was Listary.FileAppPlugin.SpanDev)
│   ├── OrielFolder.Tests/         # MSTest: Unit/, Services/, ViewModels/, Helpers/, Models/, Views/, Integration/, Stress/, Stubs/, Fixtures/
│   ├── OrielFolder.UITests/       # FlaUI UI tests (OrielFolderAppFixture.cs, Tests/)
│   └── OrielFolder.ScreenshotAnalyzer/
├── tests/                         # ~78 PowerShell regression scripts test-*.ps1, Node test-*.mjs
│   ├── integration/               # Office preview, Listary host/dialog, pdf.js viewer .mjs tests
│   ├── annotation-export/         # console test project
│   └── manual-data/               # captured manual measurement data
├── installer/                     # OrielFolder.iss, Install-OrielFolder*.ps1, Initialize-InstallerPowerShell.ps1 (new, untracked)
├── dist/                          # prebuilt release installer v1.0.1 (do not overwrite)
├── third_party/otterzip/          # vendored native archive engine
├── tools/                         # Repair-StaleFolderOpen.ps1 (untracked; HKCU folder-open repair, -Apply gated)
├── agent-handoffs/                # LEGACY pre-Trellis handoff records (untracked; read-only history)
├── .github/                       # issue / PR templates
├── .trellis/                      # Trellis: spec/, tasks/, workspace/, workflow.md, scripts/, config.yaml
├── .claude/  .codex/  .agent/  .agents/   # agent platform entry points (Trellis-generated)
├── AGENTS.md  CLAUDE.md           # gitignored; CLAUDE.md = "@AGENTS.md"
└── .gitattributes                 # new, untracked
```

## Placement rules

| New thing | Goes in |
|-----------|---------|
| UI view / user control | `src/OrielFolder/OrielFolder/Views/` (controls: `Views/Controls/`, dialogs: `Views/Dialogs/`) |
| MainWindow behavior | the matching `MainWindow.<Area>.cs` partial |
| ViewModel | `ViewModels/{Name}ViewModel.cs` (split large ones into `{Name}ViewModel.{Area}.cs` partials) |
| Service | `Services/{Name}Service.cs` (+ `I{Name}Service.cs` when mocked in tests), register in `App.xaml.cs` |
| Pure helper / converter | `Helpers/` (`{Purpose}Converter.cs`) |
| Model / DTO | `Models/` (`{Name}Item.cs`, `{Name}Dto.cs`) |
| Unit test | `src/OrielFolder/OrielFolder.Tests/<matching folder>/` |
| Regression / layout / perf script | `tests/test-<behavior>.ps1` (resolve repo via `Split-Path $PSScriptRoot -Parent`) |
| Dev/maintenance tool | `tools/` — read-only by default, writes behind an explicit `-Apply` |
| Agent working notes | `.trellis/tasks/<task>/` and `.trellis/workspace/` — never in `src/` |
| Temporary output, caches, backups | `D:\codex\...` (user rule: do not write to drive C) |

## Do not touch without explicit approval

`dist/`, `installer/`, signing/certificate logic in `package-oriel.ps1`, `Package.appxmanifest`
identity, `.github/`, `third_party/` binaries, `LICENSE*`.
