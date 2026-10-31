# TBD Movies — Release 2 / V2 Roadmap

## Status and authority

This is the dedicated planning document for Release 2 / V2 of the active WinUI application. It records ideas for work **after Release 1 / 1.0 correctness and release hardening**. It authorizes no implementation and declares no Release 1 gate closed.

- [FUTURE_PLAN.md](FUTURE_PLAN.md) remains the source of truth for Release 1, its scope freeze, correctness requirements, and release gates.
- This document governs V2 idea grouping and priority. Where it differs from the earlier post-1.0 backlog in `FUTURE_PLAN.md`, use this document for V2 planning only.
- [PHASES.md](PHASES.md) records historical implementation checkpoints. [The release manifest](packaging/RELEASE_MANIFEST.md) records artifact evidence and open release risks. Neither a historical checkpoint nor this roadmap proves current review coverage or release readiness.
- Older Python documents and `docs/reports/v2/` describe earlier product/development states. Their use of “V2” does not identify this future WinUI Release 2.
- No dates or complete feature set are committed. **V2 Core** means a priority direction, not a promise to ship every item. Each selected item needs a separate design, bounded scope, acceptance criteria, and explicit implementation authorization.
- This planning task changes no application code, schema, dependencies, or persistence contracts.

## Product direction and shared constraints

**Native Windows + local-first + real-file intelligence + safe file management.**

V2 must deepen the value of the user's local media library. Core library use, local search, file understanding, and personal state must remain useful without cloud services. Metadata providers are optional enrichment.

- Preserve the native Windows / Fluent direction, accessibility, English LTR, Arabic RTL, and supported themes.
- Keep logical Movie/TV identity separate from physical file, path, version, edition, and copy identity. Preserve lists, watch history, and user edits during repair or identity changes.
- Distinguish missing files, unavailable storage, and access/I/O errors. Missing or offline media must remain represented until an explicit user action changes the catalog.
- File operations remain within approved roots. Reject unsafe link/reparse traversal, validate source identity, never overwrite a destination, journal before mutation, and verify the destination before any permitted source removal.
- Require explicit user confirmation for every destructive operation. Copies and backup copies are not destructive duplicates by default. Suggestions must explain evidence and uncertainty.
- Prefer read-only inspection and preview. Interrupted physical operations must be recoverable. Ambiguous states preserve both files. Never promise Undo without a safe reverse operation.
- Do not silently change Clear Library Data semantics or current Application/Domain boundaries. Future schema or architecture proposals require separate design approval.

## V2 Core

These areas have the strongest connection to local media, storage, identity, and file safety. Scope selection follows the dependencies below; section order is not a delivery schedule.

### 1. Library Health & Storage Intelligence

- Library Health dashboard with integrity summaries and actionable files requiring attention.
- Missing/unavailable files, problematic paths, access errors, and suspicious or duplicate registrations.
- Storage status and usage by drive/root, including removable/offline storage and low-space information where reliable.
- Local-library composition and physical storage insights. Distinguish logical titles from physical copies when reporting counts and bytes.
- Explain how and when each finding was observed. Do not present stale availability hints as current evidence.

Build on the existing Check Library and availability contracts. This does not close Release 1 risks such as Home / Recently Watched stale availability hints, UNC timeouts, or real removable-drive verification.

### 2. Multiple Versions / Editions / Copies

Expose the Movie → MediaFiles model as a clear user-facing concept:

- Resolution versions such as 1080p / 4K.
- Sources/encodes such as Blu-ray / WEB-DL / Remux.
- Theatrical / director's cut / extended editions.
- Multiple copies across drives, including intentional backup copies.
- Clear labels, a preferred/default version, safe version switching, and version-aware Play/Open.
- Duplicate-copy intelligence and reviewed merge behavior that preserves useful copies and user state.

Separate edition differences from encoding differences and physical copies. Validate how version preference behaves when a drive is unavailable. Identity correctness belongs to Release 1 where already required; these new controls belong to V2.

### 3. Duplicate & File Intelligence

- Probable duplicate files and the same movie with different encodes.
- Probable identical physical content on multiple drives.
- Filename/path similarity, file-size evidence, and future fingerprint-assisted identification.
- Suspicious catalog duplicates and safe merge suggestions.
- Distinguish catalog-record merges from physical file deletion. Neither a suggested merge nor a fingerprint authorizes deletion.

Design evidence strength, review of uncertain results, and fingerprint cost/privacy before implementation. Preserve stable identity and personal state. Never classify copies as disposable by default.

### 4. Smart Relink

- Detect probable moves or renames made outside TBD Movies.
- Propose relinking rather than permanently losing the registered item.
- Use filename, size, metadata, and future fingerprints as supporting evidence.
- Preview the old/new path, conflicting registrations, evidence, and uncertainty before explicit confirmation.
- Preserve Movie/TV identity, lists, history, and user state.

Relink must not move/delete media. Do not apply automatic destructive relinking or choose among ambiguous candidates silently. Validate path ownership, source identity, and unavailable-drive behavior.

### 5. Safe Organize / Rename

- Optional folder/file naming templates for Movies and TV.
- Read-only preview and dry-run with exact source/destination paths.
- Collision detection, approved-root validation, and companion-file planning where supported.
- Conservative move/rename through the operation journal, with recoverable interruption handling.
- Undo only where current filesystem state allows a safe reverse operation.

Extend existing safe organize behavior. Do not perform aggressive automatic organization. Template changes and bulk actions require an explicit reviewed plan before execution.

### 6. Operation History / Undo

- Clear history for move/rename, relink, organize, merge, metadata identity changes, and safe library-removal operations.
- Record the affected logical item, paths where relevant, intent, outcome, and available recovery actions.
- Preserve the distinction between a physical-operation journal and catalog/metadata history. Relink alone is not a physical move.
- Offer Undo only after verifying reverse-operation preconditions. Explain when it is unavailable or unsafe.

Never promise universal Undo. Merge, metadata refresh, or removal may need retained prior state and a separate design before reversal is feasible. Library removal must not silently delete physical media.

### 7. Smart Lists

Rule-based collections derived from local library state, alongside custom lists:

- Unwatched; Watched this year; 4K Movies; movies on a specific drive.
- Missing Files; Review Match; No Metadata; Genre/Year combinations.
- TV shows with missing episodes, where expected episode information is known.

Share query semantics with Advanced Search. Define how unknown metadata and unavailable storage affect membership. Saving a rule must not alter files or imply metadata is required.

### 8. Advanced Search & Filters

- Movie / TV, year, genre, watched state, and metadata state.
- Storage root, availability, lists, and resolution/technical properties where available.
- Missing episodes/subtitles where reliable evidence exists.
- Combinations of filters, with the option to save a filter as a Smart List.

Queries operate on the local library. Unknown properties must remain distinct from negative results. Design indexed queries, cancellation, and large-library responsiveness before selecting scope.

### 9. TV Library Intelligence

- Stronger Show → Season → Episode navigation, episode availability overview, and season completeness.
- Up Next and improved Continue Watching based on local watch state.
- Missing-episode intelligence, better Specials handling, and repair/relink tools.
- Multiple episode files/versions where justified.
- Retain Smart TV Batch Import from the existing backlog as a candidate within this area: grouped matching and reviewed bulk import through the normal Add Media pipeline.

Keep one show-level library card. Do not regress to one card per episode. Distinguish known missing episodes from unknown expected coverage; unavailable drives are not proof of a missing episode. Accurate playback-position resume remains Later until a reliable position source exists.

### 12. Metadata Maintenance

- Refresh Metadata, poster-only refresh, and Change Match.
- Metadata lock and local title/overview/year/poster edits.
- Protect user edits from provider refreshes; define lock/provenance rules before introducing local edits.
- Manual artwork selection is a candidate requiring provider, cache, licensing, and offline behavior design.
- Preserve local identity and user state when changing a provider match.

Provider metadata remains optional and replaceable. These controls do not require a speculative provider/plugin framework. Failed provider requests must not remove successful local registration or overwrite protected edits.

### 13. Backup / Restore / Portability

Backup should cover applicable catalog data, custom lists, watch/history state, preferences, local metadata, metadata identity, and media-root configuration. **Do not back up the video files themselves.**

Before exposing Restore broadly, require:

- Validated backup identity, format/version compatibility, and integrity checks.
- Crash-safe restore with rollback/recovery and explicit coordination with active writes/file operations.
- Backup privacy/ACL hardening and clear disclosure of included personal data and paths.
- Preservation of user data when restore fails or is interrupted.

Build on existing protection primitives without assuming they satisfy these gates. Portable export/import or backup manifests remain candidates after the protection foundation. Restoring data must not move, delete, or assume the availability of video files.

## V2 Candidates

These ideas need evidence, design, or performance validation before selection for the first V2 release.

### 10. Subtitle Intelligence

- Detect local external subtitle files and associate them with a Movie/Episode.
- Detect language only where reliable; preserve an unknown/uncertain state.
- Show subtitle availability and provide missing-subtitle filters.
- Understand companion files for reviewed organize/rename workflows without silently modifying them.

Define filename association, episode/version matching, supported formats, and ambiguity handling first. **No automatic subtitle downloading in the initial V2 scope unless separately approved.**

### 11. Folder Watcher

- Optional watching of configured media roots.
- Detect newly added files, probable renamed/moved files, and removals.
- Offer review/import/relink through the normal safe TBD Movies review pipeline.
- Wait for copying/downloading to complete; tolerate duplicate, out-of-order, missed, or noisy events.

Watcher events are hints, not catalog truth. Revalidate current state before a reviewed action. Do not silently mutate the catalog based solely on events, delete records on removals, or interpret a disconnected drive as mass deletion.

### 14. Library Views / Presentation

- Grid / List toggle and compact/horizontal cards.
- Density options and richer technical information views.
- Artwork/poster viewer with zoom, pan, Fit, and 100%.
- Improved large-library browsing with virtualization and responsive filtering.

Preserve native Windows / Fluent controls, accessibility, themes, and LTR/RTL behavior. Validate real browsing needs and scale before expanding presentation scope.

## Later / Post-V2 Exploration

These must not drive the initial V2 release. Recording them does not authorize implementation.

- **Discover / non-local catalog:** optional Trending, Popular, Upcoming, Top Rated, or search outside the library. Keep this secondary and clearly separate from locally registered media. TBD Movies must not become primarily a generic TMDB browser.
- **Letterboxd:** optional import/export and watched/history interoperability. Preserve local ownership and resolve identity conflicts explicitly. No social-platform direction.
- **Statistics / analytics:** limited viewing trends, library composition, storage statistics, and watch-history summaries. Useful local storage/integrity summaries already belong to Core; analytics-heavy expansion remains deprioritized.
- **Provider / plugin architecture:** consider only if a concrete provider or product need justifies complexity. Keep provider metadata replaceable without committing to a plugin system.
- **Accurate playback-position resume:** requires a reliable external-player position source or a separately approved player design. Existing watch state alone must not be presented as measured playback position.

## Avoid / Deprioritize

- Social features, friends/followers, or a comments/reviews platform.
- Accounts or cloud sync required for library use.
- A streaming playback platform or recommendation engine as the core product.
- Aggressive automatic file operations, silent destructive merges, or automatic media deletion.
- Speculative plugin architecture or provider infrastructure without a real need.
- Discover, tracking, or analytics expansion that makes local-file intelligence secondary.

## Prioritization and design dependencies

Choose a small, coherent V2 scope after the Release 1 gates. Rank proposals by value to real local libraries, safety, evidence quality, offline usefulness, and cost. Do not treat the entire list as one release commitment.

- Reliable identity and availability underpin versions, duplicate intelligence, health, TV completeness, and relink.
- Define evidence and optional fingerprints before using them for duplicate/relink suggestions. Fingerprints are not permission to mutate files.
- Define shared local query semantics before Smart Lists; subtitle/technical filters depend on reliable inspection data.
- Define metadata locks/provenance before local editing and refresh workflows.
- Reuse conservative operation/recovery contracts before templates, expanded history, or Undo. Re-check reverse safety at execution time.
- Validate backup/privacy/recovery gates before broad Restore or portability workflows.
- Watchers feed existing reviewed actions only after those actions are reliable. Presentation changes must preserve large-library performance.

## Overlap and reconciliation with existing plans

- `FUTURE_PLAN.md` already records most areas in its post-1.0 backlog: versions, duplicates, Smart TV Batch Import, watcher, relink, health/storage, subtitles, metadata, backup/restore, queries/lists, organize/history, and views/artwork. This document expands and regroups those ideas; it does not reclassify Release 1 correctness work as V2.
- Existing Tier 0 identity/import correctness stays in Release 1. Existing organize, history, watch-state, availability, and backup primitives are foundations to evaluate, not claims that the V2 capabilities are already complete.
- The earlier backlog places richer Discover and analytics-heavy dashboards under Avoid. The current direction permits **limited Later exploration** while retaining the identity constraints and rejecting discovery-first or analytics-first expansion. Release 1 exclusions remain unchanged.
- Manual artwork selection moves from the earlier Later list to a V2 candidate within Metadata Maintenance. It is not a shipping promise. Accurate playback-position resume remains Later.
- The older backlog's numbered post-1.0 sequence is an earlier proposal. The Core/Candidates/Later/Avoid grouping and dependencies here guide V2 selection without committing to that sequence.
- Root `README.md` and `docs/README.md` still point to older Python product material. For the active WinUI releases, use `FUTURE_PLAN.md` for Release 1 and this document for Release 2 planning. The historical documents are preserved.
- Existing Release 1 status wording is not fully synchronized: the forward plan records completed trust work while later text still calls it “not started.” This roadmap neither repairs those historical status lines nor uses them to claim readiness.

No feature is implemented by this document. Release 1 risks remain open until verified evidence closes them. Implementation, schema design, tests, builds, installer work, commits, and publishing require a separate authorized task.
