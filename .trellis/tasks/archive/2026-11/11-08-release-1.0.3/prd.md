# Release OrielFolder 1.0.3

## Goal

User (2026-11-08): "把现在这个版本pull上去吧版本号顺延" ("push this version up, and bump the version number").
Asked to confirm the scope, the user chose "正式发布 1.0.3":
- release identity
- version 1.0.3
- signed installer in `dist/`
- push `main`
- tag `v1.0.3`
- GitHub Release with the EXE

Per `dev-release-boundary.md`, this permission covers this release only.

## Requirements

- R1: Ship all `rename/orielfolder` work since 1.0.2:
  - font fix and new fonts
  - startup and new-tab speedups
  - zip opening in every view, plus the extract button
  - Details header columns
  - split-view button size
- R2: Release identity unchanged: `OrielFolder.Desktop` / `CN=OrielFolder`. Package version is 1.0.3.0, so it upgrades 1.0.2.0 and the internal 1.0.2.1.
- R3: Sign with the existing distribution certificate F7940CE8… only. Do not create a new one.
- R4: `dist/`, README and install notice say 1.0.3. `main` fast-forwards. Tag `v1.0.3`. The GitHub Release has the single-file EXE and the source zip.
- R5: `rename/orielfolder` stays Dev.

## Acceptance Criteria

- [x] R1–R5 done. See `handoff.md` for hashes and verification.
- [ ] The user installs 1.0.3 over 1.0.2 and confirms settings are kept.
