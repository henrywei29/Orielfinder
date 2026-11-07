# Faster folder open and new tabs (Dev)

## Goal

Opening OrielFolder Dev with a folder (cold start) should show the window and rows sooner. Opening more folders as new tabs in a running window should stall less.
Dev only (`.trellis/spec/project/dev-release-boundary.md`).
Measure first and report before/after numbers.

## Requirements

- R1: On a cold start with a folder argument, the window appears as soon as the content is ready, with no 1–2 s delay while cloaked.
- R2: Opening a folder as a new tab in a running window causes fewer and shorter UI stalls (frames of 50 ms or more, GC pauses).
- R3: No visible behavior change in rows, the Git column (M/?), tag dots, the font-scale live update, or density settings.
- R4: Unit tests cover the new helpers and template rules. `build-dev.ps1` builds with 0 errors. Dev is started with `start-dev.ps1`.

## Acceptance Criteria

- [x] Cold start System32: the window appears at 2.35 s instead of 4.52 s (median). Committed in `7ac7541`.
- [x] 6 System32 tabs in a running Dev:
  - Total stall: 12.4–13.4 s → 5.0–5.2 s.
  - GC pause: 6.7–7.2 s → 2.1–2.3 s.
- [x] Cold start System32:
  - Rows created: 208 → 159.
  - Stall in the first 3 s: 849 → 454 ms.
- [x] Unit tests pass. 1215 passed, 0 failed, 3 skipped before the zip task; 1235 passed with it.
- [x] The user approved; part 2 committed in `27e72ca`.

## Notes

- Measurement scripts and raw results are in `D:\codex\perf-open\` (see `handoff.md`).
- The findings are also in `.trellis/spec/frontend/layout-window-performance.md`, in the GC / XAML element count bullets.
