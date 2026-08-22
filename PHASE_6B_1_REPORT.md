# DropSort Phase 6B.1 Report

> Historical report correction (2026-08-15): later user acceptance found that the packaged Clear
> Library button did not pass the real confirmation result and that one leading-domain filename still
> reached matching with noisy title text. The original GREEN result below records the earlier test
> evidence only; current release status is governed by `PROJECT_STATUS.md` and remains blocked pending
> retest of the corrected release candidate.

## Result

**GREEN.** Phase 6B.1 closes the final manual-acceptance gaps without changing the Phase 1
filesystem pipeline, matcher thresholds, database schema, or runtime dependencies.

Verified on 2026-08-15 on native Windows / Python 3.12 / NTFS:

```text
Focused post-review tests: 127 passed, 1 skipped, 0 failed
Final full source suite: 936 passed, 5 skipped, 0 failed
Total branch coverage: 95%
```

The five full-suite skips remain legitimate Windows symlink-creation privilege limitations.

## Noisy filename metadata search

`movie_search_fallbacks.py` builds at most four deterministic provider-neutral queries. It starts
with the parsed title/year, then uses the existing filename parser plus bounded cleanup for leading
site/domain prefixes, bracketed release noise already handled by parsing, and common edition/cut
suffixes. Year-aware variants precede yearless variants. Equivalent queries are case-insensitively
deduplicated.

Candidates from all attempted queries are deduplicated by `(provider, external_id)`. Each query is
matched against its corresponding cleaned title, but the existing matcher weights, thresholds,
ambiguity margin, year-conflict behavior, and explicit import boundary are unchanged. Fuzzy title
evidence alone cannot authorize a match or catalog import. Numeric titles including `1917`,
`2001 A Space Odyssey`, and `Blade Runner 2049` have regression coverage.

A controlled provider failure remains distinct from a successful zero-result response. Once a
session-wide provider failure is known, no further fallback requests are scheduled. Normal tests use
fake providers; no live TMDB credential was supplied for this acceptance run, so live noisy-filename
TMDB verification is **NOT VERIFIED**.

## Automatic Library reconciliation

The first Library entry in each desktop session renders the catalog immediately, then starts the
existing bounded reconciliation use case in the background. Progress and the final
present/missing/unavailable counts appear inline in the Library; no automatic modal is shown.

Automatic and manual checks share one active reconciliation slot. A manual request coalesces with an
active automatic run instead of starting duplicate work. Cancellation, window-close invalidation,
stale-result rejection, bounded batches, and UI-thread-only widget updates reuse the established task
boundary. Repository status updates now use path compare-and-swap behavior and return the number of
rows actually committed, so progress never claims a stale row was updated.

The packaged clean-profile smoke opened an empty Library and completed the automatic check with
`0 present, 0 missing, 0 unavailable` while remaining responsive.

## Relink completion and race protection

After a successful Relink, its modal is accepted and closed before the completion signal is emitted.
Library and Movie Details then refresh immediately, showing the same `MediaFile` row at the new path
with `PRESENT` status.

Relink retains its existing explicit confirmation, missing-old-path check, path ownership conflict
check, size/extension/parser/technical compatibility checks, filesystem identity revalidation, and
transactional catalog-only update. It performs no Move, Rename, Copy, Delete, or file-operation
journal insertion.

Reconciliation persists status only when the row still owns the path that was inspected. A stale
result captured before Relink therefore cannot overwrite the new path or status. Relink and catalog
maintenance also share the desktop operation coordinator with other safety-sensitive workflows.

## Clear Library Data

Settings now contains a **Clear Library Data** action behind an explicit confirmation stating that
physical movie files are not deleted, moved, renamed, copied, or modified and that operation history
and recovery records are preserved.

One `BEGIN IMMEDIATE` transaction:

- blocks while any file operation is nonterminal;
- deletes catalog `movies`, `media_files`, and cached metadata;
- preserves the immutable `file_operations` journal and recovery evidence;
- verifies foreign-key integrity; and
- rolls back completely on database failure.

After the database commit, the application clears only validated regular poster-cache assets under
the centralized injected cache root. Root/reparse/identity checks prevent cache-root escape. Cache
cleanup failure is a nonfatal warning and cannot roll back or misreport the already-committed catalog
transaction.

The UI invalidates open details and renders an empty Library immediately. Repeated clearing is
idempotent, and normal scan/import can repopulate the catalog. Automated integration tests verify
that physical fixture hashes remain unchanged and no journal row is created or removed.

## Files added

- `src/dropsort/application/dto/catalog_maintenance.py`
- `src/dropsort/application/use_cases/clear_library_data.py`
- `src/dropsort/application/use_cases/movie_search_fallbacks.py`
- `src/dropsort/database/repositories/library_maintenance.py`
- `src/dropsort/library/movies/maintenance.py`
- `tests/integration/application/test_clear_library_data.py`
- `tests/unit/application/test_clear_library_architecture.py`
- `PHASE_6B_1_REPORT.md`

## Important files modified

- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/use_cases/propose_movie_import.py`
- `src/dropsort/application/use_cases/reconcile_library_files.py`
- `src/dropsort/database/repositories/media_files.py`
- `src/dropsort/library/movies/errors.py`
- `src/dropsort/posters/cache.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/library/library_view.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/reconciliation/dialog.py`
- `src/dropsort/ui/reconciliation/relink_dialog.py`
- `src/dropsort/ui/settings/settings_view.py`
- focused application, database, poster, UI, architecture, and bootstrap tests
- `PROJECT_STATUS.md`, `README.md`, and `RELEASE_CHECKLIST.md`

## Review findings and fixes

- **HIGH:** a reconciliation result captured before Relink could update the relocated row. Fixed
  with repository path compare-and-swap status writes and an integration regression test.
- **HIGH:** catalog clearing could conflict with an active safety-sensitive workflow. Fixed by
  sharing the desktop operation coordinator and by rejecting nonterminal journal state inside the
  database transaction.
- **HIGH:** untrusted/cache link entries could escape a naïve cleanup. Fixed with cache-root,
  no-follow, reparse, type, and identity checks; nested unrelated entries remain untouched.
- **MEDIUM:** fallback searches could repeat equivalent queries/candidates or continue after a
  session-wide provider failure. Fixed with deterministic query/candidate deduplication, a four-query
  bound, and the established provider-failure short circuit.
- **LOW:** exception logging initially omitted the actual callback exception tuple. Fixed without
  exposing credentials or untrusted payloads.

No unresolved BLOCKER, CRITICAL, or release-relevant HIGH finding remains.

## Release rebuild and packaged acceptance

The release was rebuilt cleanly with PyInstaller 6.22.0 as a one-directory portable application:

```text
Executable: release\DropSort\DropSort.exe
Portable directory size: 118,498,215 bytes (113.01 MiB)
Files: 184
```

The Phase 6B.1 artifact contained the Qt runtime, three SQLite migrations, the then-current bundled
font and OFL,
TMDB logo, README, and third-party notices. Artifact scans found zero forbidden development/runtime
artifacts, zero developer-path matches, zero assigned TMDB environment secrets, and zero bearer-token
patterns. Two generic `Authorization:` strings inside the shipped OpenSSL/Qt Network DLLs were
classified as library implementation text rather than credentials; no values were printed.

Using only the packaged files, no activated virtual environment, an unrelated working directory,
and a disposable redirected `LOCALAPPDATA`, the app:

- created/reused `%LOCALAPPDATA%\DropSort`-equivalent database, poster-cache, and log locations;
- opened on the empty Library without a TMDB credential;
- ran automatic reconciliation once and showed truthful zero counts;
- rendered the packaged theme, font, Settings attribution, and Clear Library safety confirmation;
- cancelled the destructive confirmation without changing data; and
- shut down cleanly twice with a zero-byte diagnostic log.

No separate clean machine or VM was available; this remains a **CLEAN-PROFILE APPROXIMATION**.

## Dependencies and migration

Dependencies changed: none. No database migration was added.

## Known limitations

- Live TMDB noisy-filename lookup was not verified because no credential was supplied. Automated
  fake-provider coverage is deterministic and requires no network.
- Automatic reconciliation reports point-in-time catalog-path availability; it does not search drives.
- Clear Library Data intentionally preserves operation history/recovery state and does not offer
  selective deletion.
- The portable executable remains unsigned and may trigger Windows SmartScreen.
- A separate clean Windows machine/VM verification and explicit DropSort project license remain open
  distribution considerations.

## Release decision

**V1 RELEASE CANDIDATE: READY.** No V2 work was started.
