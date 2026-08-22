# ADR-0012: Read-Only Movie Library Query Boundary

**Status:** Accepted for Phase 3B

**Date:** 2026-08-11

## Context

The future desktop UI needs local movie summaries, recently added movies, and coherent movie/file
details without knowing about SQLite, provider adapters, filesystem services, or persistence domain
objects. Querying file counts one movie at a time would also create avoidable N+1 behavior.

## Decision

Add a provider-neutral `MovieLibraryReadRepository` protocol under `library/movies`. Its domain read
projections are mapped by application use cases into immutable presentation DTOs.

The SQLite adapter uses one aggregate `LEFT JOIN` query for paged movie summaries and file counts.
Default ordering is `date_added` newest first, then movie ID descending. `julianday` is used so
timezone offsets are compared chronologically rather than lexically. Recently added uses the same
ordering with an explicit bounded limit.

Movie details use two bounded `SELECT` statements on one SQLite read snapshot: one for the movie and
one for its files, ordered by media-file ID. Stored paths are returned as display strings only.

Application use cases depend only on the read protocol and translate controlled catalog failures to
application-level query errors. Unknown IDs raise `MovieNotFoundError`.

## Consequences

- No SQL row, JSON genre payload, provider response, or persistence model reaches presentation.
- Listing performs one query regardless of page size; details performs exactly two bounded reads.
- `PRESENT` and `MISSING` are exposed through a controlled presentation enum.
- Queries perform no HTTP, matching, filesystem access, or catalog mutation.
- File counts include all associated catalog rows, including rows currently marked `MISSING`.
- Search, filtering, and cursor pagination remain out of scope.
