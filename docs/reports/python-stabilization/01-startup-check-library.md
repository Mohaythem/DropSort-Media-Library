# Python Stabilization Pass 1: Startup and Check Library

Date: 2026-08-23
Branch: `codex`
Scope: startup composition, explicit Check Library, reconciliation progress identity, and
incremental Library presentation only.

## Outcome

DropSort startup now loads the local SQLite Library snapshot and renders it without starting file
reconciliation. No startup path calls `ReconcileLibraryFiles`, catalog-wide file inspection,
metadata health repair, or a progress-driven full Library reload.

Check Library remains a user-initiated operation. After a status batch commits, progress carries an
immutable `MediaFileStatusChange` with `media_file_id`, `movie_id`, and the new status. MainWindow
requests the summary for each changed MovieId through `GetMovieListItem`; `LibraryView` updates its
cached tuple and lets `MovieGrid` replace only the changed card. Unaffected card widgets keep their
identity. A missing file continues to count as a registered media file and the movie stays visible.

Metadata repairs performed by the same explicit Check Library operation expose changed MovieIds as
progress and use the same one-item update path.

## Implementation

- Removed the automatic reconciliation trigger, worker callbacks, coalescing state, close-time
  cancellation state, and every reconciliation-progress call to `_refresh_library_snapshot()` from
  `MainWindow`.
- Added `SqliteMovieLibraryReadRepository.get_movie_summary(movie_id)` and the application-level
  `GetMovieListItem` use case.
- Added `LibraryUiActions.get_movie_item(movie_id)` and `LibraryView.refresh_movies(movie_ids)`.
- Extended manual Check Library page/dialog progress delivery without changing their explicit start,
  cancellation, or terminal-state behavior.
- Preserved compare-and-swap status writes and confirmed identities when a batch is only partially
  applied, so stale Relink races are not reported as committed changes.
- Added eight focused tests across startup, UI, application, and SQLite boundaries.

No database migration, dependency, filesystem-engine change, TMDB matching redesign, Add Movies
change, Clear Library change, or UI redesign was made.

## Verification

- Eight stabilization tests plus affected MainWindow tests: `29 passed`.
- Broader affected application/database/UI gate: `109 passed, 1 failed`; the single failure is the
  pre-existing `load_on_show=False` bootstrap expectation documented by the Deep Audit.
- Full suite: `1,175 passed, 11 failed, 5 skipped` in 162.72 seconds. All 11 failures exactly match
  the named pre-Pass-1 baseline failures; no new failure appeared.
- `python -m compileall -q src`: passed.
- Offscreen startup smoke: passed with a temporary SQLite database and
  `ReconcileLibraryFiles.execute` replaced by a guard that raises if invoked.
- Tests used repository-local temporary paths; no real media file or user library was mutated.

## Explicit Answers

- Does startup automatically reconcile or verify cataloged files? **NO.**
- Does startup automatically run Check Library or metadata repair? **NO.**
- Does reconciliation progress trigger a full Library list query/reload? **NO.**
- Is Check Library still explicit and manual? **YES.**
- Does a committed availability change carry MediaFileId, MovieId, and status? **YES.**
- Does one changed movie use a one-item query and preserve unaffected card widgets? **YES.**
- Does a missing file remain registered and keep its movie visible? **YES.**
- Were schema, migration, dependency, filesystem engine, Add Movies, Clear Library, or `main`
  changed? **NO.**

## Deferred / Known Boundary

Library card construction still delegates individual poster cache misses to the existing bounded
poster service, which can use TMDB when credentials are configured. Pass 1 removed no global poster
refresh because none was on the startup path, but it did not redesign cache-miss poster policy.
Making Library open strictly network-silent is deferred to a poster-boundary pass rather than being
mixed into startup/reconciliation stabilization.
