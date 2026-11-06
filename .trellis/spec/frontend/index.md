# Frontend Layer — WinUI 3 UI (Views, ViewModels, MainWindow, XAML)

> In OrielFolder "frontend" means the **WinUI 3 desktop UI**: `MainWindow*.cs`, `*.xaml`,
> `Views/`, `Controls/`, `ViewModels/`, converters, and WebView2 assets. There is no web
> frontend, no React, no TypeScript. Template topics are mapped to their WinUI equivalents.

Applies to: `src/OrielFolder/OrielFolder/{MainWindow*, App.xaml*, Views/**, Controls/**, ViewModels/**, Assets/**}`.

## Guidelines Index

| Guide | Description |
|-------|-------------|
| [Directory Structure](./directory-structure.md) | Where UI code lives, partial-class layout |
| [Component Guidelines](./component-guidelines.md) | XAML views/controls, bindings, 4-view parity, split-pane parity |
| [Event & Dispatcher Lifecycle](./hook-guidelines.md) | (template "hooks") event subscription, DispatcherQueue, async void, guards |
| [State Management](./state-management.md) | MVVM state, tabs, panes, view modes, settings persistence |
| [Type Safety](./type-safety.md) | C# nullable, records/DTOs, path/string comparison |
| [Layout, Window & Performance](./layout-window-performance.md) | Grid/sizing, title bar & window dragging, pane dividers, virtualization, perf budgets |
| [Quality Guidelines](./quality-guidelines.md) | Forbidden patterns, review checklist, required tests |

## Pre-Development Checklist

- [ ] Read `../project/index.md` (Git safety + handoff) first.
- [ ] Identify every view the change affects: **Miller, Details, List, Icon** (and Home/Settings if relevant), left/right/quad panes, tabs.
- [ ] Read the existing partial (`MainWindow.<Area>.cs`) and ViewModel partial before adding code.
- [ ] Find the `tests/test-*.ps1` scripts that assert on the code you change (`grep` the method name in `tests/`) — many lock exact XAML attributes and method shapes.
- [ ] For layout/scroll/selection work read [Layout, Window & Performance](./layout-window-performance.md).

## Quality Check

- [ ] Behavior works in all 4 views and in split/quad panes, or the limitation is documented.
- [ ] No new DP writes in hot paths (container callbacks, scroll, selection) without a measured reason.
- [ ] Event subscriptions balanced (`-=` before `+=`, unsubscribe on cleanup).
- [ ] Deferred UI callbacks check `_isClosed` / cleanup guards.
- [ ] Relevant `tests/test-*.ps1` pass; `build-dev.ps1` compiles (or state why not run).
