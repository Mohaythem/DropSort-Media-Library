# P2 fixes and verification

## Local update follow-up — 2026-10-25

User authorized direct Codex verification, packaging and installation, without Gemini or Antigravity. Fresh applicable suite: 1110 passed, 0 failed, 0 skipped; WindowsTmdbCredentialStoreTests excluded. Rechecked original P1/P2 fixes and corrected the remaining P3 title mouse-button handling in both details pages. Release x64 self-contained Rebuild after that change passed. Version is now 0.11.1 / 0.11.1.0, schema remains 19. Inno compilation and local silent installation both exited 0. All 535 installed payload files match staged hashes, including 16 XBF resources; actual installed EXE is 0.11.1.0. No app was running at installation and none was stopped or launched. No live database/media/credential access, commit, push, tag or publication. Full evidence and outstanding manual/live-account limits: `artifacts/release/0.11.1-local-20261025/VERIFICATION.md`. Earlier sections below describe the previous P2 task, not this later installation.

Date: 2026-10-23 (Africa/Cairo). Branch: codex, current working tree.

## Implemented scope

1. Personal-list availability: `PersonalLibraryRepository.ListMovies` counts distinct missing media-file IDs. Repeated watch events no longer inflate missing counts. Watchlist, liked, and blacklisted fixtures cover one present file, one missing file, and three views. Existing zero-file behavior remains covered.
2. Discovery release years: numeric titles at the beginning are retained; a separate parenthesized/bracketed release year is preferred, with the prior unbracketed convention retained after a real title prefix. TV release-year search stops before the episode marker and the extracted year is preserved. Examples include `1917 (2019)`, `2001 A Space Odyssey (1968)`, bare `1984`, and `Doctor Who (2005) S01E01`. Ambiguous naming is not a claim of perfect identification.
3. Trakt retry: a server minimum above the configured cap still fails. Jitter is bounded by the remaining allowance, so an exact 60-second server minimum retries at 60 seconds. Delta and HTTP-date fixtures cover 60 and 61 seconds without sleeping or real HTTP.
4. My Lists: custom-list movie/show projections retain canonical and custom titles. Search matches the displayed title, canonical title, or year. A UI-independent search helper is linked into the test project. No live UI verification was performed.
5. TV library reads: optional `ITvShowSummaryReader` provides a single aggregate SQLite query, exposed by the production unit-of-work show adapter. The service retains its existing fallback for other repositories. Files aggregate per episode first, so a present copy takes precedence and multiple missing copies count as one missing episode. Empty seasons/shows, ordering, custom/canonical titles and timestamps are preserved. LibraryPage reads summaries on a worker and publishes on its UI context; revision and generation checks reject stale results, errors remain retryable, and repeated refreshes share the active load.
6. Startup recovery: App claims single-instance library ownership on the UI thread, then awaits background service initialization before constructing MainWindow/pages. Existing migration, recovery, hashing, failure handling and service publication order are unchanged. No fire-and-forget recovery overlaps user actions or shutdown. The main window still waits for initialization/recovery to finish; total recovery time was not reduced or benchmarked.

No schema migration, new package dependency, identity-matching replacement, or media-file mutation was introduced. Existing unrelated scanner/title/P1 work and unrelated deletions were preserved. No P3 mouse-button change was included.

## Delegation and review

Gemini 3.8 Flash was called through the actual `gemini_flash` MCP tool for P2 proposals, startup architecture analysis, and final independent source review. The root implemented and inspected actual changes and ran tests/builds. Gemini has no repository execution tools and did not execute these checks.

The proposal was evaluated rather than copied wholesale: its parser sample had inconsistencies, and its suggestion to defer TV aggregation was superseded by a bounded optional read interface owned by the root. Startup review supported the awaited initialization approach. The final response returned “No Material Findings” but its detailed explanation was truncated. Treat that as a limited independent review, not complete verification or an absolute safety guarantee. Raw responses are under artifacts/p2-verification. One startup review attempt timed out in automatic permission review; the permitted retry completed.

## Executed verification

All commands run from the repository root against disposable fixtures, with no application launch or real user database/media/credential access.

- Before production fixes: 19 targeted cases ran, 15 failed and 4 passed. `p2-before.trx` records the demonstrated defects.
- Focused parser/repository/Trakt/search/title/batch checks: 110 passed, 0 failed (`p2-focused.trx`). An intermediate run had two failures due to missing pagination headers in the new fake success response; the fixture was corrected and rerun. No API policy was relaxed.
- TV aggregate parity/scale: 3 passed, 0 failed (`p2-tv-summary.trx`), including mixed copies, missing files, episodes without files, empty seasons/shows, custom titles, and 1/250-show fixtures. Supplying null hierarchy readers proves the optimized service does not read per-show hierarchy.
- Broad suite after startup change: 1110 passed, 0 failed, 0 skipped (`p2-regression-final.trx`). Command: `dotnet test DropSort_WinUI3/tests/DropSort.Tests/DropSort.Tests.csproj --no-restore --filter "FullyQualifiedName!~WindowsTmdbCredentialStoreTests" --logger "trx;LogFileName=p2-regression-final.trx" --results-directory DropSort_WinUI3/artifacts/p2-verification`.
- Native Windows credential-store tests were explicitly excluded, not counted as passed.
- After adding test-output instrumentation only, all 17 RepositoryParityTests passed (`p2-repository-final.trx`). This changed no production source.
- Release x64 build after the App startup change: exit 0. Command: `& "C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" DropSort_WinUI3/src/DropSort.UI/DropSort.UI.csproj /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal /m:1 /fl "/flp:logfile=DropSort_WinUI3/artifacts/p2-verification/release-build-final.log;verbosity=minimal"`.
- `git diff --check`: exit 0.
- Test compilation reported NU1900 vulnerability-feed unavailability and existing xUnit analyzer warnings in other tests. No test/build errors remain.

Focused counts overlap the broad suite and must not be added together.

## Bounded performance observation

One disposable SQLite run with 250 empty shows recorded 3.66 ms for bulk summaries versus 41.12 ms for the existing per-show hierarchy path. The 1-show observation was 0.22 ms versus 13.45 ms. These are single local fixture observations with warm-up/order effects, not representative user-library or UI benchmarks and not speed guarantees. Timings are captured in p2-repository-final.trx.

## Limits and handoff

- No live UI inspection, launch, screenshot, automation, or physical acceptance was performed. Startup ordering has a source contract test; async loading/focus/visual behavior still needs manual acceptance.
- No real large-media recovery timing was measured. Recovery safety rules and hash checks remain intact.
- No installer was built or installed during this P2 task. The installed copy remains the earlier P1 update. The successful Release build alone is not a packaged/installed-runtime verification.
- No commit, push, tag, public release, account change, or normal-profile credential operation occurred.
