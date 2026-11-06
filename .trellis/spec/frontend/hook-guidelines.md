---
name: Event and dispatcher lifecycle
description: Event subscription, DispatcherQueue, async void and teardown guards in OrielFolder UI (replaces web "hooks")
paths:
  - "src/OrielFolder/OrielFolder/MainWindow*.cs"
  - "src/OrielFolder/OrielFolder/ViewModels/**"
  - "src/OrielFolder/OrielFolder/Views/**/*.cs"
---

# Event & Dispatcher Lifecycle

> Template topic "hooks" does not apply (no React). The WinUI equivalent is lifecycle-safe
> event subscription and UI-thread dispatching.

## Subscriptions

- Pattern: `x.Event -= Handler; x.Event += Handler;` to prevent accumulation.
- Replacing a column/explorer: unsubscribe `PropertyChanged` on the **old** ViewModel before subscribing
  the new one (`SetLeftExplorer`, column replace at index N, `SubscribePreviewToLastColumn` /
  `UnsubscribePreviewSelection` must pair). Leaks keep whole column trees alive.
- When a backing field is assigned directly (tab switch sets `_leftExplorer` to avoid cascades), per-instance
  subscriptions don't follow; existing code forwards via static events and compares `sender` — follow it.
- `IsSwitchingTab` suppresses spurious `SelectionChanged` while ListViews swap; `_isCleaningUp`
  (ExplorerViewModel.Cleanup) suppresses re-entrant PropertyChanged on disposed objects.

## UI thread

- Use `DispatcherHelper.SafeEnqueue(...)` (logs exceptions with caller info) for deferred UI work.
- **Every deferred UI callback checks `_isClosed`** before touching window resources — callbacks after
  window close cause access violations.
- Never write `DispatcherQueue.TryEnqueue(async () => { ... })`: that lambda is `async void` and its
  exceptions escape. Extract an `async Task` method and call `_ = DoWorkAsync();` inside a try/catch.
- Retry focus/layout with `TryEnqueue` at Low priority, not `Task.Delay` loops.

## async

- Only UI event handlers may be `async void`, and each needs an outermost `try/catch` that logs via
  `DebugLogger` (an unhandled exception in async void terminates the process).
- Services return `Task`/`Task<T>`; pass `CancellationToken` for navigation/loading and drop stale results
  (see `test-folder-loading-performance.ps1`: a cancelled load must not populate a stale folder).
- Disk/shell I/O never runs on the UI thread (`Task.Run` for thumbnail `File.Exists`/reads, etc.).

## Native / COM

- Shell context menu hosting: `SetThreadErrorMode` (not process-wide `SetErrorMode`) with try/finally restore,
  `CMIC_MASK_FLAG_NO_UI`, `GetCommandString` ID guard. STA work goes through `ShellStaWorker`.
- `StoreContext.GetDefault()` crashes natively in non-Store builds — check package signature kind first.
