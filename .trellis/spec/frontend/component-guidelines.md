---
name: WinUI component guidelines
description: XAML view/control conventions, bindings, 4-view and split-pane parity for OrielFolder
paths:
  - "src/OrielFolder/OrielFolder/**/*.xaml"
  - "src/OrielFolder/OrielFolder/Views/**"
  - "src/OrielFolder/OrielFolder/Controls/**"
---

# Component (View / Control) Guidelines

## Bindings

- Both `{x:Bind}` (~420 uses) and `{Binding}` (~510 uses) exist. Prefer `{x:Bind}` in new
  Page/UserControl/Window XAML (compile-time checked, `Mode=OneWay` for display, `TwoWay` for editable).
  Keep `{Binding}` where the surrounding template/resource dictionary already uses it.
- TextBoxes that commit on Enter (rename, address bar) need `UpdateSourceTrigger=PropertyChanged`
  — the TwoWay default is `LostFocus`.
- **Never assign a value in code to a DependencyProperty that has a `{Binding}`** (e.g.
  `border.BorderBrush = brush`): it permanently destroys that binding. Toggle the ViewModel property instead.
- `{ThemeResource}` on non-FrameworkElement `DependencyObject`s (e.g. `BoolToBrushConverter`) does not
  refresh on theme change; update them manually in the theme-change path.

## 4-view parity (critical for a file manager)

Any change to view behavior — drag & drop, rename, keyboard shortcuts, context menu, selection,
type-ahead, bindings, inline tree — must be applied and checked in **Miller, Details, List and Icon**.
Working in one view only is a defect. If parity is intentionally deferred, write it in the task `handoff.md`.

## Split / multi-pane parity

- Every visual element on the left pane must exist on the right pane template too, including
  `Loaded`/`Unloaded` handlers (e.g. floating path indicator). Quad-pane workspace uses
  `WorkspaceFolderPane` + `MainWindow.WorkspaceLayout/WorkspaceRouting`.
- In split view use the active pane's mode:
  `(IsSplitViewEnabled && ActivePane == ActivePane.Right) ? RightViewMode : CurrentViewMode`.
- Split view and Miller inline preview are mutually exclusive: enabling split must hide inline preview.
- Toolbar separators must share the `Visibility` of the buttons they separate (no double lines).

## Context menus, keyboard, IME

- `MenuFlyoutItem.AccessKey` set before the item is in the visual tree crashes natively (E_INVALIDARG);
  store accelerators in `Tag` and use the custom access-key path.
- Shell menu text with CJK accelerators like `보내기(&N)` — do not strip CJK brackets.
- OEM keys under Korean/Japanese/Chinese IME: add `e.KeyStatus.ScanCode` fallbacks (backtick 41, quote 40, comma 51).
- Type-ahead: Latin via `CharacterReceived`, CJK via `TextChanged` on a hidden TextBox (800 ms buffer).
- `IsRenaming` check comes first in global key handling; after inline rename, focus returns to the
  list container, not the TextBox; `_justFinishedRename` prevents the commit-Enter from opening the item.
- Global handlers registered with `handledEventsToo: true` must check `if (!e.Handled)`.

## Focus

- Save the active column/pane index **before** showing a modal dialog and use the saved value after.
- Pane activation calls `FocusColumnAsync(index, autoSelect: false)`; `true` auto-selects and cascades columns.
- Container access after collection changes: `DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ...)`.

## Strings & icons

- User-visible text goes through `LocalizationService` / `LocalizationData` (10 languages per tuple).
  New strings must be added for all languages in the tuple; do not hard-code UI text.
- Icon glyphs via `RemixIconHelper` / `PhosphorIconHelper` / `TablerIconHelper` and the bundled fonts.
- Product name in new UI strings: **OrielFolder** (some existing strings still say "Span" — see
  `../project/rename-migration-status.md`; change them only in an approved task).
