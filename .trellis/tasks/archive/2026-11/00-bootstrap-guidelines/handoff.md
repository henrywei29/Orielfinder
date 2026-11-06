# Handoff — 00-bootstrap-guidelines

## Current State
- **TASK_ID:** 00-bootstrap-guidelines            - **Updated:** 2026-11-06 10:55 (local)
- **Agent:** Claude Code
- **Task & phase:** Adapt Trellis spec / task / workspace docs to the current OrielFolder working tree and set up the
  cross-agent handoff protocol. Trellis phase 3 (check). Status: **complete — finished and archived with user approval**. AGENTS.md option A applied.
- **Git at handoff:** branch `main`, HEAD `59a4af3`. Pre-existing: 654 staged renames (259 `R`, 395 `RM`), 100 ` M`,
  124 untracked at session start. This session added only untracked Trellis files (see below).

### Completed work
- Read-only Git inspection (index vs working tree vs untracked), snapshot saved outside repo.
- Analysed current code: `OrielFolder.sln`, all 7 projects, `AppIdentity.cs`, `Package.appxmanifest`, csproj, build/start/package
  scripts, tests, README/CONTRIBUTING, remaining `Span` references, title-bar/pane-divider/perf code and tests.
- Wrote spec layer `.trellis/spec/project/` and filled `backend/` + `frontend/` (WinUI-mapped) + updated `guides/index.md`.
- Set `session_auto_commit: false` in `.trellis/config.yaml` (Trellis default `true` would auto-commit journal/archive).
- Recovered the pre-Trellis OrielFolder `AGENTS.md` (gitignored → not in Git) from a Codex session log; wrote merge proposal.
- Applied AGENTS.md merge option A (user-approved): protocol appended below the unchanged Trellis block; backup in
  `D:\codex\Orielfolder-dev\agents-md-backup-20261106\` (`AGENTS.md.trellis-only`, `CLAUDE.md`).
- Activated this task for the Claude Code session (`task.py start`), which set `task.json.branch = "main"`.

### Files modified by this task
Created:
- `.trellis/spec/project/{index,architecture,directory-structure,rename-migration-status,build-test-verification,git-workspace-safety,multi-agent-handoff}.md`
- `.trellis/spec/frontend/layout-window-performance.md`
- `.trellis/tasks/00-bootstrap-guidelines/{handoff.md, AGENTS.pre-trellis.recovered.md, AGENTS.merge-proposal.md}`
Modified (all were untracked Trellis template files):
- `.trellis/spec/backend/{index,directory-structure,database-guidelines,error-handling,logging-guidelines,quality-guidelines}.md`
- `.trellis/spec/frontend/{index,directory-structure,component-guidelines,hook-guidelines,state-management,type-safety,quality-guidelines}.md`
- `.trellis/spec/guides/index.md`, `.trellis/workspace/index.md`, `.trellis/config.yaml`
- `.trellis/tasks/00-bootstrap-guidelines/{prd.md, task.json}`
- `.trellis/workspace/developer/{journal-1.md, index.md}` (session records via `add_session.py`)
- `AGENTS.md` (gitignored; appended OrielFolder protocol outside the Trellis markers). `CLAUDE.md` unchanged.
Runtime (gitignored): `.trellis/.runtime/sessions/claude_6db947b6-....json`

### Unfinished modifications
- None.

### Uncommitted changes & ownership
- This task: only the Trellis files listed above (all untracked; nothing staged).
- Pre-existing, NOT ours, untouched: the whole Span → OrielFolder rename (staged + unstaged), README/scripts/tests edits,
  `agent-handoffs/`, `tools/Repair-StaleFolderOpen.ps1`, `tests/test-stale-folder-open.ps1`, `tests/test-installer-powershell.ps1`,
  `installer/Initialize-InstallerPowerShell.ps1`, `.gitattributes`, platform dirs `.claude/ .codex/ .agent/ .agents/`.

### Tests actually executed
- No application build or test run (docs-only task; not needed and `build-dev.ps1` kills Dev processes).
- `get_context.py --mode packages` → spec layers `backend, frontend, project` discovered.
- Spec frontmatter parsed with Trellis `parse_spec_frontmatter` for every spec file → no errors.
- Git status before/after comparison → see Checkpoint Log.

### Failed approaches
- Searching Git (`git log --all -- AGENTS.md`) for the old protocol: only upstream SPAN versions exist; file is gitignored.
- Codex 2026-11-05 logs held only truncated copies; the full text came from the 2026-10-21 rollout file.

### Known risks
- Antigravity may not auto-load `AGENTS.md`; until confirmed, start Antigravity sessions with an explicit
  "read AGENTS.md and .trellis/spec/project/index.md" and run `.agent/workflows/start.md`.
- Codex hooks (`.codex/hooks.json`) only run if enabled/trusted in Codex; otherwise run `get_context.py` manually.
- Trellis current-task pointer is per session — every new agent session must `task.py start` explicitly.
- `trellis update` may rewrite generated platform files (not the spec); project rules are kept in `.trellis/spec/`.

### Next step
Task archived. Next agent: confirm Antigravity loads AGENTS.md; create a new Trellis task for the next user request.

### Needs user approval
1. Any future fix listed in `.trellis/spec/project/rename-migration-status.md` (each needs its own task).
2. Whether/when to commit the Trellis files (nothing staged or committed by this session).

## Checkpoint Log
- 2026-11-06 10:05 [Claude Code] Git snapshot taken (878 status lines: 259 R, 395 RM, 100 M, 124 ??). Read-only analysis started.
- 2026-11-06 10:15 [Claude Code] Recovered pre-Trellis AGENTS.md from Codex log; spec project layer written.
- 2026-11-06 10:25 [Claude Code] backend/frontend specs filled; session_auto_commit=false; task activated for this session.
- 2026-11-06 10:35 [Claude Code] Verification: status diff before→after = only 11 new `??` Trellis files; `git diff --cached`
  sha1 3793885a… and tracked `git diff` sha1 1315add6… identical before/after; HEAD 59a4af3, branch main, no stash;
  AGENTS.md / CLAUDE.md hashes & mtimes unchanged. Journal session 1 recorded with --no-commit.
- 2026-11-06 10:45 [Claude Code] User approved option A. Backed up AGENTS.md/CLAUDE.md to D:\codex\Orielfolder-dev\agents-md-backup-20261106\;
  appended protocol after TRELLIS:END (Trellis block byte-identical, cmp OK); fixed `task.py current` -> `task.py list/start`.
  Re-verified: Git status unchanged, staged sha1 3793885a…, tracked diff sha1 1315add6… identical; AGENTS.md still gitignored.
- 2026-11-06 10:55 [Claude Code] User approved finishing. Ran `task.py finish` and `task.py archive 00-bootstrap-guidelines` (session_auto_commit false -> files moved only, no git).
- 2026-11-06 11:10 [Claude Code] Post-archive: user verified Antigravity auto-loads AGENTS.md when its project is opened at the Git root (answered 7 rules, rule 7 D:\codex, handoff.md path without reading files). Outer folder D:\project\Orielfolder loads nothing. Recorded in spec/project/multi-agent-handoff.md.
