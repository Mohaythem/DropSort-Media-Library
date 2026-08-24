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

## Remediation by finding

| Finding | Result |
|---|---|
| P0.1 cross-tab content on failure | Fixed. An uncached target clears the shared visible source and owns a localized loading/error state. Only same-section stale data may remain. |
| P0.2 stale result paints another tab | Fixed. Every request captures generation and section. A result may cache only for its generation and may paint only while it owns the current section. |
| P0.3 Clear reuses stale snapshots | Fixed. Clear success explicitly discards Library cards/source, all Personal caches, details state, and search suggestions before one authoritative Library load. |
| P0.4 incomplete active clear | Fixed. One SQLite transaction deletes metadata cache, Watch Events, personal state, MediaFiles, and Movies; unresolved operations still block the clear. |
| P1.1 Personal two-stage presentation | Fixed. Fresh same-section cache is immediate; uncached targets use target-specific loading; failed targets cannot show another section. |
| P1.2 poster delivery | Deliberately not redesigned. Runtime evidence proves it is per-card and not the sole cause. |
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
- Poster delivery: individual card repaint remains; whole-grid/card recreation is not triggered.
- Metadata enrichment: same MovieCard object updates in place; only a changed poster identity starts a new request.
- Media rows: stable MediaFileId panels survive one-file updates.
- History rows: stable operation-id rows survive inserts and state/path updates.

Remaining visual risk: Qt can still produce platform-specific geometry/paint timing during the first native show, and asynchronous posters still replace placeholders per card. The trace does not justify claiming all perceptual flicker is eliminated.

## Verification

- Compileall: passed.
- New acceptance tests: `13 passed`.
- Focused Personal file: `29 passed, 1 existing baseline failure`.
- Clear Library gate: `7 passed`.
- MovieCard file: `10 passed, 1 existing baseline failure`; the three new stable-identity/import-lifecycle cases passed.
- Startup/refresh/check gate: `65 passed` (`9` static flicker-lifecycle plus `56` Pass 1/Check Library tests).
- Full suite: `1,230 collected; 1,215 passed; 10 failed; 5 skipped` in 331.82 seconds.
- Accepted starting suite: `1,217 collected; 1,202 passed; 10 failed; 5 skipped`.
- New failures: `0`.
- `git diff --check`: passed after removing one trailing blank line.
- V7 UI prototype ported: NO.
- Poster Phase 3 started: NO.
- Packaging/release work performed: NO.
