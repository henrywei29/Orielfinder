# Handoff — 11-08-details-header-columns

## Current State
- **TASK_ID:** 11-08-details-header-columns
- **Updated:** 2026-11-08 (local)
- **Agent:** Claude Code
- **Task & phase:**
  - Implemented and checked in Dev.
  - Committed in `d0ff46e` on `rename/orielfolder`. Not pushed.
  - Completed and archived.

### What changed
- **New pure helper** `Helpers/DetailsColumnLayout.cs`:
  - which columns can be toggled
  - the label key for each column
  - "keep at least one column visible"
  - the resize math
  - when the filler column is needed
  - parsing and saving widths
  - Tests: `DetailsColumnLayoutTests`.
- **`Views/DetailsModeView.Columns.cs`:**
  - `GridSplitter` is replaced by our own separators: an 8-px hit area with a 1-px line and a resize cursor.
  - Explorer-style dragging:
    - The first drag fixes Name, which stretched until then, to its current width.
    - A trailing filler column takes the free space.
  - Widths are saved in `DetailsViewSort.ColumnWidths` and broadcast to the other Details views.
  - The header is kept in sync with the list's horizontal scroll.
- **`Views/DetailsModeView.xaml`:**
  - `ColumnSeparatorLineStyle` (theme-aware).
  - The header is wrapped in `HeaderScroller`.
  - The ListView scrolls horizontally.
- **`Views/DetailsModeView.xaml.cs`:**
  - The header menu is built from the column order, with localized labels, and every column can be toggled.
  - `ToggleColumnVisibility` is generic. Hidden columns are simply left out of the header grid; the old zero-width trick is gone.
  - New saved flags: `NameColumnVisible`, `CloudStatusColumnVisible`, `ColorTagColumnVisible`, `GitColumnWanted`.
  - Git: the menu entry sets a preference. The column still appears only in Git folders, and only when Git integration is on.
- **Localization:** new keys `ShowColumns` and `ColumnGitStatus`, in all languages including Polish.
- **Spec:** `layout-window-performance.md`, Grid sizing section.

- **Split view toolbar** (`MainWindow.WorkspaceLayout.cs`): at the user's request ("把网格平铺和添加窗格一样大"), the arrangement ComboBox is now a Button with the same padding as "Add pane". It shows the current arrangement and opens a MenuFlyout of radio items. A ComboBox reserves a fixed arrow column, so it can never be as narrow as the button. MinWidth follows the Add pane button. Measured in Dev: both 74×25.

### Verification
- `build-dev.ps1` builds with 0 errors.
- Unit tests: 1259 passed, 0 failed, 3 skipped.
- Dev runtime check (`D:\codex\header-demo\demo.ps1`, `demo2.ps1`) with real mouse input (absolute `mouse_event` moves; `SetCursorPos` does not produce pointer moves in WinUI):
  - The menu is in Chinese and every column is checkable.
  - Hiding 状态: every column to its right moves left by 74 px (72 px column + 2 px gap).
  - Dragging the 状态|颜色标签 separator +120: 状态 72 → 192, and 颜色标签, 修改日期, 类型 and 大小 each move +120.
  - Dragging the separator left of 类型 +80: 修改日期 200 → 280, and 类型 and 大小 move together.
  - Overflow: the list scrolls horizontally (scrollbar shown) and the header stays aligned with the rows.
  - Undoing both drags restores the widths, and there is no horizontal scroll left.
- Not run: the PS7-only static scripts (`test-detail-row-layout-cost.ps1`, `test-detail-header-layout-cost.ps1`). The methods they extract (`ApplyCellWidths`, `OnHeaderLayoutUpdated`) are unchanged.
- **Side effect of the test:** the Dev settings now hold saved widths with a fixed Name width (`Name=1322`). Name no longer stretches with the window in the user's Dev.

### Next step
None. If Name should stretch again in the user's Dev, the saved `DetailsViewSort.ColumnWidths` has to be cleared (there is no reset option yet).

## Checkpoint Log
- 2026-11-08 [Claude Code] Implemented: column toggles + localization, visible separators, Explorer-style resize with horizontal scroll. Tests: 1259 passed. Checked in Dev with screenshots.
- 2026-11-08 [Claude Code] Split-view arrangement selector matched to the Add pane button (both 74×25 in Dev; menu opens with 水平/垂直/网格平铺). Picking an item was not clicked in the test; the handler logic is unchanged from the ComboBox version.
- 2026-11-08 [Claude Code] User approved ("一起提交"); committed `d0ff46e` (header + split-view toolbar); archived.
