# ADR-0011: Transactional Movie Catalog Ingestion

**Status:** Accepted for Phase 3A

**Date:** 2026-08-11

## Context

DropSort needs to persist normalized movie metadata and associate verified physical-file facts with
logical movies. The existing Phase 1 schema already provides `movies`, `media_files`, a nullable
one-to-many foreign key, provider-scoped movie uniqueness, and Windows-oriented `path_key`
uniqueness. It lacks only genre persistence and an index for listing files by movie.

Catalog persistence must not reinterpret match confidence or authorize filesystem operations.

```text
MATCHED != AUTHORIZED TO MOVE
```

## Decision

Keep catalog domain models and repository protocols under `library/movies`. Keep SQL and concrete
SQLite repositories under `database/repositories`. The application ingestion use case depends on a
catalog-specific Unit of Work protocol, not `sqlite3` or concrete repositories.

Migration `0003_movie_catalog` adds:

- `movies.genres TEXT NOT NULL DEFAULT '[]'`, containing normalized UTF-8 JSON arrays of strings.
- `idx_media_files_movie_id` for one-to-many file queries.

The down migration refuses to remove the genres column while any movie contains non-empty genre
data.

Movie identity remains `(provider, external_id)`. File identity continues to use the existing
case-folded `path_key`; no second path-normalization policy is introduced.

`RegisterMovieFile` receives an explicit association command containing provider-neutral
`MovieMetadata`, `ParsedMedia`, and caller-verified path/size/time facts. It does not accept a match
threshold, infer acceptance from confidence, access the filesystem, or change a stored path.

One SQLite `BEGIN IMMEDIATE` transaction covers movie create/refresh and media-file create/link.
Repeated path registration refreshes file size, technical fields, presence state, and
`last_seen_at`, while preserving `current_path`, `path_key`, `movie_id`, and `discovered_at`.

Attempting to associate a file already owned by another movie raises
`MediaFileAssociationConflict` and rolls back the complete ingestion transaction.

## Consequences

- Repeated ingestion produces one movie and one media-file row.
- One movie can own multiple physical files.
- Metadata refresh changes descriptive movie fields only.
- File association is explicit and cannot silently switch movies.
- Existing Phase 1 media rows with no movie association can be linked explicitly.
- Database rows remain catalog facts; no filesystem existence check or mutation is performed.
- Director and cast remain available in metadata DTOs but are not persisted in this V1 catalog
  migration because Phase 3A does not require them.
