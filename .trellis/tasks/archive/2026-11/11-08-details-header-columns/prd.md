# Details header: toggle all columns, visible separators, Explorer-style resize (Dev)

## Goal

The user's request (2026-11-08, with a screenshot of the Details header right-click menu):
- "把这一栏的所有选项都变成可开关的，让选项有中文翻译" — make every option in the menu toggleable, and translate the options into Chinese.
- "让每一点列之间的分隔点更明显一些" — make the separators between columns more visible.
- "当调整分隔大小的时候带动后面的所有项，像资源管理器那样" — when resizing at a separator, everything after it moves, like in Windows Explorer.

Dev only.

## Requirements

- **R1 Menu:** "Show columns" lists every column except Location, which only appears in search results.
  - The columns are Name, Status, Color tag, Date modified, Type, Size and Git status.
  - Each one can be toggled. At least one column always stays visible.
  - All menu text is localized, including the Group by submenu.
- **R2 Separators:** Each separator between two columns is a visible line. Hovering it highlights the line, and the cursor shows a resize arrow.
- **R3 Explorer-style resize:**
  - Dragging a separator changes only the width of the column on its left. Every column to its right keeps its width and moves.
  - When the columns are wider than the view, the list scrolls horizontally and the header follows.
- **R4 Persistence:** Column visibility and widths are kept across views and restarts.

## Acceptance Criteria

- [x] R1–R4 implemented. Build: 0 errors. Unit tests: 1259 passed, 0 failed, 3 skipped (24 new in `DetailsColumnLayoutTests`).
- [x] Checked in Dev with real mouse input. Screenshots are in `D:\codex\header-demo`.
- [x] The user saw the Dev screenshots and approved the commit ("一起提交"); committed in `d0ff46e`.
