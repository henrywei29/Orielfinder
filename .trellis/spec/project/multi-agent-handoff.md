# Multi-Agent Handoff (Codex · Claude Code · Antigravity)

> Three tools take turns on the **same local working tree** at different times. Work may be
> handed over unfinished and uncommitted, including when an agent runs out of quota mid-task.
> No agent can see another agent's conversation — the files below are the only shared memory.

## One source of truth per kind of state

| State | Where | Rule |
|-------|-------|------|
| Which task is active | Task status in `.trellis/tasks/<TASK_ID>/task.json` (`task.py list` → `in_progress`) + the newest `handoff.md` | The Trellis "current task" pointer is **per AI session** (`.trellis/.runtime/sessions/<platform>_<session>.json`) and is **not** shared between agents. Every new session must pick the task explicitly with `task.py start <TASK_ID>` |
| Task goal & acceptance criteria | `.trellis/tasks/<TASK_ID>/prd.md` | Keep the original objective; the user changes it, not the agent |
| **Live task state / handoff** | `.trellis/tasks/<TASK_ID>/handoff.md` | `## Current State` is replaced at each handoff; `## Checkpoint Log` is append-only |
| Session history (who did what, when) | `.trellis/workspace/developer/journal-N.md` via `add_session.py` | One session record per agent session, title prefixed with the agent name |
| Durable conventions & lessons | `.trellis/spec/**` | Failed approaches that are generally useful also go here |
| Legacy pre-Trellis records | `agent-handoffs/` (`CURRENT_TASK.md`, `history/*.md`) | **Read-only history.** Do not write new state there; do not delete it |

`TASK_ID` = the Trellis task directory name (e.g. `00-bootstrap-guidelines`; new tasks use the
name `task.py create` generates). Do not keep a second, independent "current task" file.

## Start-of-session protocol

1. **Locate & identify.** Confirm the folder is `D:\project\Orielfolder\Orielfolder` (Git root) and you
   know which agent you are. Read `AGENTS.md` (Claude Code: via `CLAUDE.md`) and
   `.trellis/spec/project/index.md`, then the layer indexes you will need.
2. **Single writer.** Confirm no other agent, editor, watcher, or background job is still writing.
   If unknown, ask the user before writing anything.
3. **Load Trellis context.** `python ./.trellis/scripts/get_context.py` (Claude Code/Codex hooks
   inject this automatically; Antigravity: run the `start` workflow). Then `python ./.trellis/scripts/task.py list`.
   A fresh session normally shows `CURRENT TASK: (none)` even when another agent was mid-task — find the
   `in_progress` task whose `handoff.md` was updated most recently, confirm with the user if more than one
   qualifies, then `python ./.trellis/scripts/task.py start <TASK_ID>` so hooks inject that task's context.
4. **Read the active task.** `prd.md` → `handoff.md` (Current State **and** the latest Checkpoint Log
   entries) → `design.md`/`implement.md` if present. Read the latest journal session.
   **No `in_progress` task?** Read the latest journal session and the newest archived handoff
   (`task.py list-archive` → `.trellis/tasks/archive/<YYYY-MM>/<TASK_ID>/handoff.md`), then create a new task
   for the user's request (`task.py create ...`) and start it. `agent-handoffs/` (pre-Trellis) is background only.
5. **Inspect Git read-only** (see git-workspace-safety.md): `git status --porcelain=v1 -uall`,
   `git diff --cached --name-status`, `git diff --name-status`, untracked files, `git log --oneline -5`.
   Save a status snapshot outside the repo.
6. **Reconcile notes with reality.** Handoff notes may be stale or incomplete — a quota cut can stop an
   agent before it writes notes. Compare the files on disk with `handoff.md`: classify each item as
   *complete / partial / unverified / broken*. Missing notes ≠ zero progress.
7. **Do not repeat failed approaches.** Read `Failed approaches` in `handoff.md`; retry one only with new
   evidence and say why.
8. **Confirm and continue** with the smallest safe next step. If intent cannot be reconstructed from files
   and the user's newest message, ask instead of inventing requirements. The user's newest explicit
   request wins over older handoff text; flag conflicts.

## Continuous checkpoints

Append a `## Checkpoint Log` entry to `handoff.md` after each meaningful milestone, before risky edits,
and whenever you suspect you may be interrupted. Keep it short and factual. A checkpoint helps recovery
but cannot be relied on — that is why step 6 exists.

## End-of-session / handoff protocol

1. Stop or finish all file-writing jobs; leave no background writers.
2. Re-run `git status --porcelain=v1 -uall`, diff it against the start snapshot, and review this task's diffs
   (staged, unstaged, untracked).
3. Run focused checks if feasible; record exact commands and results (build-test-verification.md levels).
4. Rewrite `handoff.md → ## Current State` using the template below and append a Checkpoint Log entry.
5. Record the session: `python ./.trellis/scripts/add_session.py --title "[<Agent>] <summary>" --commit - --summary "..." --change "..." --test "..." --next-step "..."`
   (`session_auto_commit: false` → writes files only; never commits).
6. Tell the user: complete / partial / blocked, what was verified, and what needs their approval.
7. Only when the user says the task is finished: `task.py finish`, then (with approval) `task.py archive <TASK_ID>`.

## `handoff.md` template

```markdown
# Handoff — <TASK_ID>

## Current State
- **TASK_ID:** <dir name>            - **Updated:** YYYY-MM-DD HH:MM (local)
- **Agent:** Codex | Claude Code | Antigravity
- **Task & phase:** <one line>; Trellis phase <1 plan | 2 implement | 3 check/finish>; status <in progress | blocked | verification needed | complete>
- **Git at handoff:** branch, HEAD <sha>; status summary (staged N / unstaged N / untracked N)

### Completed work
### Files modified by this task   (path — purpose; mark created/modified)
### Unfinished modifications       (what is half-done, where, exact next edit)
### Uncommitted changes & ownership
- This task: <files>
- Pre-existing / other agents / user: <e.g. Span→OrielFolder rename (654 staged renames) — not ours, untouched>
### Tests actually executed        (command → result; "not run" with reason)
### Failed approaches              (what, evidence, why it failed — do not retry without new evidence)
### Known risks
### Next step                      (one concrete action)
### Needs user approval            (commits, deletes, installs, identity/signing changes, AGENTS.md edits ...)

## Checkpoint Log
- YYYY-MM-DD HH:MM [Agent] <short factual entry>
```

## Platform entry points

| Agent | Instruction entry | Context loading | Trellis commands / skills |
|-------|-------------------|-----------------|---------------------------|
| Claude Code | `CLAUDE.md` → `@AGENTS.md` | `.claude/settings.json` hooks: `session-start.py` (startup/clear/compact), `inject-spec-context.py` (spec `paths:` injection), `inject-workflow-state.py` | `/trellis:continue`, `/trellis:finish-work`; skills in `.claude/skills/trellis-*` |
| Codex | `AGENTS.md` | `.codex/hooks.json` → `.codex/hooks/session-start.py` etc. (only if Codex hooks are enabled/trusted; otherwise run `get_context.py` manually) | skills in `.agents/skills/trellis-*` (`trellis-start`, `trellis-continue`, `trellis-finish-work`); sub-agents `.codex/agents/*.toml` |
| Antigravity | `AGENTS.md` — **auto-loaded (verified 2026-11-06) only when the Antigravity project is opened at the Git root `D:\project\Orielfolder\Orielfolder`**. Antigravity searches the project root and parent dirs, never subfolders; opening the outer `D:\project\Orielfolder` loads nothing | **No session-start hook**: run `.agent/workflows/start.md` steps manually | workflows `.agent/workflows/{start,continue,finish-work}.md`; skills `.agent/skills/trellis-*` |

These platform files are generated by Trellis (`.trellis/.template-hashes.json`); project rules live in
`.trellis/spec/`, so `trellis update` cannot overwrite them.
