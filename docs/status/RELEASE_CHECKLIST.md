# DropSort V1 Release Checklist

- [x] Build a clean PyInstaller one-directory portable release.
- [x] Bundle Qt, migrations, Inter Regular/Bold, Noto Sans Arabic Regular/Bold, TMDB logo,
  README, notices, and license texts.
- [x] Verify first run with disposable user data and no credential.
- [x] Verify a copied populated database and controlled invalid-database failure.
- [x] Verify unrelated-CWD and source-independent packaged execution.
- [x] Run packaged scan/cancel/restart, local-action, organize/undo, and relink checks on disposable data.
- [x] Run the complete source suite with branch coverage.
- [x] Scan the final artifact for secrets, developer paths, caches, fixtures, and user data.
- [x] Verify bounded/deduplicated noisy-filename metadata fallbacks without matcher-policy changes.
- [x] Verify automatic first-Library reconciliation, truthful progress, and no duplicate manual run.
- [x] Verify Relink closes/refreshes immediately and stale reconciliation cannot overwrite it.
- [x] Add provider-neutral Edit Search recovery with optional year, explicit candidate selection,
  zero automatic catalog import, and zero filesystem/file-operation side effects.
- [x] Add selectable/copyable technical information, Settings-based Operation History, compact
  responsive cards, and four persisted semantic themes in English and Arabic.
- [x] Rebuild the portable artifact after the final UX follow-up and pass the clean-profile launch
  smoke from an unrelated working directory.
- [x] Compact manual search results, cap the visible deduplicated list at five, and keep zero/error
  states compact.
- [x] Replace dense manual TMDB rows with structured localized result cards, bounded wrapped
  overviews, vertical-only scrolling, and per-card explicit selection.
- [ ] Perform a direct visual packaged Manual Search card walkthrough and live TMDB search with a
  user-supplied credential; this controlled process only rebuilt and audited the artifact.
- [x] Hide full paths from normal Add Movies rows, show Edit Search only for failed/unusable automatic
  proposals, and preserve explicit Add to Library authorization after single-candidate preselection.
- [x] Retire Main from the selectable themes, expose exactly Slate, Dark, and Light, and migrate the
  persisted Main/deep-ink compatibility IDs safely to Slate.
- [x] Add a branded multi-size DropSort application icon to Qt and the packaged executable.
- [x] Make Check Library Files terminal-state driven: progress alone remains RUNNING, terminal
  success/failure/cancellation update controls and coalesced manual dialogs safely.
- [x] Convert Add Movies to a remaining-work queue: successful rows disappear, failed rows remain,
  dismiss X is session-only, and the exhausted queue shows an explicit All done state.
- [x] Rebuild and audit the packaged release with the branded icon and four-theme/sidebar changes.
- [x] Add atomic per-user/session single-instance ownership with Qt local activation IPC; verify
  secondary launch, activation, stale recovery, and zero catalog/file-operation side effects in source tests.
- [x] Rebuild and verify duplicate, rapid, minimized, crash/stale, and post-exit launches of the exact
  packaged `release\DropSort\DropSort.exe`.
- [ ] Re-verify the corrected packaged Clear Library button through confirmation, transactional
  catalog reset, history preservation, and zero physical-media mutation.
- [ ] Re-verify the exact `AnimeSanka.com Kaze Tachinu ...` filename through live packaged TMDB.
- [ ] Complete user acceptance of English/Arabic switching, RTL/LTR restoration, and bundled fonts.
- [ ] Perform a separate clean-machine or clean-VM launch (V1 used a clean-profile approximation).
- [ ] Perform live TMDB metadata/poster verification with a user-supplied credential.
- [ ] Code-sign the executable if a signing certificate becomes available.
- [ ] Choose and publish an explicit DropSort project license before public source distribution.
