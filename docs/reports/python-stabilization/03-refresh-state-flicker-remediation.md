# Refresh State and Flicker Remediation

Date: 2026-08-25
Branch: `codex`
Starting SHA: `83d0d8e4ff9ab3975581bb5f2ec0da83cc162d58`

## Scope and preserved contracts

This pass stabilizes UI ownership, incremental presentation, and Clear Library semantics. It does not port the approved V7 prototype, begin Poster Phase 3, package a release, add startup reconciliation, or move/delete/copy physical media. Check Library remains explicit and manual. Registration remains offline-first and TMDB enrichment remains a separate optional transaction on the same MovieId.

## Runtime flicker trace

A temporary, uncommitted offscreen harness used a disposable SQLite database, disposable profile/cache, deterministic local data, no network, and no TMDB credential. The harness was removed before implementation.

| Case | Library loads | set_items | show_items | relayout | card creates | poster request/delivery/apply | poster fetch |
|---|---:|---:|---:|---:|---:|---:|---:|
| A: empty startup | 1 | 1 | 2 | 1 | 0 | 0 / 0 / 0 | 0 |
| B: one card, uncached | 1 | 1 | 2 | 4 | 1 | 1 / 1 / 1 | 1 |
| C: four cards, uncached | 1 | 1 | 2 | 4 | 4 | 4 / 4 / 4 | 4 |
| D: four cards, cached | 1 | 1 | 2 | 4 | 4 | 4 / 4 / 4 | 0 |
| E: four cards, poster delivery suppressed | 1 | 1 | 2 | 4 | 4 | 0 / 0 / 0 | 0 |

The Library snapshot was built before `window.show()`; no post-show full Library source reload occurred. Relayout still ran four times with poster delivery suppressed, so poster delivery is not the sole startup mechanism. Cached posters still travel through asynchronous per-card delivery, but avoid network fetch. The proven startup contributors are initial card/grid construction plus repeated geometry passes; uncached/cached poster delivery adds an individual-card repaint.

The Personal trace reproduced both confirmed defects before implementation: a late LIKED result painted WATCHLIST, and a BLACKLISTED failure left the late LIKED card visible. The implemented ownership tests now reject both sequences.

## Per-card poster presentation follow-up

Starting SHA: `7f4dac7449dd4f518d9b4b24a8f928583ce389b5`.

The remaining visible refresh was traced to the exact UI-thread chain `poster completion -> MovieCard.apply_poster() -> QLabel.setPixmap() -> Window UpdateRequest -> card/poster paint`. The number of poster-correlated visible cycles scaled with the visible card count: approximately `1 / 3 / 5` for `1 / 3 / 5` cards.

`MovieCard` still validates and decodes each asynchronous result immediately. A grid-owned presentation coordinator now stages only the final pixmaps of visible cards, preserves request tokens and stable MovieId/MovieCard identity, and applies the ready pixmaps together when the visible request wave completes. A single-shot 100 ms maximum-wait timer bounds a partial wave when another request is slow or fails; it does not block the UI. Results completed before initial visibility classification are staged, which closes the cached-poster startup race. Hidden-card results may be applied immediately because they cannot repaint the visible page.

A native Qt diagnostic used production `LibraryView`, `PersonalLibraryView`, `MovieGrid`, and `MovieCard`, deterministic local PNG assets, no network, and no user database/cache. Measured poster presentation batches and top-level Window `UpdateRequest` counts were both `1 -> 1`, `3 -> 1`, `5 -> 1` in Library, and `5 -> 1` in Personal Library. Every final poster loaded and every card object retained identity. The temporary diagnostic process and files were removed. Computer Use screenshot capture could not attach because its Windows helper failed with the host ACL error `apply deny-read ACLs`; the native-window event measurement completed independently.

## Remediation by finding

| Finding | Result |
|---|---|
| P0.1 cross-tab content on failure | Fixed. An uncached target clears the shared visible source and owns a localized loading/error state. Only same-section stale data may remain. |
| P0.2 stale result paints another tab | Fixed. Every request captures generation and section. A result may cache only for its generation and may paint only while it owns the current section. |
| P0.3 Clear reuses stale snapshots | Fixed. Clear success explicitly discards Library cards/source, all Personal caches, details state, and search suggestions before one authoritative Library load. |
| P0.4 incomplete active clear | Fixed. One SQLite transaction deletes metadata cache, Watch Events, personal state, MediaFiles, and Movies; unresolved operations still block the clear. |
| P1.1 Personal two-stage presentation | Fixed. Fresh same-section cache is immediate; uncached targets use target-specific loading; failed targets cannot show another section. |
| P1.2 poster delivery | Fixed at the presentation boundary. Visible ready pixmaps are coalesced by the grid into one bounded presentation batch; loader/cache/network behavior is unchanged. |
| P1.3 MovieCard recreation | Fixed. Same MovieId calls `update_item()`; title/year/rating/availability/file counts update in place. Poster work restarts only when provider/reference changes. |
| P1.4 registration plus enrichment | Fixed at presentation boundary. The second same-MovieId DTO updates the locally created card rather than replacing it. Transactions remain separate. |
| P1.5 History rebuild | Fixed. Rows are diffed by operation id; survivors update in place, new rows are created, removed rows are deleted. |
| P1.6 explicit theme repaint | Unchanged by design. Ordinary navigation does not apply a global stylesheet. |
| P2.1 broad Personal invalidation | Narrowed: preference affects LIKED/BLACKLISTED; watchlist affects WATCHLIST/READY; watch events affect READY. Unknown changes retain broad fallback. |
| P2.2 payload-free History changes | Broad fallback retained because current Undo/Recovery results expose MediaFileId, not a proven MovieId. No unsafe identity inference was added. |
| P2.3 Media Files rebuild | Fixed. Panels are retained by MediaFileId and rebuilt internally only for the changed row; unrelated panels preserve identity and exact button ids. |
| P2.4 layered show_items | Simplified. `set_items(..., visible_items=...)` performs the source sync and one presentation update; views render state without a second grid call. |
| P2.5 repeated Check Library ids | Fixed conservatively within one run by coalescing identical `(MediaFileId, status)` changes and metadata MovieIds. A different status remains deliverable. |
| P2.6 credential refresh | Unchanged; no evidence justified removal. |

## Clear Library safety and semantics

The clear transaction counts all active Movies, rejects nonterminal filesystem operations, then deletes active catalog/personal rows in foreign-key-safe order. The immutable `file_operations` journal remains; its deleted MediaFile foreign key becomes NULL through the existing schema behavior. Physical test media is byte-identical after success and after blocked/failing clears. Poster cleanup remains delegated to the injected application poster-cache boundary after the database commit; a cache failure becomes the existing post-commit warning.

Restart/query verification proves all Personal sections empty, Watch Events absent, and a later reimport starts with no inherited preference/watchlist/watch history.

## Presentation behavior after the pass

- Startup Library: still one local source load and no automatic reconciliation. Stable DTO updates no longer tear down cards.
- First Personal load: target-specific loading, then one accepted target result.
- Cached Personal switch: same-section cache appears immediately with no cross-section placeholder.
- Uncached Personal switch: no old-tab cards; localized loading/error or same-section stale cache only.
- Poster delivery: asynchronous results remain per-card, but visible `setPixmap()` swaps are staged and presented in a bounded grid batch without recreating cards or the grid.
- Metadata enrichment: same MovieCard object updates in place; only a changed poster identity starts a new request.
- Media rows: stable MediaFileId panels survive one-file updates.
- History rows: stable operation-id rows survive inserts and state/path updates.

Remaining visual risk: Qt compositor timing can still vary by Windows/DPI/driver. Poster completions separated beyond the 100 ms bound can form more than one batch, but the measured 1/3/5 wave no longer scales one visible cycle per card. Computer Use screenshot capture was unavailable on this host because its helper hit the repository ACL error; native Qt Window UpdateRequest measurement supplied the runtime gate.

## Verification

- Compileall: passed.
- New acceptance tests from the original pass: `13 passed`.
- Per-card poster focused tests: `5 passed` (1/3/5 delayed delivery, real temporary cache, and Personal Library).
- Focused Personal file: `29 passed, 1 existing baseline failure`.
- Clear Library gate: `7 passed`.
- MovieCard file: `10 passed, 1 existing baseline failure`; the three new stable-identity/import-lifecycle cases passed.
- Startup/refresh/check gate: `65 passed` (`9` static flicker-lifecycle plus `56` Pass 1/Check Library tests).
- Full suite after the poster follow-up: `1,235 collected; 1,220 passed; 10 failed; 5 skipped` in 224.59 seconds.
- Accepted starting suite: `1,217 collected; 1,202 passed; 10 failed; 5 skipped`.
- New failures: `0`.
- `git diff --check`: passed after removing one trailing blank line.
- V7 UI prototype ported: NO.
- Poster Phase 3 started: NO.
- Packaging/release work performed: NO.
