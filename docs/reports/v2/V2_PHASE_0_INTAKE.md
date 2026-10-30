# DropSort V2 Phase 0 — Intake & Baseline Audit

Audit date: 2026-08-16  
Repository: `D:\\DropSort_ chat\\DropSort`  
Scope: Phase 0 only. No V2 production code, migration, schema change, UI feature, or test change was made.

## 1. Executive Summary

DropSort is a local-first Windows desktop application implemented as a Python/PySide6/SQLite modular monolith. The current executable V1 contains read-only discovery and matching, explicit catalog import, library/details UI, posters, safe Play/Open Folder actions, journaled Move/Rename, Undo/recovery, reconciliation, Relink, Clear Library Data, manual provider search, four themes, English/Arabic localization, and a one-directory PyInstaller release.

The V1 safety boundary is clear and should be preserved. Discovery, matching, catalog import, and physical filesystem mutation are separate application boundaries. Move/Rename is the only normal media mutation path and is journaled before execution; the catalog path is committed only after filesystem verification. Tests cover the required failure and recovery classes in temporary roots.

The main V2 risk is catalog maintenance: `ClearLibraryData` deletes `movies`, `media_files`, and `metadata_cache`. Future preferences, watch events, watchlists, and diary records need logical Movies to survive local-file removal. V2 must establish a personal-data boundary before adding user-facing tracking.

The current database already permits a Movie with no physical file: `media_files.movie_id` is nullable and the read query uses a `LEFT JOIN`. The application path is still file-driven: `RegisterMovieFileCommand`, `ConfirmMovieImport`, Relink, and Organize require a physical file. A metadata-only Movie creation/ensure boundary is required for watchlist, history, liked, blacklisted, and future discovery/import sources.

Repository-local verification passed `1023 passed, 5 skipped, 0 failed` in 54.78 seconds. Branch coverage passed at 95% with 8,846 statements and 1,896 branches in 66.83 seconds. The same command without a repository-local basetemp produced 645 passed and 383 errors because pytest could not enumerate the host-owned temp directory. That is an environment/privilege limitation, not a product assertion failure. The five successful-run skips are symlink/reparse scenarios unavailable under current Windows privilege.

V2 readiness is blocked by the explicit V1 acceptance gate in current project status and by unresolved semantics for clearing local catalog data while preserving personal data. The codebase is otherwise ready for a narrowly scoped database/domain first V2 slice after those gates are accepted.

## 2. Repository / Git Baseline

Evidence:

- Root: `D:\\DropSort_ chat\\DropSort`.
- `Get-Location` and `Resolve-Path .` resolve to that directory.
- The directory is not recognized as a Git repository. `git rev-parse`, branch, commit, and `git status` all returned `fatal: not a git repository`; `Test-Path .git` is false.
- There is therefore no current branch, commit SHA, or Git status. Git provenance is a traceability blocker for production changes.
- The checkout contains source, tests, reports, release files, `DropSort.zip`, `.venv`, `.coverage`, `.build-phase6b`, and ignored caches.

Runtime evidence:

| Item | Current value |
|---|---|
| Host Python | 3.12.10 |
| Project Python | 3.12.10 at `.venv\\Scripts\\python.exe` |
| Project PySide6 | 6.11.1 |
| Project pytest | 9.1.1 |
| Project pytest-cov | 7.1.0 |
| Project PyInstaller | 6.22.0 |
| SQLite | 3.49.1 |
| Package version | `dropsort-media-library` 0.1.0 |
| Declared Python range | `>=3.11` |

`pyproject.toml` declares PySide6 as runtime dependency and pytest/pytest-cov/PyInstaller as optional development/package groups. Host Python lacks project GUI, coverage, and packaging imports; `.venv` has them.

The audit generated ignored `.pytest-phase0`, `.pytest-phase0-coverage`, and `.coverage`. No production file was modified by verification.

## 3. Current Project Tree

```text
src/dropsort/
  application/{bootstrap,configuration,dto,runtime,use_cases}
  core/{file_engine,operations,safety}
  database/{connection,migrations,repositories}
  library/{availability,movies,operations,playback,scanning}
  media/{discovery,matcher,movies,parser}
  metadata/{cache,contracts,providers}
  posters/{providers}
  ui/{common,history,library,main_window,movie_details,organization,
      posters,reconciliation,scan,settings,assets}
tests/
  unit/{application,core,library,media,metadata,posters,ui}
  integration/{application,database,file_engine,media,ui}
docs/{adr,architecture,reviews,source,specs}
packaging/build_release.ps1
DropSort.spec
Phase6BVerifier.spec
tools/phase6b_verify.py
release/DropSort/
```

Meaningful repository contents include 114 test Python files and 27 documentation files, all V1 source packages above, three SQL migrations with down files, assets/fonts/TMDB attribution, PyInstaller specs, release reports, and a current one-directory release. `src/dropsort_media_library.egg-info` is generated packaging metadata. `watched_folders`, `library/scanning`, `media/movies`, and some contracts are reserved or partial; they are not evidence of a completed watcher or V2 personal library.

## 4. Source-of-Truth Assessment

| Source | Status | Assessment |
|---|---|---|
| `src/dropsort/**` | CURRENT | Primary implementation truth. |
| `tests/**` | CURRENT | Current executable contracts and safety expectations. |
| `database/migrations/**` | CURRENT | Only versions 0001–0003 exist. |
| `docs/status/PROJECT_STATUS.md` | MOSTLY CURRENT | Current 1023/5/95% baseline and V1/V2 acceptance gate. |
| `README.md` | CURRENT FOR V1 | Accurate V1 boundaries, with acceptance caveats. |
| `docs/status/RELEASE_CHECKLIST.md` | OPEN | Several packaged/live/acceptance items remain unchecked. |
| `docs/reports/v1/PHASE_6B_FINAL_REMAINING_ISSUES_REPORT.md` | CURRENT HISTORICAL | Latest package/UX evidence, explicitly qualified as user-retest pending. |
| `PHASE_6B_2/3_REPORT.md` | PARTIALLY STALE | Earlier three-theme naming and earlier counts. |
| `docs/reports/v1/PHASE_4D_REPORT.md` | HISTORICAL ONLY | Describes Margarine; current source uses Inter/Noto Sans Arabic. |
| `docs/architecture/**`, ADR-0001..0008 | FOUNDATION/HISTORICAL | Rules remain useful; early phase descriptions are stale. |
| ADR-0009..0014 | CURRENT IMPLEMENTED | Provider, matcher, catalog, query, discovery, and import boundaries match code. |
| `docs/source/The Idea.md` | HISTORICAL ONLY | Contains future TV/watcher/collection and old rating/status concepts. |

Conflicts found:

1. Current code uses exactly four themes: Main, Dark, Slate, Light. Older reports use Deep Ink/Modern Charcoal/Clean Light Blue or Margarine.
2. Newest reports use ready language while `docs/status/PROJECT_STATUS.md` and `docs/status/RELEASE_CHECKLIST.md` retain unchecked acceptance. The checklist/status gate governs V2.
3. Historical idea text proposes User Rating, My Rating 9/10, Favorite, and Watch Later. Current V2 must use NO OPINION, LIKED, and BLACKLISTED, with no personal numeric rating or rating snapshot.

## 5. Current Architecture

DropSort is a modular monolith. Dependency direction is UI -> application -> library/media/core -> contracts; SQL is confined to `database/`; HTTP is confined to `metadata/providers/`. No server, cloud, Docker, PostgreSQL, Redis, Kafka, or watcher dependency is active.

- UI: `ui/main_window/window.py`, `ui/library/*`, `ui/movie_details/*`, `ui/scan/*`, `ui/settings/*`, `ui/history/*`, and dialogs. Widgets consume UI protocols and DTOs.
- Application: `application/bootstrap/desktop.py` composes repositories, providers, use cases, locks, settings, and MainWindow.
- Domain/library: `library/movies/models.py`, `repositories.py`, `queries.py`, availability inspector, and Windows playback boundary.
- Database: `Database`, `MigrationRunner`, SQLite repositories, and `SqliteCatalogUnitOfWork`.
- Media: read-only discovery/parser and deterministic matcher.
- Metadata: provider-neutral contracts, TMDB adapter, session credentials, cache.
- File Engine: `PathPolicy`, `FileOperationService`, `SafeTransferEngine`, durable store, recovery service.
- Runtime: user-scoped paths, redacted rotating logs, and QLockFile/QLocalServer single instance.
- Posters: application-owned disposable cache and asynchronous service.
- Packaging: PyInstaller one-directory spec with migrations, fonts, licenses, icons, Qt plugins, README, and notices.

Reusable UI patterns are immutable DTOs, UI protocols, QtTaskRunner background calls, per-session stale-result tokens, close/shutdown invalidation, semantic theme tokens, localization bindings, and exact physical-file row binding.

V2 should add personal domain/application/database modules beside `library/movies`; personal actions must not route through File Engine, scanner, matcher, or provider HTTP.

## 6. Current Database Schema

The migration runner reaches schema version 3 after 0001, 0002, and 0003.

| Table | Schema facts | V2 implication |
|---|---|---|
| `movies` | id PK; provider/external_id/title/date_added/created_at/updated_at NOT NULL; optional original_title/year/overview/runtime/rating/poster_path; genres JSON text default []; UNIQUE(provider, external_id) | Logical provider/movie identity, currently deleted by Clear Library. |
| `media_files` | id PK; nullable movie_id; current_path/path_key/file_size/status/discovered_at/last_seen_at NOT NULL; technical facts optional; unique path_key; status PRESENT/MISSING; FK movies ON DELETE SET NULL | Physical file, one-to-many; orphan rows structurally allowed. |
| `metadata_cache` | provider/cache_key/payload/fetched_at NOT NULL; expires_at optional; UNIQUE(provider, cache_key) | Provider cache only. |
| `file_operations` | text id PK; Move/Rename paths, state, identity evidence, SHA-256, strategy, errors, timestamps; optional media/reverse FKs ON DELETE SET NULL | Durable physical mutation history; must survive local clear. |
| `watched_folders` | id PK; case-insensitive unique path; role MOVIES/SCAN; enabled; created_at | Reserved schema; no watcher implementation. |
| `settings` | text key PK; value and updated_at NOT NULL | UI settings today; not personal data. |
| `schema_migrations` | runner-created version PK, filename, applied_at | Migration bookkeeping. |

Indexes: `idx_file_operations_state`, `idx_file_operations_media_file_id`, `idx_media_files_movie_id`, and SQLite autoindexes for unique constraints. Database connections enable foreign keys, WAL, and FULL synchronous mode.

0001 creates the foundation schema; 0002 rebuilds `file_operations` for portable text device/inode fields; 0003 adds genres and media-by-movie index. No V2 table, personal preference, watch history, watchlist, diary, rating snapshot, or personal rating structure exists.

## 7. Movie Identity Model

Logical Movie identity is `(provider, external_id)`, enforced by `UNIQUE(provider, external_id)` and handled by `SqliteMovieRepository.get_by_external_id/create/update_metadata`.

Current flow:

1. `ReadOnlyMediaScanner` reads a selected folder and parses a filename.
2. `ProposeMovieImport` queries provider/matcher and returns an informational proposal.
3. `ConfirmMovieImport` loads details for the selected candidate.
4. `RegisterMovieFile` creates or updates the Movie by provider identity, then adds/links the exact current MediaFile in one catalog transaction.

Manual search emits only a candidate; it does not create a Movie or import a file. Import confirmation is explicit and candidate membership is checked. Provider `rating` is TMDB metadata and must remain separate from personal preference.

Executable matcher values in `media/matcher/matcher.py` are `AUTO_MATCH_THRESHOLD=0.85`, `REVIEW_THRESHOLD=0.60`, and ambiguity margin `0.08`. MATCHED never authorizes movement or import.

## 8. Movie ↔ MediaFile Relationship

The schema supports one-to-many relationships:

- `media_files.movie_id` is nullable and indexed.
- `MediaFileRepository.list_for_movie()` returns all rows; tests cover multiple files per Movie.
- Empty media tuples are valid in `MovieDetailsSnapshot` and `MovieDetails`.
- The repository can create a Movie without a file, but the application only composes the file-driven `RegisterMovieFile` path.

File-coupled assumptions to revisit:

- `RegisterMovieFileCommand` requires absolute file path, size, parsed Movie media, and observation time.
- `ConfirmMovieImport` derives from `DiscoveredMedia`.
- `MediaFileRepository.add` requires a Movie id.
- Relink requires a missing MediaFile with a Movie association.
- Organize starts from a MediaFile id.
- Proposal duplicate checking is path-based; logical identity is provider/external id.
- Clear Library deletes Movie rows.
- Current UI has no personal state or fileless personal actions.

The underlying Movie repository/read projection are promising reuse points; V2 needs a metadata-only application contract and personal-data-aware maintenance.

## 9. Application Use Cases & DTOs

| Area | Boundary | Effects |
|---|---|---|
| Scan | `DiscoverMedia`/scanner | Read-only folder scan; no DB/filesystem writes. |
| Match/propose | `ProposeMovieImport`/review | Provider/cache reads and path ownership read only. |
| Manual search | `ManualMovieSearch` | Up to five provider candidates; no import/file effects. |
| Catalog import | `ConfirmMovieImport`/`RegisterMovieFile` | Transactionally creates/updates Movie and adds/links MediaFile; no physical mutation. |
| Library/details | `ListMovies`/`GetMovieDetails` | Read-only DB queries and immutable DTOs. |
| Availability | `ReconcileLibraryFiles` | Reads cataloged paths; updates PRESENT/MISSING and last-seen only. |
| Relink | `RelinkMediaFile` | Validates candidate and updates catalog path/status; no file move/journal. |
| Play/Open | `WindowsLocalMediaActions` | OS launch/Explorer only; no DB or media mutation. |
| Organize | `OrganizeMediaFile` + FileOperationService | Explicit journaled Move/Rename; DB path after FS verification. |
| Undo/recovery | history use cases + RecoveryService | Reverse journal/recovery; original journal preserved. |
| Clear | `ClearLibraryData` | Deletes metadata/media/movies; preserves journal/settings; poster cache only after commit. |
| Settings | language/theme/sidebar/metadata settings | Settings writes; TMDB token memory/environment only. |

Future personal actions need a separate database-only application boundary.

## 10. File-Safety Architecture

The actual lifecycle is:

```text
PathPolicy validate source/destination/roots/identity
 -> journal create PLANNED
 -> VALIDATED with source identity
 -> EXECUTING
 -> SafeTransferEngine prepare
      same volume: exclusive hardlink and verify
      fallback: copy to unique temp, flush/hash, no-overwrite finalize, verify
 -> FS_VERIFIED with destination evidence
 -> reverify source/destination
 -> source removal as final Move/Rename step
 -> transactionally update media path and COMMITTED
```

States are PLANNED, VALIDATED, EXECUTING, FS_VERIFIED, COMMITTED, FAILED, and RECOVERY_REQUIRED. PathPolicy rejects outside roots, links/reparse traversal, same Windows path, destination existence, casefold collision, invalid Rename directory changes, missing source, and identity changes. SafeTransferEngine never overwrites. RecoveryService distinguishes source-only, verified destination-only, both-existing, neither-existing, and unsafe/changed destination; ambiguous files are preserved.

Relink is catalog-only and verifies identity, fingerprint, extension, title/year/technical facts, path ownership, and TOCTOU stability. Scan, match, manual search, Play/Open Folder, reconciliation, and Clear Library do not mutate user media.

### V1 Safety Invariants That V2 Must Never Break

1. Personal actions must never become File Engine actions.
2. No personal/query/import/clear-local operation may delete, move, rename, copy, or overwrite media.
3. Physical Move/Rename remains journaled before mutation, verified before source removal, reversible, and recoverable.
4. Ambiguous states preserve both files.
5. Provider/external uniqueness remains authoritative for logical Movie creation.
6. Clear Local Library must not delete future personal data.
7. TMDB rating remains provider metadata, never personal preference/history.

## 11. Authorization Boundaries

Current separation is:

```text
read-only scan -> discovery/parser
candidate lookup/ranking -> provider/cache + matcher
explicit Add to Library -> catalog transaction
explicit Organize confirmation -> FileOperationService/File Engine
```

The matcher thresholds are 0.85 automatic, 0.60 review, and 0.08 top-two ambiguity margin. `ProposeMovieImport` is informational; `ConfirmMovieImport` validates explicit candidate membership and provider identity. V2 personal tracking must use:

```text
user intent -> personal use case -> personal repository transaction
```

and never pass through catalog-import or filesystem authorization.

## 12. TMDB / Metadata Architecture

`MetadataProvider` exposes provider-neutral search and detail contracts. `TmdbMetadataProvider` uses HTTPS, bearer read-access headers, bounded timeout, controlled 401/403/429/5xx/JSON/network errors, and normalized provider records. `CachedMetadataProvider` stores provider/cache-key payloads; it does not store credentials or personal state.

`SessionTmdbCredentials` selects session token first, then `DROPSORT_TMDB_READ_ACCESS_TOKEN`. Session input is validated in memory only and is not persisted. Repr values are redacted. Runtime logging filters Authorization bearer values and the environment-variable pattern. No real credential was read or printed during this audit.

Future onboarding gaps, intentionally not implemented: friendly Test Connection UX, offline setup/troubleshooting documentation, and explicit packaged credential policy. These must not couple personal data to TMDB.

## 13. UI Architecture

`MainWindow` owns the sidebar and stacked Library, Add Movies, Movie Details, Settings, and Operation History pages. The sidebar is a persisted bounded splitter with compact icon-only mode. Widgets receive UI protocols and DTOs, never SQL/HTTP/File Engine objects.

`QtTaskRunner` runs blocking calls on QThreads and delivers after thread completion. Views/dialogs use session tokens to reject stale results and close/shutdown invalidation; `wait_for_pending_tasks` supports orderly exit. Existing reusable patterns include immutable DTOs, semantic roles, centralized localization/theme, exact physical-file row binding, explicit previews, and one-shot confirmations.

V2 limitation: cards/details expose provider metadata, poster, local availability, and physical-file actions, but no preference, watched state, watchlist, diary, notes, reviews, or tags. Details already supports zero media files.

## 14. Manual TMDB Search Status

Current code implements title plus optional year input, background search, deduplication by provider/external id, maximum five candidates, structured cards with title/year/TMDB ID/provider rating/wrapped overview, compact per-card Select, vertical-only scrolling, loading/empty/error states, stale-result token protection, English/Arabic, RTL/LTR technical handling, and theme-derived styling.

Selection emits a candidate and closes the dialog; it creates no Movie, imports no file, moves no file, and creates no journal row. Add to Library remains a separate explicit click. Direct packaged visual walkthrough and live credentialed noisy-domain lookup remain unchecked acceptance items.

## 15. Theme Architecture

Current user-facing IDs are `main`, `dark`, `slate`, and `light\). Legacy persisted IDs map `deep_ink -> main`, `charcoal -> dark`, and `light_blue -> light`. Internal enum aliases preserve old callers but are not additional themes.

`ui/common/theme.py` defines ColorTokens, THEME maps, fonts, dimensions, stylesheet roles, and `apply_theme`. Light includes settings root, viewport, content, card-host, and scroll surfaces. Future V2 screens must use semantic roles; icon/native controls and attribution SVG are intentional non-token exceptions.

## 16. Localization / RTL Status

Supported language IDs are `en` and `ar`, with English default. Runtime inspection found 266 TextId values and `UiLocalizer().missing_translations()` returned an empty tuple. UiLocalizer provides English fallback, weak widget bindings, runtime retranslation, RTL application direction for Arabic, and `mark_ltr` for paths, filenames, IDs, technical values, and other data. Language is persisted through SQLite settings.

Bundled fonts are Inter Regular/Bold and Noto Sans Arabic Regular/Bold plus OFL texts. `register_application_fonts` uses QFontDatabase and falls back to Segoe UI without networking. Future V2 strings must use this system and explicitly handle technical-value direction.

## 17. Single-Instance Status

`application/runtime/single_instance.py` uses a user/session server name, hashed temp lock path, atomic QLockFile, QLocalServer, bounded `ACTIVATE\\n` protocol, 750 ms activation timeout, primary activation signal, one stale-lock retry, and cleanup on shutdown. Secondary startup exits before database migrations, UI, workers, metadata, or reconciliation. SQLite is never the process lock. This subsystem must remain untouched by personal-library domain work.

## 18. Clear Library / Delete Semantics

`SqliteLibraryMaintenanceRepository.clear_catalog`:

1. blocks while any operation is not COMMITTED or FAILED;
2. counts movies/media/metadata;
3. deletes metadata_cache, media_files, and movies inside BEGIN IMMEDIATE;
4. checks foreign-key integrity;
5. commits or rolls back atomically;
6. preserves file_operations/settings and all physical media;
7. clears only the injected application-owned poster cache after commit.

This is safe for physical media but unsafe as a future personal-data policy. Before V2 data exists, define separate Clear Local Library and Clear Personal Data semantics. Do not add personal-table cascades as a shortcut; prefer restrict/no-action plus explicit personal deletion.

## 19. Test Suite Baseline

Tests cover database migrations/repositories/cache/settings, File Engine collision/identity/failure/cross-volume/restart/ambiguity/reverse behavior, parser/scanner/cancellation/reparse, matcher thresholds, TMDB/provider/cache/redaction, application DTOs/use cases, headless UI/task lifecycle/stale callbacks, localization/themes/single instance, and packaging contracts.

Live commands:

```text
.venv\\Scripts\\python.exe -m pytest -q -W error
  645 passed, 383 errors in 118.26s
  common PermissionError: WinError 5 enumerating host pytest temp root

.venv\\Scripts\\python.exe -m pytest -q -W error --basetemp=.pytest-phase0
  1023 passed, 5 skipped, 0 failed in 54.78s

.venv\\Scripts\\python.exe -m pytest --cov=src/dropsort --cov-branch
  --cov-report=term-missing -q -W error --basetemp=.pytest-phase0-coverage
  1023 passed, 5 skipped, 0 failed in 66.83s
  95% total branch coverage
```

Five skips are symlink/reparse tests blocked by WinError 1314/current account privilege. Tests used sandbox/temp paths and no real media library or real credential. No tests were changed.

## 20. Packaging / Release Baseline

Packaging is one-directory PyInstaller. `DropSort.spec` includes migrations, fonts/OFL texts, TMDB logo, DropSort icon, licenses, and `pathex=src`; excludes pytest/coverage. `build_release.ps1` invokes project PyInstaller and copies README/notices beside the executable. `Phase6BVerifier.spec` and `tools/phase6b_verify.py` describe disposable sandbox verification; they were inspected, not rebuilt/run here.

Current artifact inspection:

| Artifact | Evidence |
|---|---|
| `release\\DropSort\\DropSort.exe` | 2,353,470 bytes; SHA-256 `23CD90AED24E5A754796E902827327A9EDBED9D5A89DC12E7D356018076E62AA` |
| `DropSort.zip` | 1,389,924 bytes; SHA-256 `6A7324213BBDD65161858B47D7B00C87CA50E26A922F51E3E9DA9D2A5129E7C7` |
| release directory | 190 files; 120,116,153 bytes |

Release root contains `_internal`, executable, README, and THIRD_PARTY_NOTICES. Migrations, fonts, TMDB attribution SVG, Qt Windows plugin, README, notices, and licenses are present. No rebuild or new packaged UI claim was made.

Open packaging/release items are live TMDB, direct packaged visual walkthrough, clean machine/VM, signing, project license, and final acceptance.

## 21. Documentation Freshness Audit

Current/useful: README, PROJECT_STATUS, ADR-0009..0014, file-safety invariants, ADR-0003/0006/0007.

Partially stale/historical: early architecture docs describe reserved modules; The Idea proposes TV/watcher/collections/old statuses and User Rating; Phase 4D describes Margarine; Phase 6B.2/6B.3 describe earlier theme names and counts. Newest reports use ready language but retain explicit user-retest qualifiers. Do not use historical rating plans as V2 authority.

## 22. Known V1 Issues

- V1 release freeze remains blocked pending user acceptance, especially live noisy-domain TMDB and packaged visual/clear-library follow-up.
- Unqualified pytest uses an ACL-inaccessible host temp root; repository-local basetemp is reproducible.
- Five symlink/reparse tests are skipped under current privileges.
- No separate clean machine/VM verification.
- Unsigned executable and no explicit DropSort project license.
- Cross-volume/ambiguous recovery are simulation-tested, not manually exercised against real ambiguous files.
- Live TMDB/poster behavior was not exercised with a credential in this audit.

None authorizes changing V1 code in Phase 0.

## 23. Technical Debt Relevant to V2

1. Clear catalog deletes Movie roots and has no local/personal distinction.
2. `movies` combines durable logical/provider metadata with local-library maintenance assumptions.
3. There is no metadata-only Movie application use case.
4. Library DTOs expose no personal projections and use `date_added` for local-library ordering.
5. Zero-file Movie projections work but have no personal actions/state.
6. `watched_folders` is only a schema reservation, not watch history or a watcher.
7. Documentation contains phase-history conflicts and obsolete rating/theme concepts.
8. Git metadata is absent from this checkout.

## 24. V2 Personal Library Impact Map

REUSED UNCHANGED: File Engine/safety/operations/recovery; scanner/parser/matcher; provider/cache/poster infrastructure; Database/migration mechanics; QtTaskRunner; single instance; themes/localization/fonts.

EXTENDED: Movie repository/model/query projections; application bootstrap/UI protocols; library maintenance; cards/details; local recency semantics.

NEW: personal domain models, repositories, queries, migrations, preference/watchlist/watch-event use cases, DTOs, and tests.

HIGH RISK TO MODIFY: `clear_catalog`, `ClearLibraryData`, Movie retention/garbage collection, library read projections, details refresh, and shared bootstrap locks.

MUST NOT BE TOUCHED FOR FIRST SLICE: File Engine, PathPolicy, operation states/recovery, matcher thresholds, TMDB auth/redaction, scanner/parser, existing V1 organize/Undo/Relink/playback/single-instance.

## 25. V2 Movie-Without-Local-File Constraints

Already compatible: movies has no file NOT NULL constraint; MovieRepository.create needs metadata/time only; Movie has no MediaFile field; media_file.movie_id is nullable; library SQL uses LEFT JOIN; empty details media tuples are valid.

Blocking/file-oriented: only RegisterMovieFile is composed for Movie creation; its DTO requires path/size/ParsedMedia; ConfirmMovieImport is discovery-derived; no personal state/repository/query/DTO/UI exists; Clear Library deletes Movies; Relink/Organize require MediaFiles; proposal duplicate checking is path-oriented.

Safe enabling contract: add metadata-only `EnsureMovie`/CreateMovie using provider/external uniqueness and no filesystem call. Use it for future watchlist/history/liked/blacklisted/discover/import sources.

## 26. Personal Preference Model Recommendation

Use one mutually exclusive nullable state:

```text
NULL       = NO OPINION
LIKED
BLACKLISTED
```

Recommended schema boundary: separate `movie_preferences` keyed by `movie_id`; absence means NO OPINION. Use constrained text values LIKED/BLACKLISTED. A separate table protects provider/local catalog separation and clear semantics. Do not use independent booleans. Do not create numeric ratings, stars, `rating_snapshot`, averages, distributions, or rating-based diary history. Existing `movies.rating` remains provider metadata.

Prefer FK ON DELETE RESTRICT/no action; explicit personal-data deletion must remove the row.

## 27. Watch History Model Recommendation

Use normalized individual events:

```text
watch_events(id, movie_id, watched_at, rewatch)
```

Notes may wait for the diary phase. No `rating` or `rating_snapshot`. Derived values are EXISTS for Watched, COUNT for Times Watched, and MAX(watched_at) for Last Watched. Preserve multiple events rather than flattening to watched/watch_count/last_watched fields. Add an index on movie/time and deterministic timestamp/id ordering. Use restrict/no-action FK deletion.

## 28. Ready-to-Watch Derivation

Use derived state, never a stored boolean:

```text
Watchlist
AND PRESENT MediaFile exists
AND no WatchEvent exists
```

The current repositories can support this after personal queries are added. Use LEFT JOIN/EXISTS so a fileless watchlisted Movie is not mistaken for ready local media. Recompute after Relink, reconciliation, local clear, and new watch events.

## 29. Date Semantics Recommendation

Actual meanings:

- `movies.date_added` is the `now` passed to MovieRepository.create and remains unchanged by metadata refresh. Current application usage means first logical catalog insertion, not filesystem timestamp.
- `created_at` starts with date_added; `updated_at` changes on metadata update.
- `media_files.discovered_at` is set on first row insertion and is not changed by refresh/relink.
- `media_files.last_seen_at` changes when observed present, refreshed, relinked, or path-updated after organization; marking missing does not fabricate a last-seen value.

Recommendation: preserve date_added as first known to DropSort; let metadata-only creation set it for a first-known personal Movie. Use media discovered_at/last_seen_at for physical recency/presence. Add explicit personal timestamps for watchlist-added/first-watched rather than overloading current columns.

## 30. Clear Library V2 Risk & Recommendation

Current Clear Library is physically safe but deletes logical Movies. Future personal rows would be orphaned, cascaded, or force an unsafe choice.

Recommended future policy:

1. Clear Local Library removes MediaFiles/local observations and local-only records.
2. Movies referenced by any personal row survive.
3. Movie garbage collection occurs only by explicit policy when no personal records and no local media remain.
4. Operation history/recovery remains preserved.
5. Poster cleanup remains application-owned.
6. Clear Personal Data is separately confirmed and separately implemented.

Decide metadata retention for personal-only Movies before migration. Do not use Clear Local Library as personal-data deletion.

## 31. V1 Systems That Must Remain Untouched

For the first V2 slice leave untouched unless a concrete test proves otherwise:

- `core/file_engine/transfer.py`
- `core/safety/path_policy.py`
- `core/operations/service.py` and `recovery.py`
- operation store/file operation repository
- `media/discovery/**`, `media/parser/**`, `media/matcher/**`
- `metadata/providers/**` and credential redaction
- single instance
- existing organize, Undo, recovery, Relink, playback, poster, scanner, manual search, UI, theme/localization, and packaging behavior.

No V2 screen redesign, watcher, threshold change, schema alteration outside an approved new migration, or production refactor belongs in Phase 0.

## 32. V2.0 Recommended Phase Breakdown

1. **Phase 1 — Personal Library Domain + Schema:** preference/watch-event/watchlist schema, domain contracts, repository adapters, upgrade tests, and metadata-only Movie ensure. No UI/File Engine.
2. **Phase 2 — Personal Tracking Use Cases:** Like, Blacklist, clear preference, Watch, Rewatch, Watchlist add/remove, derived queries; database-only.
3. **Phase 3 — Library/Movie Details Integration:** optional personal projections, Watched/count/last date, Watchlist, local availability, Ready-to-Watch.
4. **Phase 4 — Diary/Notes/Tags:** normalized personal extensions after watch/retention semantics stabilize.
5. **Phase 5 — UX/Localization/Packaging:** user-facing controls, RTL/themes, packaged acceptance, and documentation.

## 33. Exact Recommended First Implementation Slice

Smallest coherent first slice: **V2 Phase 1A — Personal Library Foundation**, database/domain/application contracts only. No new screen or filesystem operation.

Proposed new migration, not to be created in Phase 0:

```text
0004_personal_library_foundation.up.sql
0004_personal_library_foundation.down.sql
```

Minimum conceptual tables:

```text
movie_preferences(
  movie_id PK REFERENCES movies(id) ON DELETE RESTRICT,
  preference CHECK(preference IN ('LIKED','BLACKLISTED')),
  created_at, updated_at
)

watch_events(
  id PK, movie_id REFERENCES movies(id) ON DELETE RESTRICT,
  watched_at, rewatch CHECK(rewatch IN (0,1))
)

watchlist_entries(
  movie_id PK REFERENCES movies(id) ON DELETE RESTRICT,
  added_at
)

INDEX watch_events(movie_id, watched_at, id)
```

No personal rating, star, rating_snapshot, average, or distribution.

Expected domain files: `library/personal/models.py`, `repositories.py`, `queries.py`, `__init__.py`. Expected DB file: `database/repositories/personal.py`, plus exports and 0004 SQL. Add a metadata-only EnsureMovie/CreateMovie application boundary on existing MovieRepository identity logic.

Required tests: fresh 0001->0004 migration; V1 database upgrade without loss; idempotent startup; invalid DB preservation; fileless Movie creation; provider identity idempotency; preference exclusivity and clear-to-NULL; multiple watch events/count/last; watchlist idempotence; ON DELETE RESTRICT; database-only architecture imports; full existing suite green.

Expected untouched: File Engine, safety, operation states/recovery, scanner/parser/matcher/provider, existing V1 UI, theme/localization, packaging, and existing tests except additive tests.

## 34. Risks / Blockers

Concrete blockers:

1. `docs/status/PROJECT_STATUS.md` explicitly says Release Freeze or V2 must wait for user acceptance; `docs/status/RELEASE_CHECKLIST.md` still has unchecked live/packaged acceptance.
2. Clear Library deletes Movies and has no local/personal retention policy.
3. Git metadata is absent, so production change traceability is not normal.

Non-blocking but required to record: host temp ACL issue, five symlink privilege skips, no clean VM, unsigned executable/no project license, simulation-only cross-volume/ambiguous recovery, and no live credentialed TMDB run.

Adversarial review:

- No hidden physical mutation path was found outside explicit journaled Move/Rename; poster cleanup is application-owned and Relink is catalog-only.
- The material personal-data destruction path is `clear_catalog()` deleting `movies`.
- Movie/local-file coupling is concentrated in file-driven import/Relink/Organize; repository/read projections already tolerate zero files.
- Provider identity uniqueness controls duplicate logical Movies and must be preserved.
- Credential redaction and stale-result/thread protections are present.
- Windows path/case protections must not be bypassed by new physical-path DTOs.
- Historical documentation, not executable code, contains most claims of unimplemented V2 features.

## 35. V2 Readiness Decision

The current V1 architecture is understood and has a reproducible green source baseline under a repository-local sandbox. The File Engine, transaction boundary, provider abstraction, UI task model, localization/theme system, and packaging boundaries are suitable foundations.

V2 is not ready to begin production implementation because current project status gates V2 on remaining V1 acceptance and because current Clear Library behavior would delete logical Movie roots required by personal data. Restore Git provenance and accept/document local-versus-personal retention first. Then implement only Section 33 and rerun the full V1 gate.

No V2 production code, migrations, schema changes, or UI features were implemented during Phase 0.

V2 READINESS: BLOCKED
