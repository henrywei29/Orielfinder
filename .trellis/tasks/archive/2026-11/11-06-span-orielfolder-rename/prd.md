# Continue Span → OrielFolder rename (takeover from Antigravity)

## Goal

Continue the uncommitted Span → OrielFolder rename **on top of the current working tree** (not HEAD):
rename the remaining identifiers that really belong to the app, keep framework / third-party /
compatibility `Span` names, verify, and keep the cross-agent handoff current.

## Requirements (from the user's request, 2026-11-06)

- Naming: app name `OrielFolder`; C# types/namespaces/assemblies `OrielFolder`; variables `orielFolder`;
  files/folders follow existing conventions.
- Never blind-replace `Span`. Keep `.NET` `Span<T>`/`ReadOnlySpan<T>`/`TimeSpan`, XAML `RowSpan`/`ColumnSpan`/`<Span>`,
  HTML `<span>`, pdf.js, third-party, external tools/protocols.
- Analyse semantics before renaming. Identifiers affecting app identity, persisted data, registry,
  file associations, compatibility or external APIs: analyse risk and **ask before changing**.
- Do not touch GUIDs, signing, certificates, publish identity, installed release app, machine settings.
- Preserve all existing Git work: no reset/clean/restore/checkout/stash/commit/push/add; no index changes.
- Keep the project building and the original behavior intact.

## Acceptance Criteria

- [ ] Remaining `Span` references inventoried and classified: rename now / keep (with reason) / needs approval.
- [ ] Safe internal renames applied with all references updated (C#, XAML, tests, scripts, docs).
- [ ] Build and focused tests executed on the renamed tree; real results recorded; pre-existing issues separated.
- [ ] Git index (staged diff hash) unchanged; only this task's files changed in the working tree.
- [ ] `handoff.md` Current State + Checkpoint Log updated; journal session recorded;
      `spec/project/rename-migration-status.md` updated.
- [ ] Items needing approval listed for the user.

## Out of scope (separate tasks)

- Release packaging / Dev-vs-release manifest identity, standalone EXE (`DesktopSettingsStore` missing).
- Data migration of persisted identifiers (keybinding command IDs, WebView2 origins, log names) unless approved.
