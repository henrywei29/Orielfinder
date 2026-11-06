# UI Quality Guidelines

## Forbidden patterns

| Pattern | Why |
|---------|-----|
| Code assignment to a bound DependencyProperty | Destroys the `{Binding}` permanently |
| `DispatcherQueue.TryEnqueue(async () => ...)` | async void lambda, unobserved crash |
| `async void` without outer try/catch | Process termination |
| Deferred UI callback without `_isClosed` check | Access violation after window close |
| `+=` without prior `-=` / missing unsubscribe on cleanup | Leaks, duplicated handlers |
| Disk/shell I/O on the UI thread | UI freezes |
| DP writes / per-row system reads in `ContainerContentChanging` or scroll handlers | Breaks perf budgets (tests) |
| `Margin` changes during measure pass | COMException 0x8000FFFF |
| `SetTitleBar()` + `WM_NCHITTEST`, or controls inside Caption rect without Passthrough | Broken drag/clicks |
| Feature implemented in one view only | 4-view parity defect |
| Hard-coded UI strings | Localization |
| Mass reformatting / comment translation / file moves | Unreviewable diffs in a dirty tree |

## Review checklist

- [ ] 4 views + split/quad panes + multiple tabs + tear-off window considered.
- [ ] Keyboard path works with Korean/Japanese/Chinese IME active (ScanCode fallbacks).
- [ ] Light/dark theme and high contrast still correct (cached brushes refreshed).
- [ ] DPI scaling (100/150/200%) for any geometry/regions.
- [ ] Dev identity unchanged (`tests/test-oriel-profile.ps1`).
- [ ] Targeted `tests/test-*.ps1` that reference the touched methods/XAML pass.
- [ ] `build-dev.ps1` result stated (ran/passed, or not run + reason).

## Tests to add

- Pure logic: MSTest in `OrielFolder.Tests` or a PowerShell `Add-Type` script.
- XAML structure / wiring invariants: `[xml]` + XPath script in `tests/`.
- Performance: counting fakes (DP write counters) as in `test-detail-row-layout-cost.ps1`.
- Real interaction: FlaUI in `OrielFolder.UITests` (needs running Dev app — ask before running).
