# Python Stabilization Pass 2: Offline Registration and TMDB Decoupling

Date: 2026-08-25
Branch: `codex`
Accepted starting SHA: `92fdfb537e164d936e84071047ca1bbc173d0cd9`

## Scope

This pass makes Add Movies local-first. One explicit **Add to DropSort Library** action now
commits a stable local `MovieId` and `MediaFileId` before optional TMDB enrichment. It does not
redesign poster networking, Clear Library Data, missing-file actions, packaging, or the visual UI.

## Old Behavior

The old confirmation path required a selected TMDB candidate and fetched TMDB details before
creating the Movie and MediaFile association. Missing credentials, offline/provider failure, zero
results, and ambiguous results could therefore prevent a valid local file from entering the Library.

## New Behavior

A valid movie discovery is locally addable even without a candidate or usable TMDB connection.
The UI publishes registration success, removes the completed review row, and incrementally inserts
or refreshes the one affected Library card immediately after Transaction A. Candidate-driven
enrichment is then scheduled as a separate background task keyed by the stable `MovieId`. Scan,
proposal generation, cancellation, and row dismissal still create no catalog or journal state.

## Schema Migration

Migration `0005_offline_movie_registration` rebuilds `movies` with nullable paired
`provider`/`external_id` columns and a non-null `metadata_status` constrained to
`PENDING`, `READY`, `FAILED`, or `NEEDS_MATCH`. Both identity columns must be null or both
must be populated and non-blank. `READY` requires an external identity. The populated
`(provider, external_id)` pair remains unique, while multiple provisional null/null Movies are
allowed.

All pre-existing Movies migrate to `READY`. The migration preserves Movie IDs, MediaFile IDs and
links, descriptive metadata, timestamps, personal state, watch-event IDs and references, and
filesystem-operation/recovery evidence.

## Migration Safety

The migration runner recognizes an explicit foreign-key-off rebuild marker. It disables foreign-key
enforcement before `BEGIN IMMEDIATE`, performs the complete rebuild and migration-version write in
one transaction, runs `PRAGMA foreign_key_check` before commit, and restores enforcement after
both success and failure. A failed migration does not advance `schema_migrations`.

The down migration fails closed if any Movie lacks an external identity or is not `READY`; it
does not fabricate identities. Focused tests prove successful and failed rebuild atomicity,
reference preservation, clean foreign keys, safe downgrade, and refused unsafe downgrade.

## Transaction A: Local Registration

`RegisterLocalMovieFile` receives already-discovered/parser facts and performs no provider,
poster, or filesystem call. Inside one SQLite transaction it rechecks the normalized path, returns
existing stable identities for an already-linked path, or creates a provisional `PENDING` Movie
and creates/links the MediaFile atomically. An existing unlinked MediaFile keeps its original ID.
Failure while writing the MediaFile rolls back the new Movie.

The fallback title and year come from parsed local facts. Registration creates no filesystem
operation, and tests preserve the original path and bytes.

## Transaction B: Metadata Enrichment

`EnrichMovieMetadata` performs provider detail retrieval before opening its SQLite unit of work.
It then reloads the Movie by stable `MovieId`, validates identity ownership, and attaches identity
plus descriptive metadata in place. Poster loading is post-commit and cannot undo catalog state.

`ConfirmMovieImport.register()` and `ConfirmMovieImport.enrich()` expose the two boundaries.
The synchronous compatibility method still executes A then B, while `ImportView` publishes A's
success before it schedules B.

## Metadata States

- `PENDING`: local registration succeeded; enrichment is not attempted yet or failed retryably,
  including missing credentials, offline/unavailable service, or rate limiting.
- `READY`: a valid external identity and provider metadata are attached to the same Movie.
- `FAILED`: provider mismatch, invalid response, or other non-retryable data failure.
- `NEEDS_MATCH`: no confident match, ambiguous selection, identity replacement request, or
  external-identity collision requires review.

No enrichment outcome deletes a Movie or MediaFile.

## Collision Behavior

Strategy B is implemented. If provisional Movie A selects an identity owned by Movie B, both Movies
and all MediaFiles survive unchanged, no file is reparented, no personal/watch state or operation
attribution is merged, A becomes `NEEDS_MATCH`, B is unchanged, and a typed
`IDENTITY_COLLISION` result exposes both Movie IDs. Repeating the collision is deterministic.
Title/year are never used as an identity key.

## Poster and Check Library Compatibility

Identity-less Movies do not construct provider-dependent poster requests and retain the local
placeholder. Check Library treats identity-less `PENDING` Movies as valid local members, reports
that matching is needed, does not call the provider for them, and never deletes them. Check Library
remains manual and is not restored as startup work.

## Files Changed

- Schema/runner: migration `0005` up/down and atomic foreign-key rebuild support.
- Domain/persistence: nullable external identity, `MetadataStatus`, repository attach/status
  operations, and unit-of-work contracts.
- Application: local-registration/enrichment DTOs and use cases, split confirmation boundary,
  bootstrap composition, Library projection mapping.
- UI: locally confirmable offline/no-match rows, immediate registration callback, deferred
  enrichment callback, one-Movie incremental insertion, and nullable poster guards.
- Tests: migration, registration, enrichment, collision, orchestration, presentation, import flow,
  restart, Check Library, and Pass 1 regression expectations.
- Documentation: this report, `Deep-Audit.md`, and `PROJECT_STATUS.md`.

## Verification

- Gate A, focused migration: **5 passed**.
- Gate B, final focused Add/enrichment/import: **72 passed**.
- Two-stage callback gate after review correction: **46 passed**.
- Gates C/D, persistence, personal state, Check Library, and Pass 1 regressions: **95 passed**.
- Architecture boundary correction: **49 passed**.
- Full suite: **1,217 collected; 1,202 passed; 10 failed; 5 skipped; 333.77 s**.
- Accepted baseline: **1,191 collected; 1,175 passed; 11 failed; 5 skipped**.
- New failures: **0**. The remaining ten reproduce independently and are the known unrelated
  localization/theme/UI/source-inspection contracts.
- Compile: `python -m compileall -q src tests` **passed**.
- Diff whitespace: `git diff --check` **passed**.

## Native Offline Smoke

An offscreen native Windows/Python 3.12 desktop launch used a disposable database and poster-cache
root with TMDB credentials omitted. The app opened to Library and closed cleanly. Automated native
SQLite smoke then registered disposable media, reopened the database, confirmed the same MovieId
and MediaFileId in `PENDING`, enriched with a deterministic valid provider, and confirmed the same
IDs in `READY` with unchanged media bytes.

`DROPSORT_TMDB_READ_ACCESS_TOKEN` was not configured, so the live-TMDB network segment could not
be performed without inventing credentials. Live TMDB enrichment remains explicitly unverified;
the offline launch, restart persistence, and same-ID READY transition are verified.

## Known Limitations and Deferred Work

- Phase 3: full poster cache/network policy redesign; Library-open cache-miss networking remains.
- Phase 4: Clear Library Data active personal/history semantics.
- Phase 5: Play/Open Folder persistence when a file becomes missing.
- Phase 6: changed-file physical-identity redesign.
- Phase 7/release: packaging rebuild, code signing, licensing, and release-verifier redesign.
- No automatic merge, alias/tombstone system, external identity table, TV/collections/storage
  expansion, or general UI redesign was added.
- Live TMDB smoke is pending a valid credential; no token or secret was stored.

Dependencies changed: none. No personal media, runtime database, cache, log, release artifact, or
credential is included in this pass.
