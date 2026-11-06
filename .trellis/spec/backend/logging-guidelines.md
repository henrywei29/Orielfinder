# Logging

## Mechanism

`Helpers/DebugLogger.cs` (static): `Log(string)`, `LogCrash(context, ex)`, `FlushSync()`, `Shutdown()`,
`GetLogsDirectory()`. Daily files `Span_Debug_<date>.log` (legacy prefix, still "Span" — see
`../project/rename-migration-status.md`), 7-day / 50-file retention.

Location: packaged → `ApplicationData.Current.LocalFolder\Logs` (real path under
`%LOCALAPPDATA%\Packages\LocalDevelopment.OrielFolderDev_*\LocalState\Logs`); unpackaged fallback →
`%LOCALAPPDATA%\OrielFolderDev\Logs`. Settings has an "open logs folder" action.

## Format

Prefix every message with a bracketed area tag, then context, then the exception message:

```csharp
DebugLogger.Log($"[SafeEnqueue] {fileName}:{caller} — {ex.Message}");
DebugLogger.Log("[Migration] Skipped an unsupported legacy setting type.");
```

## What to log

- Failures and fallbacks in services, worker start/stop/restart, IPC errors, migrations, crash context.
- Timing for performance investigations only while investigating; remove or gate noisy per-item logs.

## What never to log

- Credentials, `connections.json` / `credentials.dat` contents, tokens, certificate data.
- Full file contents or clipboard contents. File paths are acceptable for diagnostics.
- Per-row / per-frame messages in hot paths (scrolling, `ContainerContentChanging`) — they distort perf.

## Agent diagnostics

Diagnostic output produced by agents (traces, screenshots, probes) goes to `D:\codex\...`, not the repo and not drive C.
