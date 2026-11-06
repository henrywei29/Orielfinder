# Error Handling

## Patterns in use

- **File operations** return `OperationResult` (success/failure/partial + messages) instead of throwing to the UI;
  conflicts go through `FileConflictDialogViewModel`. Cancellation via `CancellationToken` is a normal outcome, not an error.
- **Services** catch expected I/O exceptions (`IOException`, `UnauthorizedAccessException`, `COMException` from
  Shell APIs) close to the call, log via `DebugLogger`, and return a safe fallback (empty list, null, false).
  Don't swallow silently: at minimum `DebugLogger.Log($"[Area] context: {ex.Message}")`.
- **UI entry points**: async event handlers have an outermost try/catch; deferred UI work uses
  `DispatcherHelper.SafeEnqueue` which logs exceptions with file/caller info.
- **App level** (`App.xaml.cs`): unhandled exception handlers log crash context (`DebugLogger.LogCrash`) and
  forward to `CrashReportingService`. Sentry: capture count per session is throttled; synchronous fatal capture only
  from the domain-unhandled (process ending) path, never for `e.Handled = true` non-fatal exceptions.
  In this fork the Sentry DSN is empty → reporting is effectively disabled; do not add a DSN without user approval.
- **WER local dumps** (`WerHelper`) for native crashes.

## Native / shell safety (crashes here are uncatchable)

- Shell extension hosting: `SetThreadErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX)` with try/finally
  restore, `CMIC_MASK_FLAG_NO_UI`, guard `GetCommandString` for IDs > 5000.
- Never call `StoreContext.GetDefault()` unless the package signature kind is Store.
- Network Shortcuts folders have ReadOnly attributes — clear to `FileAttributes.Normal` before delete.
- `DriveInfo.VolumeLabel` for network drives returns the remote label; use `WNetGetConnectionW` for Explorer-like names.

## Rules

- Never turn a failed file operation into a silent success; never delete/overwrite as error recovery.
- Error messages shown to users go through localization.
- Do not catch `Exception` around large blocks just to hide failures; catch narrowly and log.
