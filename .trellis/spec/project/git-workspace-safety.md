# Git Workspace Safety (non-negotiable)

> The working tree holds a large **uncommitted** rename and feature state (654 staged renames,
> ~495 files with unstaged edits, ~124 untracked files as of 2026-11-06). It is the only copy
> of that work. Treat it as irreplaceable. These rules come from the original OrielFolder
> shared-agent protocol and the user's explicit instructions; Trellis defaults never override them.

## Forbidden without explicit, task-specific user approval

1. `git reset` (any mode), `git clean`, `git checkout -- <path>` / `git checkout <rev> -- <path>`,
   `git restore` (worktree or `--staged`), `git stash` (any form), `git rm`, branch switching that
   would rewrite the tree, rebase/merge/cherry-pick, any `--force` operation.
2. `git add`, `git commit`, `git commit --amend`, `git push`, creating branches or tags.
   Changing the index is forbidden too — the staged renames must stay exactly as they are.
3. Reverting or "cleaning up" another agent's or the user's changes, even if they look incomplete,
   broken, or unrelated. Do not restore renamed files to their old `src/Span/...` paths.
4. Overwriting working-tree files with content from `HEAD`, older commits, tags, or other branches
   (`git show <rev>:path > path` is an overwrite too).
5. Broad reformatting, mass renames, line-ending normalization, or "fix all warnings" sweeps.
   (Git warns that many files are LF and would become CRLF when Git touches them — leave them.)
6. Touching the user's installed **release** OrielFolder: uninstalling, re-registering, replacing its
   files, data or settings, its Listary plugin, file associations, or certificates.
7. Opening, printing, or changing credentials, signing certificates/keys (`Cert:\CurrentUser\My`),
   `connections.json`, `credentials.dat`, release/signing settings, or production configuration.
8. System-wide / destructive commands: registry writes, deleting user files, killing processes
   that are not this task's own, changing security policy.

If one of these seems necessary, stop and ask the user, stating exactly which command, which files,
and why.

## Allowed read-only inspection (use freely)

```powershell
git status --porcelain=v1 -uall          # staged (col 1), unstaged (col 2), ?? untracked
git diff --cached --name-status          # staged changes incl. renames
git diff --name-status                   # unstaged changes
git diff --cached -M --stat ; git diff --stat
git ls-files --others --exclude-standard # untracked (git diff never shows these)
git log --oneline -8 ; git branch --show-current
git show HEAD:<path>                     # read old version to stdout only
```

Set `$env:GIT_OPTIONAL_LOCKS='0'` for long read-only sessions to avoid index refresh writes.
`git diff` alone is not enough — always check the index **and** untracked files.

## Trellis-specific guards

- `.trellis/config.yaml` sets `session_auto_commit: false`, so `add_session.py` and
  `task.py archive` only write files and never stage/commit. Do not re-enable it.
- Workflow Phase 3.4 ("commit plan") may only run when the user explicitly asks to commit; even then
  stage only this task's files by explicit path, never `git add -A`, `git add .`, or `git add -f .trellis/`.
- `AGENTS.md`, `CLAUDE.md`, `.claude/`, `.agents/` are gitignored — Git does not protect them.
  Do not rewrite them without approval; back up before any approved edit.

## Session snapshot routine

At session start, save a snapshot outside the repo (e.g. `D:\codex\...` or the agent scratch dir):

```powershell
git status --porcelain=v1 -uall > <snapshot>\status-before.txt
git diff --cached | Get-FileHash   # optional hash of staged diff
```

At session end, diff the new status against the snapshot. Every changed line must belong to this task.
Record the comparison in the task `handoff.md`.

## Single writer

Only one agent/session writes to the working tree at a time. Before writing, confirm the previous
agent's editors, background jobs, watchers and subprocesses are stopped (ask the user if unknown).
Do not leave background writers running when you hand off.
