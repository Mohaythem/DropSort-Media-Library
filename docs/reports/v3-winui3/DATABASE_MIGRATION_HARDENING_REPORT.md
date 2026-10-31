# Database and migration hardening — 2026-10-03

This is a historical source-work report. Final artifact, source-tag, full-suite, and installed-smoke facts are recorded in [packaging/RELEASE_MANIFEST.md](packaging/RELEASE_MANIFEST.md); they supersede the earlier release/status statements below.

Database-only changes are complete. Schema remains **14**. The final focused test run and independent review are recorded below. All database fixtures are disposable. The real user database and physical media were not opened or changed.

## 1. Current schema and architecture

- **Verified safe:** `DatabaseMigrator.LatestVersion` is 14. SQLite `PRAGMA user_version` is the version authority.
- One database contains catalog, file-operation journal, settings, personal state, history, and lists.
- Repositories enable foreign keys and use WAL with FULL synchronous mode. Catalog work uses an immediate transaction. The existing write gate serializes catalog, personal, journal, and settings writes within the process.
- Startup acquires `LibraryInstanceGuard` before migration and recovery. The guard remains held during initialization retries and is released after connections are closed at shutdown. Services are published only after successful initialization.
- `DatabaseBootstrap` is an engine smoke check and pool-release helper; it is not the migration runner.

## 2. Migration chain audited

| Version | Change |
|---|---|
| 1 | Initial movies, media files, metadata cache, operation journal, watched folders, settings |
| 2 | Rebuild portable filesystem identity fields as text |
| 3 | Movie genres and media-file movie index |
| 4 | Preferences, watchlist, initial movie watch events |
| 5 | Offline movie registration and nullable provider identity pair |
| 6 | Shows, seasons, episodes, episode-file links |
| 7 | Movie and TV artwork, ratings, taglines, season/episode external IDs |
| 8 | Durable movie/episode watch history and metadata snapshots |
| 9 | Normalize both historical v8 watch-history artwork shapes |
| 10 | Stable TV import aliases and backfill |
| 11 | Original fixed-slot movie custom-list membership |
| 12 | List definitions and migration of names/membership |
| 13 | Mixed movie/show lists, exclusive-kind CHECK, partial unique indexes |
| 14 | Persisted `movies.review_match`, default 0 |

Assumptions were reported before implementation:

- Each migration commits separately. Successful earlier steps remain committed if a later step fails.
- The host owns the cross-process single-instance guard. A direct migration caller must respect that contract.
- Automatic backup retention assumes one library database per data folder.
- A media file may have a null movie link. TV registration and intentional catalog deletion require this.
- Durable watch history may outlive the movie or episode it records.
- Both historical v8 artwork shapes must be supported. Arbitrary prototypes are not supported.

These semantics were retained. No schema version, dependency, UI, startup wiring, or restore workflow was added.

## 3. Historical fixtures

- **Covered by automated fixtures only:** populated v7, v8 with full artwork, legacy v8 without backdrop/poster columns, v9, v11, v12, v13, and current v14.
- Historical fixtures use current forward scripts to build the applicable earlier shape. Legacy v8 is reconstructed explicitly. They are not archived databases from actual previous installations.
- Existing v1 portable-identity coverage was also run. This does not add prototype compatibility.

## 4. Exact data preservation

`DatabaseHistoricalRowsTests` compares ordered values for every pre-existing column in movies, media files, cache, journal, watched folders, settings, personal state, shows, seasons, episodes, and episode-file links. It also compares complete history, aliases, lists, and mixed memberships where those structures already existed.

Fixtures include:

- Matched and offline movies; stable IDs; provider/external identity; title/original title; year; overview; runtime; rating; genres; artwork; tagline; metadata status; timestamps.
- Two files for one movie and two files for one episode; paths, keys, sizes, format details, availability, and exact link IDs/dates.
- Show → season → episode IDs, numbers, metadata, provider IDs, and enriched fields.
- Watchlist/preferences; movie and episode history; title/year/show/season/episode snapshots; artwork references and event timestamps.
- Metadata-cache payloads; folder configuration; settings; terminal operation history and unsigned identity text.
- List names, IDs, movie/show membership IDs, kind, references, and added dates. v11 → v12 verifies the defined fixed-slot mapping rather than claiming old membership IDs survive that migration.
- Review Match defaults to 0 for older schemas; a persisted value of 1 survives current-schema reopen.

Current reopen compares the complete SQL schema and catalog rows, and creates no additional migration backup.

## 5. Failure and interruption behavior

- **Fixed:** final validation now executes inside the final migration transaction. A missing required index leaves v13 at v13 and rolls back the new Review Match column.
- A late v13 rebuild failure, caused by an already-created index name, rolls back the copied/dropped membership table, rows, and version. Removing the conflict permits a successful retry.
- A pre-existing Review Match column with a v13 stamp causes a fail-closed error. Repeated attempts leave that version and preserve the recovery backup.
- Unknown non-empty version 0, negative versions, and future versions are rejected before persistent journal-mode changes.
- Foreign-key violations are rejected before migration. Broken v7 history is preserved for diagnosis instead of silently discarded by the v8 join.
- Transaction exceptions are tested. Abrupt process termination and power loss were not injected.
- A failure never reports the whole upgrade as successful. Earlier independently committed migration steps may remain; the pre-upgrade snapshot is the recovery point.

## 6. Transactions, foreign keys, and constraints

- **Verified safe in fixtures:** immediate migration transactions; rollback of DDL and row changes; version stamps in the same transaction; foreign-key checks before migration and before commit.
- Rebuild steps temporarily disable FK enforcement outside the transaction, then restore it in `finally`. FK consistency is checked before commit.
- Migrated fixtures pass `PRAGMA integrity_check` and `PRAGMA foreign_key_check`.
- Tests reject duplicate movie provider identities, incomplete identity pairs, invalid season numbers, references to absent movies, duplicate episode-file links, duplicate list membership, and invalid mixed-kind membership.
- Movie deletion intentionally detaches media files, cascades personal/list rows, and detaches durable watch events. Show deletion cascades its hierarchy, aliases, and membership while retaining history and media-file records. Media-file deletion removes episode links and detaches the journal reference.
- FK enforcement prevents links to absent rows. Nullable media ownership remains intentional; the database does not require every registered file to belong to a movie or episode.

## 7. Backup safety

- **Fixed:** migration snapshots defer retention pruning until the requested upgrade succeeds. Seven failed attempts cannot prune the earlier valid v13 backup. The fixture checks exact backup bytes and successfully upgrades a disposable copy.
- **Verified safe in selected tests:** SQLite online backup includes committed WAL state; physical integrity and source version are verified; backup failure prevents migration; names do not intentionally overwrite an existing backup; normal retention remains five files.
- Backups are beside the database in `backups`, under the per-user data root, outside installed binaries. The QA data-root override remains unchanged.
- Backups contain SQLite data only. They do not move, rename, open, or delete media files.
- Verification proves SQLite integrity and version, not complete application semantics. A malformed but physically intact schema can still produce a snapshot.

## 8. Reproduced bugs and fixes

An isolated harness compiled the exact saved pre-change migrator and backup code. It reproduced all eight deterministic cases:

| Before | Root cause | Minimal fix |
|---|---|---|
| v8 backdrop/poster values became NULL | v9 copied only shared columns | Choose the projection from actual source columns |
| Failed final validation left version 14 | Validation occurred after commit | Validate inside the final transaction |
| Current stamp without Review Match was accepted | Missing column guard | Require `movies.review_match` |
| Unknown non-empty v0 was adopted | Version 0 assumed empty | Reject user schema objects |
| Future schema changed DELETE to WAL before rejection | Mutable pragmas preceded version checks | Reject first |
| Negative schema changed journal mode before failing | No negative-version guard | Reject first |
| Broken v7 watch history disappeared | v8 inner join omitted orphan rows | Check source FK integrity first |
| Failed retries pruned a clean backup | Retention ran after each snapshot | Prune only after successful migration |

Concurrent direct migrators also failed in baseline testing. They now acquire the existing write gate before reading the version or taking a backup. Concurrent migration fixtures pass without a second schema application. The host's cross-process guard remains the required process boundary.

Changed production files: `DatabaseMigrator.cs` and `DatabaseSafetyBackup.cs`. Added test files: `DatabaseMigrationHardeningTests.cs` and `DatabaseHistoricalRowsTests.cs`. Existing corrective and UNC changes were preserved.

## 9. Focused verification

Final command, from `TBDMovies_WinUI3`:

```powershell
dotnet test tests/TBD Movies.Tests/TBD Movies.Tests.csproj --no-restore --filter "FullyQualifiedName~DatabaseMigrationHardeningTests|FullyQualifiedName~DatabaseHistoricalRowsTests|FullyQualifiedName~DatabaseSafetyBackupTests|FullyQualifiedName~LibraryInstanceGuardTests|FullyQualifiedName~MigrationUpgradeTests|FullyQualifiedName~Migration7AndRepositoryEnrichmentTests|FullyQualifiedName~Migration8WatchHistoryTests|FullyQualifiedName~Migration9WatchEventsDriftTests|FullyQualifiedName~CustomListMigrationTests|FullyQualifiedName~CustomListMembershipTests|FullyQualifiedName~PersistenceTests|FullyQualifiedName~RecentlyWatchedMediaTests|FullyQualifiedName~RepeatedWatchHistoryTests|FullyQualifiedName~WatchHistoryConcurrencyTests|FullyQualifiedName~WriteCoordinationTests"
```

- Final result: **101 passed, 0 failed, 0 skipped**.
- Saved pre-change reproduction harness: **8/8 reproduced**, exit 0. It is isolated in gitignored `TestResults/db-baseline`; no baseline source is included in the test project.
- `git diff --check`: passed.
- Independent Astra review of the final production diff, both test classes, startup/gate compatibility, and report: completed; no remaining material findings.
- Final test evidence: `tests/TBD Movies.Tests/TestResults/database-hardening-final.trx` (generated with `--logger 'trx;LogFileName=database-hardening-final.trx'`).
- No full suite or WinUI build. Test compilation built backend/test dependencies in Debug. No Release or installer build.

## 10. Real-world verification still needed

- **Covered by automated fixtures only:** old catalog upgrade, malformed-schema refusal, late transaction failure/retry, backup retention/recovery copy, concurrent migrator calls, guard release, WAL snapshot, and reopen.
- Later QA should launch the updated app against an isolated `TBD Movies_TEST_DATA_ROOT`, populated with a disposable historical fixture. Check startup, exact catalog/history/list values, close/relaunch, and safe failure/retry.
- Run two actual app processes against that isolated root. Confirm the second refuses ownership and can start after the first exits.
- Inject process termination at migration boundaries on disposable databases; verify version, FK/integrity checks, WAL reopen, and backup retention. Sudden power loss and large-library timing remain unverified.
- The existing installer was not rebuilt and therefore does not contain these source changes.

## 11. Remaining database release risks

- **Still unresolved:** validation checks required object names and selected columns. It does not authenticate every column, index definition, CHECK, or FK declaration in an arbitrarily altered database.
- **Still unresolved:** failed attempts intentionally retain snapshots beyond the normal five-file limit. Persistent failure can consume disk space until successful migration or explicit maintenance.
- **Still unresolved:** migration callers outside the active host must honor the single-instance contract. The write gate serializes one process only.
- **Still unresolved:** backup retention assumes one library database per folder; backup verification is physical, not a full application-schema certification.
- **Still unresolved:** abrupt termination, power failure, and large real-library upgrade timing require isolated QA. Historical fixture coverage cannot prove every deployed schema variant.
- The existing backend restore primitive is not wired into a user/startup workflow and is not fully crash-journaled. The future Restore release gate remains open; no Restore feature was implemented.
- Review Match remains an integer with default 0; its existing SQL schema has no 0/1 CHECK. No new persistence semantics were introduced.

## Scope confirmation

- Branch `codex`; HEAD `3ef3187ec6f2f5e0812579fb5948497544d157d5` unchanged.
- `main` remains `02ba6ba4d42a316357530c661993277e0723f9ee`.
- No commit or push. No Release build or installer rebuild. No real user database or media access.
- Existing installer remains 34,725,671 bytes; SHA-256 `A28C3DBA28050A10B4615F005A76D3433275BAF585A3419A729320C3CCB90D72`.
