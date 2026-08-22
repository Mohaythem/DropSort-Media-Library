# ADR-0014: Proposal-Only Matching and Explicit Movie Import

**Status:** Accepted for Phase 3D

**Date:** 2026-08-11

## Context

DropSort now has read-only discoveries, cached provider-neutral metadata, deterministic match
decisions, and transactional catalog ingestion. Connecting them must not turn a high-confidence
match into implicit catalog persistence or filesystem authorization.

```text
MATCHED != CATALOG IMPORT AUTHORIZATION != FILESYSTEM AUTHORIZATION
```

## Decision

Expose two separate application entry points.

`ProposeMovieImport` accepts one immutable discovery. Obvious TV, unknown, and scanner-error items
short-circuit without metadata calls. Movie candidates first use the catalog's existing Windows-safe
path lookup; an existing row returns `ALREADY_IN_LIBRARY`. Otherwise the use case performs a
provider-neutral title/year search, calls the pure matcher, and returns an immutable
`MovieImportProposal`.

Proposal states are `MATCH_PROPOSED`, `REVIEW_REQUIRED`, `NO_MATCH`, `METADATA_UNAVAILABLE`, and
`ALREADY_IN_LIBRARY`. Stable reasons distinguish offline, authentication, rate-limit, response,
non-movie, and matching outcomes. Proposal status, embedded `MatchDecision`, selected candidate, and
ranked candidate tuple must remain coherent.

`ConfirmMovieImport` is a separate explicit command. It requires a confirmable prior proposal and a
chosen candidate contained in that proposal. It fetches normalized detail metadata for the exact
provider identity and delegates to Phase 3A's transactional `RegisterMovieFile` boundary using the
discovery's observed path and size. It has no matcher or confidence dependency.

## Consequences

- Merely producing `MATCHED` or `MATCH_PROPOSED` performs zero catalog writes.
- Review-required proposals can be confirmed only through an explicit caller command.
- Repeated confirmation remains idempotent through Phase 3A.
- A stale proposal cannot silently reassign a file; Phase 3A's association conflict rolls back.
- Provider failures are controlled and normal tests require no live HTTP.
- Neither proposal nor confirmation contains a destination path, move plan, or file authorization.
- Catalog imports record the file at its existing path and perform no filesystem mutation.
