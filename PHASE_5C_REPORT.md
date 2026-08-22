# DropSort Phase 5C Report

## Result

Phase 5C is **GREEN** on native Windows / Python 3.12 / NTFS.

## Goal

The existing Add Movies workflow now exposes truthful live scan progress, cooperative cancellation,
safe session restart, bounded proposal work, batched review rendering, and concise completion or
cancellation summaries. Scanning remains read-only and cannot import, journal, move, rename, copy,
overwrite, or delete media.

## Existing scan architecture reviewed

Before Phase 5C, one retained `QtTaskRunner` thread performed discovery, sequential metadata search,
matching, and whole-session construction. Discovery returned one complete tuple; only the final
callback was session-token protected. Metadata concurrency was already bounded to one, and shutdown
already waited for retained tasks. There was no progress contract or cooperative cancellation.

Phase 5C preserves those layers and the existing Qt runner. The scanner remains the only filesystem
traversal boundary; application orchestration owns proposal stages; widgets receive DTOs and queued
progress callbacks only.

## Progress model

`DiscoveryProgress` contains monotonic counters for directories, inspected entries, supported media,
movie candidates, skipped TV episodes, unknown media, and controlled errors. `ImportReviewProgress`
adds the actual workflow stage plus proposal completed/total counts. Progress events never contain
discovered-file lists or fabricated percentages.

Discovery emits an initial snapshot, then after every 32 bounded units of work, and a final snapshot.
Directory enumeration also checks cancellation every 32 names. Proposal preparation emits on stage
change, every 8 completed proposals, and at completion.

## Scan stages

- `DISCOVERING`: indeterminate bar plus real filesystem counters.
- `PREPARING_METADATA`: determinate completed/total progress.
- `BUILDING_REVIEW`: bounded row construction before the final review state.

## Cancellation model

Each scan owns one `ImportReviewCancellation` backed by a thread-safe `Event`. Checks occur before a
scheduled directory, during large directory enumeration, before each entry, before new proposal work,
and after an in-flight proposal returns. Threads and HTTP requests are never terminated forcibly.
Cancellation stops scheduling new work, makes queued progress inert, restores inputs, and reports the
observed counts.

Incomplete results are always discarded. A cancelled scan never becomes a review session and the UI
states: `Partial results were discarded. No files were changed.`

## Session isolation

The existing generation token now protects progress, failures, final results, import callbacks, and
batched row timers. A cancelled session's token cannot affect a restarted scan. Double-scan and root
changes are prevented by disabling/guarding active controls. Window close requests cancellation,
invalidates delivery, and orderly shutdown waits for bounded in-flight work.

## Large-result batching

Review rows are inserted in batches of 25 via the Qt event loop. Stable case-folded path identity
prevents duplicate widgets if a result is repeated. Old rows are detached and scheduled for deletion,
and old pending result tuples are dropped when a session is cancelled or replaced.

## Metadata concurrency and provider failure behavior

Metadata proposal work remains sequential (maximum concurrency one), respecting existing provider
timeouts, caching, authentication, and rate-limit behavior. TV/unknown/error items and cataloged paths
retain their existing early preflight behavior.

After a session-wide authentication, rate-limit, or provider-unavailable failure, no new provider
requests are scheduled. Remaining items still run non-provider preflight and receive a controlled
session failure result. A normal `NO_MATCH` or response-specific invalid result does not trigger this
short circuit.

## Qt lifecycle behavior

Progress is emitted by the worker and delivered through queued Qt signals on the UI thread. The
retained runner owns active threads until completion. Late progress after Cancel cannot replace the
`Cancelling scan...` state; callbacks after closure or a newer session are ignored; shutdown requests
cooperative cancellation and waits for active tasks.

## Scan-versus-file-operation concurrency decision

No global mutation lock was added. Scanning is read-only and does not use the Phase 5B
Organize/Undo/Recovery coordinator. Concurrent filesystem changes remain controlled TOCTOU events:
child disappearance is a bounded item error, while root disappearance, reparse conversion, identity
change, or unsafe root read stops with a controlled root-level failure.

## Files created

- `tests/unit/media/discovery/test_progress_and_cancellation.py`
- `tests/unit/media/discovery/test_enumeration_cancellation.py`
- `tests/unit/application/test_import_review_progress.py`
- `tests/unit/application/test_scan_progress_architecture.py`
- `tests/unit/ui/test_import_progress.py`
- `PHASE_5C_REPORT.md`

## Files modified

- `src/dropsort/media/discovery/contracts.py`
- `src/dropsort/media/discovery/errors.py`
- `src/dropsort/media/discovery/models.py`
- `src/dropsort/media/discovery/scanner.py`
- `src/dropsort/media/discovery/__init__.py`
- `src/dropsort/application/dto/import_review.py`
- `src/dropsort/application/errors.py`
- `src/dropsort/application/use_cases/discover_media.py`
- `src/dropsort/application/use_cases/prepare_folder_import_review.py`
- `src/dropsort/application/use_cases/propose_movie_import.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/ui/common/tasks.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/scan/import_view.py`
- `tests/unit/application/test_prepare_import_review.py`
- `tests/unit/ui/test_import_view.py`
- `tests/unit/ui/test_import_main_window.py`
- `tests/unit/ui/test_task_runner.py`
- `tests/unit/media/discovery/test_scanner_failures.py`
- `tests/integration/media/discovery/test_scanner.py`
- `tests/integration/ui/test_desktop_import_flow.py`
- `README.md`
- `PROJECT_STATUS.md`

## Database migration and dependencies

Database migration: **none**. Scan state is ephemeral and no table was added.

Dependencies changed: **none**. The implementation uses the standard library and existing PySide6
task infrastructure.

## Tests and coverage

```text
Phase 5C focused: 98 passed, 2 skipped, 0 failed
Final full suite: 834 passed, 5 skipped, 0 failed
Total branch coverage: 95%
```

The focused skips and full-suite skips are legitimate Windows symlink-creation privilege limitations.
Link/reparse behavior also has deterministic mocked coverage.

## Stress and synthetic tests

Automated discovery covers 1,000 files across 20 child directories: 200 movies, 100 TV episodes, and
700 unsupported files. It verifies exact monotonic counters, deterministic classification, iterative
traversal, no duplicate paths, and bounded progress delivery. Separate tests cover cancellation while
enumerating one 100-entry directory, proposal cancellation with an in-flight fake, and 30-row batched
deduplication.

## Adversarial findings and fixes

- Late queued progress could overwrite `Cancelling scan...`; cancelled sessions now ignore progress.
- A selected root disappearing, becoming reparse, changing identity, or failing enumeration could be
  treated like a child error; root safety failures now stop the session explicitly.
- Authentication/rate-limit/provider downtime could schedule identical requests for every candidate;
  session-wide failures now stop new provider calls while retaining cheap preflight checks.
- A huge single directory could delay cancellation during `scandir`; enumeration now checks every 32
  names before deterministic sorting.
- Batched delivery needed duplicate protection; one case-folded physical path creates at most one row.
- Zero-mutation coverage did not count all persistent boundaries; completed and cancelled review tests
  now assert zero movies, media files, and file-operation journal rows before explicit import.

No unresolved BLOCKER or CRITICAL finding remains.

## Manual native-Windows smoke test

A repository-local synthetic tree contained 5,000 files in 50 child directories. The real PySide6
`MainWindow`, scanner, metadata/cache orchestration, Qt runner, and SQLite composition were used.

Live progress was visible with an indeterminate bar and real counters. Cancel was requested at 2,185
inspected entries and observed at 2,470; the UI restored inputs, discarded all partial rows, and
remained responsive. A Qt heartbeat fired 98 times during the run. The scan restarted immediately and
completed with 5,050 inspected entries (including 50 directory entries), 500 movie candidates, 100 TV
episodes skipped, and zero scan errors. Missing credentials produced controlled metadata-unavailable
rows and the provider-wide short circuit avoided hundreds of repeated calls. Batched review rendering
completed with 600 unique rows and no lifecycle/thread error.

The test used no user media and no live TMDB credential.

## Zero-mutation verification

Before/after fixture path listings were identical. The disposable database contained:

```text
movies:          0
media_files:     0
file_operations: 0
```

No media file was moved, renamed, copied, deleted, imported, or reassociated.

## Known limitations

- Metadata proposal work is deliberately sequential; cancellation cannot abort an HTTP request already
  in flight and is observed after its finite timeout/return.
- Discovery results and review sessions remain in memory and are not a persisted review queue.
- The Widgets review list is batched but not virtualized; extremely large result sets still retain one
  widget per row after rendering.
- Scan errors are summarized by count and represented as controlled rows; there is no separate
  expandable error-log panel.
- No automatic/bulk import, organization, watcher, TV library, or missing-file reconciliation exists.

## Recommended next phase

**Phase 6A - V1 Hardening, Missing-File Reconciliation, and Final Runtime Safety.**

