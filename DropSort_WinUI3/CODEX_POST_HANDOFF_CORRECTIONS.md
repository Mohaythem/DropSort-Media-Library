# Post-handoff corrective pass

Status: PASS. Scope: verified findings H1, M1–M6, L1–L2 in `CODEX_POST_HANDOFF_REVIEW.md`. Baseline remains `65903bb`; no claim of Antigravity authorship.

## Implementation decisions

- Restore the existing session-only credential contract. Remove the accidentally persisted legacy token key without loading it, retain tokens only in memory, and preserve unrelated settings. No explicit persistent-credential feature exists, so none is added. Deleting a key cannot erase historical backups or guarantee forensic erasure of old SQLite pages.
- Keep metadata provider signatures; add a sanitized application-level failure category/exception and optional connection-test failure category. Presentation maps categories to English/Arabic messages; no raw provider payload, token, URL, or inner exception reaches the user.
- Preserve local movie/show/season/episode IDs, personal state and media links. Metadata rematches invalidate obsolete provider fields atomically, with cancellation checks at the write/commit boundary. No schema or repository contract redesign.
- Keep request-lifetime coordination in presentation. Capture targets before asynchronous work, propagate cancellation and suppress stale UI completions.
- Report crash temporary files only from an explicit Check Library pass. Never delete, rename, or automatically recover a reported file.
- Use a DEBUG-only explicit data-root override for isolated runtime verification; normal application storage remains unchanged. This is a test seam, not a product configuration feature.

## Verification evidence so far

- Credential regression test before fix: `dotnet test tests/DropSort.Tests/DropSort.Tests.csproj --no-restore --filter FullyQualifiedName~SessionCredentialRegressionTests --nologo --verbosity quiet`: 2 failed, 1 passed.
- Same command after session-only fix: 3 passed, 0 failed. The additional SQLite-backed restart check passes in the final suite (4 credential tests total).

## Final verification

- Clean restore: `dotnet restore DropSort.slnx --nologo --verbosity minimal` — passed; all projects up to date. The earlier sandbox `NU1301` was a transport restriction, not a project configuration change.
- Full solution supported build: `MSBuild DropSort.slnx /t:Rebuild /p:Configuration=Debug /p:Platform='Any CPU' /m /v:minimal` — passed; Domain, Application, Infrastructure, FileSystem, Tests and UI; zero warnings/errors.
- Supported WinUI build: `MSBuild src\DropSort.UI\DropSort.UI.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /m /v:minimal` — passed; zero warnings/errors.
- Full .NET suite: `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj --no-restore --nologo --verbosity minimal` — **215 passed, 0 failed, 0 skipped**.
- Focused regressions: provider/cache **26 passed**; TV matching **5 passed**; credential persistence **4 passed**; presentation safety/localization **3 passed**; request lifetime **5 passed**; migration coverage **2 + 5 passed**.
- Isolated x64 launch: final executable started with `DROPSORT_TEST_DATA_ROOT` set to a fresh `%TEMP%` directory and remained running with the expected `DropSort Media Library` title. Home, Library, Settings, Check Library and read-only Check Library execution were exercised. Light theme and Arabic RTL were switched and verified through the accessibility tree. The isolated process was closed/terminated by exact PID; no real library path was used.
- `git diff --check -- DropSort_WinUI3` — passed; only standard LF-to-CRLF notices. No source changes occurred outside the active app except the pre-existing unrelated deletions/untracked paths listed in the review.

## Final status

All scoped corrective areas are implemented and covered by passing focused/full tests. Remaining risks are limited to live TMDB service behavior (no real credential or network call was made), the product not providing protected persistent credentials (session-only is now explicit), and visual validation being smoke-level rather than pixel-comparison. Crash-temp reporting is deliberately read-only and only runs from Check Library. The corrective pass is included in the final commit; the exact commit hash and remote verification are reported in the final handoff.
