# Dev-only by default (release boundary)

> **User rule, 2026-11-06:** "只要我没说放进正式版本或输出exe，一切改动都只在dev。"
> Applies to every agent (Codex, Claude Code, Antigravity) and every task. It overrides any older handoff,
> task PRD or plan that implies release work.

## Default: everything stays in Dev

Unless the user's **current** message explicitly says to put the change into the release version
("放进正式版本 / 正式版 / 发布 / release") or to output an EXE/installer ("输出 exe / 打包 / 生成安装包"):

- Build and run only the Dev identity: `build-dev.ps1` → `start-dev.ps1`
  (`LocalDevelopment.OrielFolderDev`, `CN=OrielFolder Dev`, `OrielFolderDev*.exe`, data in `OrielFolderDev`).
- Commit only to a non-`main` working branch (currently `rename/orielfolder`). `main` holds the released v1.0.1 state.

## Forbidden without that explicit instruction

| Do not | Examples |
|--------|----------|
| Switch the app to the release identity | `Package.appxmanifest` → `OrielFolder.Desktop` / `CN=OrielFolder`; AssemblyName `OrielFolder` (non-Dev); `AppIdentity` release values |
| Produce release artifacts | run `package-oriel.ps1`, Inno Setup `ISCC.exe`, `-p:OrielStandalone=true` / `ORIEL_STANDALONE` EXE builds, MSIX bundles, anything under `dist/` |
| Touch signing | create/use the `CN=OrielFolder` certificate in `Cert:\CurrentUser\My`, sign packages |
| Change release delivery | `installer/*` behaviour for end users, `dist/`, release notes/version bumps meant for publishing |
| Put work into `main` | commit on, merge into, rebase onto, or push `main`; push release tags |
| Touch the installed release app | its package, data, Listary plugin, file associations (already forbidden by git-workspace-safety.md) |

Editing release-related **source** is fine when a dev task needs it (e.g. a bug fix in shared code), as long as the
result is only built/run as Dev and nothing above is executed.

## When the user does ask for release / EXE

- The permission covers that request only; it does not carry over to later tasks or sessions.
- Restate the scope (which branch, which identity, which artifact, output folder `D:\codex\...`) before running anything.
- Record in the task `handoff.md` that release work was explicitly requested, with the user's wording.

## If unsure

Ask the user. Treat ambiguous wording ("提交", "完成", "合并一下") as Dev-only.
