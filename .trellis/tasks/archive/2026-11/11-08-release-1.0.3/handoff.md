# Handoff — 11-08-release-1.0.3

## Current State
- **TASK_ID:** 11-08-release-1.0.3
- **Updated:** 2026-11-08 (local)
- **Agent:** Claude Code
- **Status:** Released. Release work was explicitly requested; the user's wording is quoted in `prd.md`.
- **Git (remote):**
  - `main` is `b227b72`, a fast-forward from `ceeca96` (1.0.2).
  - Branch `release/1.0.3` was pushed.
  - Tag `v1.0.3` is an annotated tag.
  - GitHub Release "OrielFolder 1.0.3" is marked Latest: https://github.com/henrywei29/Orielfinder/releases/tag/v1.0.3
- **Git (local):**
  - `main` was moved to `b227b72`.
  - The main checkout stayed on `rename/orielfolder` (Dev identity); it does not contain the release commit.

### How it was done
1. Created a temporary worktree `D:\project\Orielfolder\Orielfolder-release-1.0.3` on new branch `release/1.0.3` from `origin/main`.
   - `origin/main` already had the release identity.
   - The worktree had to sit next to `.tools` because `package-oriel.ps1` looks for `..\.tools`.
2. `git merge --no-ff rename/orielfolder` → `5784628`.
   - No conflicts. The release identity was kept.
   - The only csproj change was the three bundled fonts.
3. Bumped the version to 1.0.3. Files: `OrielFolder.csproj` (Version/AssemblyVersion/FileVersion), `Package.appxmanifest`, `installer/OrielFolder.iss`, `package-oriel.ps1` (notice and file names), `README.md`.
4. Ran the unit tests in the worktree under the release identity: 1259 passed, 0 failed, 3 skipped.
5. Ran `D:\codex\release-1.0.3\release-build.ps1` outside the Claude container via WMI (`Win32_Process.Create`), because the real certificate store is not visible from inside. It:
   - checks that exactly one F7940CE8 certificate exists;
   - moves the old `D:\codex\OrielFolder\build` aside;
   - runs `package-oriel.ps1`;
   - builds the single-file installer (DiskSpanning off);
   - writes the signature and hash summary.
6. Copied the spanned installer and the install notice into `dist/` and committed `b227b72`.
7. Pushed `main`, `release/1.0.3` and `v1.0.3`, then ran `gh release create` with the notes in `D:\codex\release-1.0.3\release-notes.md`.
8. Removed the worktree.

### Verification
- Package: `OrielFolder.Desktop` / `CN=OrielFolder` / 1.0.3.0. The `OrielFolder.dll` FileVersion is 1.0.3.0. The three new fonts are in `Assets\Fonts`.
- Signatures are `Valid`, signer F7940CE8D52E0980AE4B3F52B4671B06F315027A, on:
  - the MSIX
  - the spanned installer EXE
  - the single-file EXE
- SHA-256 hashes:

  | File | SHA-256 |
  |---|---|
  | Single-file `OrielFolder-1.0.3-x64-Setup.exe` (200,970,560 B) | `82B25B46BBA909850333C024919A934E1691D48EDBE9C9656D9A77B4E75B990E` |
  | `OrielFolder-1.0.3-source.zip` (26,139,525 B) | `F07943415A6F88E0026A3C3E8AE055F81369128D4CFCC30E81AF4E30D39D2189` |

  The GitHub asset digests match.
- The source zip grew from 14 MB to 26 MB because of the bundled fonts.
- Not done: installing the release build on this machine (the installed release app is not touched). Upgrade over 1.0.2 is left for the user to check.

### Files outside the repo
- `D:\codex\release-1.0.3\`: `release-build.ps1`, its log and summary, `release-notes.md`, the test log, `run-tests.ps1`.
- `D:\codex\OrielFolder\distribution-20261108-024248\`: all artifacts, including `single-file\` and the payload MSIX.
- `D:\codex\OrielFolder\build-v1.0.2-old-*`: the previous build output.

## Checkpoint Log
- 2026-11-08 [Claude Code] User confirmed "正式发布 1.0.3". Merged, bumped, tested (1259 passed), built and signed outside the container, pushed main/tag, published the GitHub Release.
