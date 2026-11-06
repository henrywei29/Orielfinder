# State Management (MVVM)

## Stack

CommunityToolkit.Mvvm 8.x: `[ObservableProperty]` (~190 uses) with
`partial void On<Name>Changed(...)` for side effects, `[RelayCommand]` / `Helpers/RelayCommand.cs`.
Services are singletons from the DI container in `App.xaml.cs`.

## Where state lives

| State | Owner |
|-------|-------|
| Tabs, active tab, view mode per pane, split/quad layout, active pane | `MainViewModel` (+ `.TabManagement`, `.ViewMode`, `.MultiPane`) |
| Miller columns, selection chain, navigation history, inline tree | `ExplorerViewModel` (one per pane/tab) |
| Folder children, sorting, grouping | `FolderViewModel` |
| Preview / Quick Look | `PreviewPanelViewModel`, `QuickLookViewModel` |
| Persistent settings | `SettingsService` → `AppSettings.LocalSettings` (packaged `ApplicationData` LocalSettings; standalone store unfinished) |
| Workspaces / tabs session | `WorkspaceService` (`workspaces.json`), `TabStateDto`, `WorkspaceDto`, `WorkspacePaneState` |
| Folder tags / color labels | `FolderTagService` (`folder-tags.json`), no `desktop.ini` writes |
| Shelf | `ShelfService` |

## Flows to preserve

- **Miller engine**: `Columns` is an `ObservableCollection<FolderViewModel>`. Selecting a folder in column N
  removes columns after N and adds N+1 (debounced); selecting a file trims after N. Replace at index instead
  of remove+add to avoid flicker. `NavigateTo` (full reset), `NavigateToPath` (segment-parallel load),
  `NavigateToSegment` (breadcrumb truncate).
- **Tab switch** (`SwitchToTab`): save active tab state → deactivate old columns → set backing fields
  directly (avoid PropertyChanged cascades) → activate new tab → MainWindow swaps per-tab panels
  (Show/Hide dictionaries keyed by tab id) → resubscribe → focus. Guarded by `IsSwitchingTab`.
- **View mode restore priority**: `_lastClosedViewMode` > `_viewModeBeforeHome` > saved preference.
  Settings/ActionLog open as singleton special tabs (Explorer = null, excluded from session save).
- **Tear-off / multi-window**: `TabStateDto` + `App.RegisterWindow/UnregisterWindow`; last window closing exits.

## Rules

- Do not add parallel state stores for something an existing service owns.
- Settings keys are persisted user data: never rename/remove a key without a migration, and never point
  the Dev build at the installed release app's package family/data (see `test-oriel-profile.ps1`).
- Configuration backup (`ConfigurationBackupService`) must include any new persisted setting the user would
  expect to migrate.
