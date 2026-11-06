---
name: WinUI UI directory structure
description: Where OrielFolder UI code lives and how MainWindow / ViewModels are split into partials
paths:
  - "src/OrielFolder/OrielFolder/ViewModels/**"
  - "src/OrielFolder/OrielFolder/Controls/**"
---

# UI Directory Structure

```
src/OrielFolder/OrielFolder/
├── App.xaml / App.xaml.cs            # resources, DI (ConfigureServices), window registry, crash hooks
├── Program.cs                        # custom Main (DISABLE_XAML_GENERATED_MAIN), single instance
├── MainWindow.xaml                   # ~2,100 lines: title bar, tabs, panes, toolbars, status bar
├── MainWindow.xaml.cs                # ~7,100 lines core window logic
├── MainWindow.<Area>.cs              # partials: TabManager, NavigationManager, KeyboardHandler,
│                                     #   DragDropHandler, FileOperationHandler, SettingsHandler,
│                                     #   SplitPreviewManager, WorkspaceLayout, WorkspaceRouting,
│                                     #   PaneSizing, OuterColumnResize, MillerColumnSizing,
│                                     #   ShelfHandler, RecycleBinHandler, CommandPaletteHandler,
│                                     #   ListaryIntegration, FolderTags, ColorLabelCategories
├── FileSystemItemTemplateSelector.cs, SidebarItemTemplateSelector.cs
├── Controls/AddressBarControl.xaml(.cs)
├── Views/
│   ├── DetailsModeView.xaml(.cs) + .Columns.cs + .InlineTree.cs
│   ├── ListModeView.xaml(.cs) + .InlineTree.cs
│   ├── IconModeView, HomeModeView, RecycleBinModeView, LogModeView
│   ├── SettingsModeView.xaml(.cs) + .ConfigurationBackup.cs + .FolderTags.cs + .ProductIcon.cs
│   ├── PreviewPanelView, QuickLookWindow, DocumentPreviewView(.Interactions), OcrTextPanel
│   ├── SettingsWindow.cs, WorkspaceFolderPane.cs, OnboardingWindow
│   ├── Controls/ (FileOperationProgressControl)   Dialogs/
├── ViewModels/
│   ├── MainViewModel.cs + .TabManagement / .ViewMode / .MultiPane / .FileOperations
│   ├── ExplorerViewModel.cs (~1,900 lines, Miller engine) + .InlineTree / .ColorLabels
│   ├── FolderViewModel, FileViewModel, FileSystemViewModel (base)
│   └── PreviewPanelViewModel, QuickLookViewModel, BatchRenameViewModel, dialogs, DetailsColumnWidths
└── Assets/ (Styles/, Fonts/, ProductIcons/, Onboarding/ (WebView2), PdfViewer/ (pdf.js))
```

## Rules

- Extend the existing partial that owns the area; create a new `MainWindow.<Area>.cs` partial only for a
  genuinely new area. Do not move code between partials as a side effect of a feature.
- View code-behind may contain view-specific interaction (focus, scrolling, container callbacks).
  Navigation/business state belongs in ViewModels or Services.
- Per-view variants follow `<View>.<Area>.cs` (e.g. `DetailsModeView.InlineTree.cs`).
- Do not rename/move files under `src/OrielFolder/` as part of a feature — the tree is mid-rename and
  `tests/*.ps1` reference exact paths.
