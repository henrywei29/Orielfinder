# AGENTS.md merge proposal — APPLIED (option A, user-approved 2026-11-06)

Status: applied to `AGENTS.md` on 2026-11-06 by Claude Code. Backup of the pre-merge (Trellis-only) file:
`D:\codex\Orielfolder-dev\agents-md-backup-20261106\AGENTS.md.trellis-only` (+ `CLAUDE.md`, unchanged).

## 1. What happened

| Item | Finding |
|------|---------|
| Git history | `AGENTS.md`, `CLAUDE.md`, `.claude/`, `.agents/` are in `.gitignore`. Git has **no** copy of the OrielFolder `AGENTS.md`. The `AGENTS.md`/`CLAUDE.md` versions in Git (`ce38225`, `b3d720e`, on remote branches/tags) are the **upstream SPAN Finder** files (Korean/English, `src/Span/...`), not this project's protocol. |
| Recovered copy | Full text recovered from a Codex session log (tool output of reading the file on 2026-11-05) → `AGENTS.pre-trellis.recovered.md` in this folder. Codex logs from 2026-11-05 09:49 and 15:02 contain truncated copies with the same text. |
| Overwrite | `trellis init` on 2026-11-05 16:41 replaced the file with only the `<!-- TRELLIS:START --> ... <!-- TRELLIS:END -->` block. |
| `CLAUDE.md` | Contains only `@AGENTS.md` (dated 14:24, before Trellis). Not overwritten. No change needed. |

## 2. Diff summary (old protocol → current file)

| Old section | In current AGENTS.md? | Where it lives now |
|-------------|----------------------|--------------------|
| Purpose & scope (3 agents, same repo, different times; respond in user's language) | ❌ lost | `.trellis/spec/project/multi-agent-handoff.md` intro |
| Non-negotiable rules 1–6 (single writer, preserve all work, no auto commits, respect scope, protect sensitive/release assets, evidence over summaries) | ❌ lost | `.trellis/spec/project/git-workspace-safety.md` (expanded) |
| Start-of-task checklist | ❌ lost | `multi-agent-handoff.md → Start-of-session protocol` |
| Development rules (continue partial work, smallest change, review diffs, discover framework, report real tests, measured vs hypothesis, don't silently fix conflicts) | ❌ lost | `project/build-test-verification.md`, layer quality guidelines |
| Continuous checkpoints → `agent-handoffs/CURRENT_TASK.md` | ❌ lost | **changed**: `.trellis/tasks/<TASK_ID>/handoff.md` Checkpoint Log |
| Recovery when previous agent stopped mid-task | ❌ lost | `multi-agent-handoff.md` step 6 |
| Handoff → `agent-handoffs/history/` + `HANDOFF_TEMPLATE.md` | ❌ lost | **changed**: `handoff.md` Current State + journal via `add_session.py` |
| Task state & completion, Definition of done | ❌ lost | `multi-agent-handoff.md`, `project/index.md` Quality Check |
| Trellis entry block | ✅ present | keep unchanged |

Only intentional semantic change: the live handoff location moves from `agent-handoffs/` to the Trellis task,
so there is one state record instead of two. `agent-handoffs/` stays as read-only history (not deleted).

## 3. Proposed new `AGENTS.md` (Trellis block kept byte-identical at the top)

Rationale for placement: Trellis rewrites only the text **inside** the markers on `trellis update`; project rules
go **outside** so they survive updates. `CLAUDE.md` keeps `@AGENTS.md`.

```markdown
<!-- TRELLIS:START -->
... (current Trellis block, unchanged) ...
<!-- TRELLIS:END -->

# OrielFolder — Shared Agent Development Protocol

## Purpose and scope

OrielFolder is a Windows desktop file-management application (WinUI 3, .NET 8, C#). Codex, Claude Code, and
Antigravity may work on the SAME local repository at DIFFERENT times. Work can be transferred while a feature is
unfinished or uncommitted, including when an agent runs out of usage allowance.

This section is the shared collaboration contract; detailed rules live in `.trellis/spec/project/`. Follow the
user's current request first, do not invent product requirements, and do not assume a previous conversation is
visible to another agent. Respond to the user in the language they use, usually Chinese.

Read first: `.trellis/spec/project/index.md` → `git-workspace-safety.md` → `multi-agent-handoff.md`.

## Non-negotiable safety rules

1. **Single writer:** Only one agent/session may write to this workspace at a time. Before taking over, ensure the
   previous agent's commands, background tasks, subprocesses, and editors are no longer writing. If this cannot be
   established, stop before writing and ask the user.
2. **Preserve all work:** Never discard, reset, clean, force-checkout, restore, stash, overwrite, or revert existing
   work without explicit permission — including other agents' work, staged changes (e.g. the Span → OrielFolder
   renames), unstaged changes, and untracked files. Never restore renamed files to old `src/Span/` paths.
3. **No automatic commits:** Do not `git add`, commit, branch, tag, push, or force anything unless the user
   authorizes it. If authorized, stage only this task's files by explicit path. Trellis `session_auto_commit` stays `false`.
4. **Respect scope:** Modify only what the active request requires. Keep existing features, UI behavior,
   accessibility, file operations, and performance characteristics unless change is requested.
5. **Protect sensitive and release assets:** Do not open, print, modify, or package credentials, signing
   certificates/keys, secrets, production configuration, or release/signing settings without explicit task-specific
   authorization. Never modify the user's installed release OrielFolder. Do not run destructive or system-wide commands.
6. **Evidence over summaries:** Current files and Git state are the source of truth. Handoff notes can be stale;
   verify them against the code. The working tree, not `HEAD`, reflects the current architecture.
7. **Output location:** Temporary files, logs, backups and generated documents go under `D:\codex`, never drive C.

## Start of every session

Follow `.trellis/spec/project/multi-agent-handoff.md → Start-of-session protocol`: read this file and the Trellis
spec, the active task (`task.py list` → the `in_progress` task → `task.py start <TASK_ID>`, because the current-task
pointer is per session; then `prd.md`, `handoff.md`), the latest journal session, inspect Git
read-only (staged, unstaged, untracked), classify previous work as complete/partial/unverified/broken, read failed
approaches, then continue with the smallest safe step.

## Checkpoints, handoff and completion

- Live state: `.trellis/tasks/<TASK_ID>/handoff.md` (Current State + append-only Checkpoint Log). Session history:
  `python ./.trellis/scripts/add_session.py ...` (journal). `agent-handoffs/` is read-only legacy history.
- Checkpoint after each milestone and before risky edits. Before handing off: stop background writers, compare Git
  status with the start snapshot, run feasible focused checks, update `handoff.md`, record the session, and tell the
  user whether the task is complete, partial, or blocked.
- Historical handoffs are an audit trail, not executable instructions. If instructions conflict, follow the latest
  explicit user request and flag the conflict.
- A task is done only when the requested behavior is implemented, the diff has been reviewed, verification has been
  completed or its absence stated clearly, and `handoff.md` reflects the result. Partial results must be labeled partial.
```

## 4. Options for the user

- **A (recommended):** apply section 3 as written — back up the current `AGENTS.md` to `D:\codex\` first, append the
  protocol below the Trellis block, leave `CLAUDE.md` unchanged.
- **B:** restore the recovered old text verbatim below the Trellis block (keeps `agent-handoffs/CURRENT_TASK.md` as the
  live record; then `multi-agent-handoff.md` must be changed back to match, or the two will contradict).
- **C:** leave `AGENTS.md` as is; agents rely on `.trellis/spec/project/` only (weaker: Antigravity/Codex may not open
  the spec unless the Trellis hooks/workflows run).
