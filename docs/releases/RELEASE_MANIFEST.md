# DropSort 0.11.0 — Windows x64

- Version: 0.11.0. Assembly/file/installer numeric version: 0.11.0.0.
- Authorized publication: 2026-10-21. Branch: codex. Intended new tag: v0.11.0. Preserve main and existing tags/releases.
- Scope: read-only Trakt Phase 1, schema18 isolated account/resource cache, protected authentication, remote viewer, review fixes, Trakt empty-state hit testing, Home empty-shelf sizing.
- Keep unrelated modified/deleted/untracked user content outside the release commit. No synthetic profiles, database files, credentials, logs, proposal artifacts or personal files belong in the runtime payload.
- Build from a fresh Git export of the release source commit. Record the exact commit and per-file SHA-256 inventory in BUILD_MANIFEST.json.
- Use Visual Studio MSBuild Release/x64/win-x64 with SelfContained=true and WindowsAppSDKSelfContained=true. Package Build output, including PRI/XBF, .NET, Windows App SDK and SQLite.
- Use existing Inno Setup AppId and per-user install location. Produce DropSortSetup.exe, DropSort-0.11.0-win-x64.zip, SHA256SUMS.txt, release notes, manifest and verification record.
- Network operations are read-only data import plus OAuth requests. No remote scrobbling, data writes or two-way sync. Local Library/history/lists remain separate.
- Prior current-worktree evidence:973 broader tests passed excluding native TMDB credential tests, then25 Home tests passed after sizing adjustment. Final release-source test/build/package evidence is recorded with the artifacts; do not imply prior counts are a new release-source run.
- No agent UI inspection/automation. Live Trakt login/native credential persistence, maximum-resource performance and clean-machine installer lifecycle remain unverified. Existing vulnerability-feed/analyzer warnings must be reported if present.
- No digital signing certificate is configured. Package is unsigned. Runtime startup migrates the local database to schema18 through existing backup/validation gates; no agent test accesses or migrates the real profile.
