# Project Layer — OrielFolder (repository-wide rules)

> Applies to the whole repository: architecture, folder layout, rename/migration
> state, build & verification, Git workspace safety, and multi-agent handoff.
> Read this layer **before any work**, then the `frontend/` (WinUI UI) or
> `backend/` (C# services, workers, file operations) layer for the code you touch.

Product name: **OrielFolder** (user-facing spelling in some docs: *Orielfolder*).
This repository is a GPL-3.0 fork of the upstream **SPAN Finder** (`Span`) WinUI 3
file manager. `Span` is a **legacy name**, never the current application name.

---

## Guidelines Index

| Guide | Description |
|-------|-------------|
| [Architecture](./architecture.md) | Projects, processes, identities, data flow (current working tree) |
| [Directory Structure](./directory-structure.md) | Current repository and source layout |
| [Rename & Migration Status](./rename-migration-status.md) | Span → OrielFolder rename: done / in progress / intentionally kept |
| [Build, Test & Verification](./build-test-verification.md) | Real build commands, test suites, what counts as verified |
| [Git Workspace Safety](./git-workspace-safety.md) | Non-negotiable rules for the shared, heavily-modified working tree |
| [Multi-Agent Handoff](./multi-agent-handoff.md) | Codex / Claude Code / Antigravity start + end-of-session protocol |

---

## Pre-Development Checklist (every agent, every session)

- [ ] Read `AGENTS.md` (via `CLAUDE.md` for Claude Code) and this index.
- [ ] Follow [Multi-Agent Handoff → Start](./multi-agent-handoff.md#start-of-session-protocol):
      read the active task's `handoff.md`, the latest journal session, and Git state.
- [ ] Confirm the previous writer has stopped (single-writer rule).
- [ ] Read [Git Workspace Safety](./git-workspace-safety.md). No reset/clean/restore/stash/commit
      without explicit user approval.
- [ ] Check [Rename & Migration Status](./rename-migration-status.md) before touching any
      `Span*` identifier — many are intentionally kept for compatibility.
- [ ] Discover the real files from the working tree, not from `HEAD` (HEAD still has `src/Span/`).

## Quality Check (before ending a session)

- [ ] `git status --porcelain=v1 -uall` compared to the start-of-session snapshot; only
      files belonging to this task changed.
- [ ] Only actually executed builds/tests are reported, with exact commands and results.
- [ ] Active task `handoff.md` updated; journal session recorded (no auto-commit).
- [ ] Anything needing user approval is listed explicitly.

---

**Language**: Spec files are written in English (Trellis convention). Agents respond to
the user in the user's language (usually Chinese).
