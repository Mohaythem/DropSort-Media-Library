# DropSort V2 Phase 1 — Personal Library Foundation

Date: 2026-08-16  
Workspace: `D:\DropSort_ chat\DropSort`  
Status: PASS

## 1. Phase Scope

Implemented the database/domain/application foundation for logical Movies, personal preference,
watch history, rewatch history, watchlist, derived Ready to Watch, and safe separation between local
catalog clearing and retained personal data. No full V2 personal UI was added.

## 2. Baseline

Phase 0 baseline: `1023 passed, 5 skipped, 0 failed`, with 95% branch coverage using repository-local
pytest temporary directories. Git/GitHub remained out of scope as directed.

Phase 1 final regression: `1033 passed, 5 skipped, 0 failed`.

## 3. Architecture Decisions

- Personal persistence is database-only and is separated from `media_files`, File Engine, scanner,
  matcher, provider HTTP, and filesystem authorization.
- One normalized `movie_personal_state` row holds the mutually exclusive preference and optional
  watchlist timestamp. A row is created lazily; absent state reads as `NO_OPINION`.
- `watch_events` stores individual occurrences. Rewatch is derived by chronological UTC ordering of
  `watched_at, id`, rather than stored as mutable duplicate state.
- See [ADR-0015](docs/adr/ADR-0015-personal-library-foundation.md).

## 4. Logical Movie Without MediaFile

`EnsureLogicalMovie` creates a Movie from existing `MovieCatalogData` without a MediaFile, path,
import authorization, or filesystem operation. Existing `(provider, external_id)` identity is reused;
the unique-identity race is handled by re-reading the winner. `EnsureMovie` is provided as a concise
alias. Existing file-driven catalog import remains unchanged.

## 5. Personal Preference Model

`PersonalPreference` is exactly `NO_OPINION`, `LIKED`, or `BLACKLISTED`, enforced by the domain enum
and SQLite CHECK constraint. Like and blacklist cannot coexist. Clearing preference removes an
otherwise-empty state row or retains the row as `NO_OPINION` when the Movie remains watchlisted.

Favorite, personal numeric ratings, `personal_rating`, and `rating_snapshot` were not implemented.
Provider/TMDB `rating` remains catalog metadata.

## 6. Watch History Model

`watch_events` stores `id`, `movie_id`, normalized UTC `watched_at`, and `created_at`. Multiple events
are preserved. Watched is `EXISTS`, count is `COUNT`, last watched is `MAX`, and rewatch is derived
from event order. Historical dates, insertion before an existing event, middle deletion, and event
removal are covered by tests.

## 7. Watchlist Model

Watchlist is represented by `watchlist_added_at` on the personal-state row. Add is idempotent and
preserves the original add time; remove clears the timestamp and removes an otherwise-empty state
row. A watchlist entry does not require a MediaFile or a prior watch.

## 8. Ready to Watch Derivation

`QueryReadyToWatch` derives results from:

```text
movie_personal_state.watchlist_added_at IS NOT NULL
AND at least one media_files.status = 'PRESENT'
AND NOT EXISTS watch_events
```

Missing files do not qualify. Multiple files qualify when any one is present. No mutable
`ready_to_watch` flag exists.

## 9. Clear Local Library Semantics

Clear Local Library remains a local catalog/cache operation and never touches physical media.

- `metadata_cache` rows are cleared.
- All catalog `media_files` rows are cleared; journal references become NULL through existing
  `ON DELETE SET NULL` behavior.
- A Movie is deleted only when it has no WatchEvents and no explicit preference or watchlist state.
- Movies with liked/blacklisted preference, watch history, watchlist, or combinations are retained
  even after their local MediaFiles are removed.
- `file_operations` and recovery history remain preserved.
- A later import/ensure operation reuses a retained Movie by provider identity.
- Existing unresolved-operation blocking and atomic rollback behavior remain in force.

The existing English and Arabic confirmation text was minimally updated so it no longer promises that
all logical Movies will be forgotten.

## 10. Database Migration

Schema `3 → 4` via `0004_personal_library_foundation.up.sql`.

Added:

- `movie_personal_state` with preference CHECK, Movie foreign key, and preference/watchlist indexes.
- `watch_events` with Movie foreign key and `(movie_id, watched_at, id)` index.

`0004_personal_library_foundation.down.sql` refuses to discard populated personal data and is safe
when both personal tables are empty. A schema-v3 fixture test preserved existing Movies, MediaFiles,
and file-operation history; migration rerun was idempotent and foreign-key checks were clean.

## 11. Repository / Use Case Changes

Created a dedicated `PersonalLibraryRepository` protocol and
`SqlitePersonalLibraryRepository`; application code does not execute raw SQL.

Use cases include `EnsureLogicalMovie`, `SetPersonalPreference`, `ClearPersonalPreference`,
`RecordWatch`, `RemoveWatchEvent`, `GetWatchHistory`, `AddToWatchlist`, `RemoveFromWatchlist`,
`GetPersonalMovieState`, and `QueryReadyToWatch`.

## 12. Files Created

- `src/dropsort/database/migrations/0004_personal_library_foundation.up.sql`
- `src/dropsort/database/migrations/0004_personal_library_foundation.down.sql`
- `src/dropsort/library/personal/__init__.py`
- `src/dropsort/library/personal/errors.py`
- `src/dropsort/library/personal/models.py`
- `src/dropsort/library/personal/repositories.py`
- `src/dropsort/database/repositories/personal_library.py`
- `src/dropsort/application/use_cases/personal_library.py`
- `tests/integration/application/test_personal_library.py`
- `tests/integration/database/test_personal_library_migration.py`
- `docs/adr/ADR-0015-personal-library-foundation.md`
- `V2_PHASE_1_PERSONAL_LIBRARY_FOUNDATION.md`

## 13. Files Modified

- `src/dropsort/database/repositories/__init__.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/database/repositories/library_maintenance.py`
- `src/dropsort/ui/localization.py`
- `tests/integration/database/test_migrations.py`

No File Engine, matcher, parser, discovery, operation journal, recovery, or UI feature code was
redesigned.

## 14. Tests Added

Focused tests cover schema-v3 migration, constraints/indexes, fileless logical Movie creation and
identity reuse, all preference transitions, invalid preference, first/second/third/historical watch
events, middle deletion, derived counts and last watched, watchlist idempotency/removal, all Ready
to Watch combinations, personal-state combinations during Clear Local Library, cache/history
behavior, reimport identity, and physical-file preservation.

An explicit adversarial test verifies personal actions create zero `file_operations` rows and do not
change a test-owned media file's existence or SHA-256 hash.

## 15. Focused Verification Results

Targeted Phase 1, migration, Clear Library, and existing affected tests: `25 passed`.

Python source and test compilation: passed with `compileall`.

## 16. Full Suite Results

```text
1033 passed
5 skipped
0 failed
```

The five skips are the existing Windows symlink/reparse privilege limitations, including WinError
1314. Tests used repository-local basetemp directories under the authorized workspace.

## 17. Branch Coverage

```text
95% branch coverage
9103 statements
1934 branches
1033 passed, 5 skipped
```

Command used: `.venv\Scripts\python.exe -m pytest --cov=src/dropsort --cov-branch
--cov-report=term-missing -q -W error --basetemp=.pytest-v2-phase1-coverage-2`.

## 18. Packaging Verification if applicable

PyInstaller `6.22.0` completed successfully with `DropSort.spec` into disposable
`.build-phase1-dist` / `.build-phase1-work` directories. The spec already globs all SQL migration
resources. The packaged output contained all eight SQL migration resources, including both 0004
files. No existing release artifact was overwritten.

## 19. File-Safety Review

PASS. Personal operations have no filesystem imports or calls, no File Engine dependency, no
authorization path, and no operation-journal writes. Clear Local Library deletes only database rows
and poster-cache data after the existing transaction; it never deletes, moves, renames, copies, or
overwrites physical media. Existing journal gating, atomic transactions, recovery, and reversible
Move/Rename behavior were left intact.

## 20. Adversarial Review

PASS. Reviewed duplicate identity races, invalid preference state, Movie deletion with each personal
relationship, retained metadata-only Movies, missing/present Ready-to-Watch files, historical event
ordering, event deletion, FK cascades, migration rerun/down guard, operation-history retention, and
database/file separation. No material finding remained.

## 21. Known Limitations

- No personal-library UI was added; application/domain correctness is the Phase 1 acceptance surface.
- There is no separate Clear Personal Data use case yet; this phase establishes retention protection
  for Clear Local Library.
- Existing Windows symlink/reparse tests remain skipped under current privileges.
- The packaged build was resource-inspected after PyInstaller completion; no credentialed or real-user
  database launch was performed.

## 22. Deferred V2 Features

No Discover, Letterboxd, Analytics, TV, Subtitles, Folder Watcher, Favorite, personal numeric rating,
rating snapshot, Diary UI, Reviews, Tags, Profile, or full Ready to Watch/Watchlist/Blacklist UI scope
was implemented.

## 23. Phase Decision

**PASS** — V2 Phase 1 Personal Library Foundation is implemented and verified at the required
quality gate.

Explicit confirmations:

```text
No personal numeric rating system or rating_snapshot was implemented.
No Discover, Letterboxd, Analytics, TV, Subtitles, or Folder Watcher scope was implemented.
```
