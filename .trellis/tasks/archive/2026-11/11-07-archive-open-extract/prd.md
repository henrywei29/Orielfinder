# Open zip in Details/List/Icon + toolbar extract button (Dev)

## Goal

The user reported that double-clicking a .zip in Details view opened it in Windows Explorer, while Miller Columns opened it inside the app.
Zips should open like ordinary folders in every view. The toolbar should get an extract button to the left of Cut.
Dev only.

## Requirements

- **R1:** In Details, List and Icon views, opening a browsable archive (.zip) goes into it in the current tab (`archive://` path), the same as Miller Columns. Other archive types keep their old behavior.
- **R2:** Up or Backspace inside an archive goes to the parent folder in the archive, and from the archive root to the folder that holds the zip.
- **R3:** A toolbar "Extract here" button to the left of Cut.
  - It is enabled when an extractable archive is selected.
  - It reuses the context menu's Extract Here, which picks a unique destination folder name.

## Acceptance Criteria

- [x] R1–R3 are implemented.
- [x] The build has 0 errors. Unit tests: 1235 passed, including 20 new ones in `ArchiveExtractAndNavigationTests`.
- [x] The implementing session checked it in Dev. Screenshots: `D:\codex\zip-shot*.png`.
- [x] Re-checked in Dev with 7 screenshots sent to the user (`D:\codex\zip-demo-run`). The user approved; committed in `27e72ca`.
