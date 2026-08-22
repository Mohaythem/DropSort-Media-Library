# ADR-0015: Database-Only Personal Library Foundation

**Status:** Accepted for V2 Phase 1

**Date:** 2026-08-16

## Context

DropSort's V1 catalog is centered on local media files, but V2 personal features must also track
logical Movies that have no local file. Clearing local catalog data must therefore be separated from
retaining personal state, while the File Engine and physical media safety boundaries remain unchanged.

## Decision

Add a database-only personal-library boundary under `library/personal`, with a SQLite adapter under
`database/repositories` and application use cases under `application/use_cases`.

`movie_personal_state` stores one constrained preference (`NO_OPINION`, `LIKED`, or `BLACKLISTED`)
and optional watchlist-added time. `watch_events` stores individual viewing occurrences. Watched,
watch count, last watched, and rewatch status are derived from normalized events ordered by UTC
`watched_at` and event ID. No personal numeric rating or rating snapshot is stored.

Ready to Watch is a query, not mutable state: a watchlisted Movie must have at least one `PRESENT`
MediaFile and no WatchEvent.

Clear Local Library deletes metadata cache and catalog MediaFile rows, but deletes a Movie only when
it has no watch events and no explicit preference/watchlist state. Operation history is preserved;
foreign keys detach deleted MediaFile references from journal rows. No personal operation invokes
filesystem authorization, File Engine services, or physical path mutation.

## Consequences

- A logical Movie can be created and reused by provider identity without a placeholder path.
- Personal state survives local-file clearing and can reconnect to a later reimport through the same
  provider/external identity.
- Rewatch and summary facts cannot drift from individual event history.
- The existing V1 library query remains compatible with zero-file Movie details.
- Personal UI, Discover, Diary, Letterboxd, TV, subtitles, and watcher automation remain deferred.
