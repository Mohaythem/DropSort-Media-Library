# Release hardening — 2026-10-02

This is a historical source-work report. Final artifact, source-tag, full-suite, and installed-smoke facts are recorded in [packaging/RELEASE_MANIFEST.md](packaging/RELEASE_MANIFEST.md); they supersede the earlier artifact/status statements below.

## Part A — manifest completion

- Read-only Git inspection confirmed branch `codex` and HEAD `3ef3187`.
- The canonical `packaging/RELEASE_MANIFEST.md` already contained the current artifact facts, schema 14, focused-test history, and installed-app upgrade/data-preservation verification.
- Restored the open risk: **Home / Recently Watched stale availability hints**. No verified resolution was available.
- Marked the earlier partial Codex adversarial review as historical. It did not review the current working tree or installer. Its original findings remain in the manifest.
- Copied the canonical manifest to `artifacts/release/0.9.0-preview/RELEASE_MANIFEST.md`. Both copies are byte-identical.
- Verified installer size: **34,725,671 bytes**.
- Verified installer SHA-256: `A28C3DBA28050A10B4615F005A76D3433275BAF585A3419A729320C3CCB90D72`.
- Installer file date and manifest build date: **2026-10-02**.
- No build or tests ran for the manifest step.

## Part B — UNC/network hardening

### 1. Behavior before this change

The current code already used `Present`, `Missing`, `StorageUnavailable`, and `Error` inspection results. Reconciliation skipped database writes when a storage root was unavailable. It preserved catalog entries. Check Library and Add Media already ran their main filesystem work on background threads.

UNC probes, discovery, and temporary-file diagnostics could still wait for Windows network timeouts. Movie/episode Play and Open Folder used synchronous `File.Exists` on the UI thread. Check Library used synchronous folder and relink probes. Availability requests could outlive navigation because the shell retains pages and changes visibility without unloading them.

### 2. Bugs and causes

- Network reads had no application wait limit or shared in-flight guard.
- `File.Exists` hid the distinction between missing media and unavailable storage.
- Native file-identity failures could throw `Win32Exception` and abort reconciliation.
- Cancellation was not rechecked after a slow inspection and before a catalog write.
- Cached pages required `IDeactivatableView` cancellation, in addition to unload cancellation.
- Discovery needed to preserve healthy siblings when one child disappeared during enumeration.

### 3. State model

| Inspection result | Meaning | Catalog effect |
| --- | --- | --- |
| Present | File exists and verified facts can be read | Refresh facts and restore persisted PRESENT status |
| Missing | Storage root is reachable; registered file is absent | Mark MISSING; retain registration and relationships |
| StorageUnavailable | Share cannot be queried reliably, or read wait expires | Preserve every catalog value and relationship |
| Error | Storage is reachable but file inspection fails | Preserve catalog state; report an inspection error |

StorageUnavailable remains a transient inspection/UI result. No database migration or new persisted status was added.

### 4. Implementation

- Added `NetworkReadGuard`: two-second default wait per network read, one in-flight native read per UNC share, no queued retry loop. Canonical and extended UNC spellings share the same guard. Local and extended local paths retain their existing behavior.
- Guarded availability inspection, discovery root checks, directory snapshots, file statistics, and network temporary-file diagnostics.
- Added explicit StorageUnavailable discovery issues. Loose-file scans process local directories first and retain root-level error rows.
- Added `MediaFileActions` for background availability checks and shell activation. Failed actions return Missing, StorageUnavailable, or Error without catalog mutations.
- Deferred UNC operation-journal recovery during synchronous shell initialization. Pending journals remain intact. Local startup recovery retains its existing behavior.

### 5. UI-thread and cancellation handling

Movie, TV, and Check Library actions now await background services. Separate request lifetimes suppress stale availability, action, relink, and error results. The pages cancel these requests when the shell hides them and when records change. Check Library guards dispatched progress and completion by generation. Relink previews are discarded when abandoned. Reconciliation and relink confirmation check cancellation after inspection and before database writes.

Timed-out read workers cannot publish catalog writes. Abandoned enumeration stops at its next managed cancellation check. Windows can retain the native call until its own timeout expires.

### 6. Catalog safety

Unavailable storage never causes a delete, detach, path rewrite, metadata clear, watch-state clear, or list-membership change. Files retain their last persisted PRESENT or MISSING state. A failure on one network root does not prevent healthy local files from being checked first.

### 7. Recovery

No periodic retries or network watcher were added. A later ordinary check, scan, page load, or file action probes the share again. After the pending native read completes and the share is reachable, the next check restores availability on the same registered IDs and relationships. Re-registration is not required.

### 8. Focused tests

Final verification: **42 passed, 0 failed, 0 skipped**. These tests compile the latest backend code. Network failures use deterministic injected probes; SQLite tests use disposable databases. No real user media or library database was used.

```powershell
dotnet test 'DropSort_WinUI3\tests\DropSort.Tests\DropSort.Tests.csproj' -c Debug --filter 'FullyQualifiedName~NetworkHardeningTests|FullyQualifiedName~NetworkCatalogSafetyTests|FullyQualifiedName~DriveAwareAvailabilityTests|FullyQualifiedName~DiscoveryNetworkHardeningTests|FullyQualifiedName~DiscoveryParsingTests|FullyQualifiedName~PostHandoffRequestTests' --no-restore
```

Coverage includes all three availability states, timeout suppression, extended UNC share identity, sequential recovery, healthy local-root progress, full catalog/personal/list/TV relationship snapshots, complete offline media-row preservation, cancellation before reconciliation/relink writes, safe action failure, cancellation before shell activation, and healthy sibling discovery. The final test compile reports five existing xUnit1031 warnings in `WriteCoordinationTests.cs`; that unrelated file was not changed.

Independent Astra review found no unresolved material findings after the repairs. Final `git diff --check` passed.

### 9. Debug build

One Debug x64 / win-x64 build was requested through the existing VS MSBuild toolchain:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' 'src\DropSort.UI\DropSort.UI.csproj' -t:Build -p:Configuration=Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -verbosity:minimal -nologo
```

Result: **passed, exit 0**, with one CS8603 nullable warning. This was the only WinUI build. It preceded the final backend-only root-exception/guard-slot repairs and nullable annotation. The final focused tests compiled and verified those later backend changes. No second WinUI build was run. Log: `.audit-artifacts/unc-network-hardening-2026-10-02.Debug-x64.log`.

### 10. Manual network-share test sequence — not executed in this pass

Use an isolated QA data root and disposable media. Use a share on another machine so disconnection can be tested without changing the user's real library.

1. Configure a disposable UNC media folder and a healthy local folder. Import a movie and a TV episode. Add watch events and custom-list memberships. Record media IDs, paths, metadata, and relationships.
2. With the share online, run Check Library. Expect Present. Test movie/episode Play and Open Folder.
3. Remove one disposable media file while keeping the share reachable. Run Check Library. Expect Missing for that file, with catalog registration and relationships retained.
4. Restore that file. Run Check Library. Expect Present on the same registration.
5. Make the remote share unreachable. Run Check Library. Expect StorageUnavailable, no new Missing rows caused by the outage, and unchanged catalog/personal/list/episode relationships. Confirm the local root is checked and the shell stays responsive.
6. With the share unavailable, test folder scans, loose-file scans, Play, and Open Folder. Expect graceful unavailable feedback. Cancel checks/scans and navigate away during requests. Confirm no stale dialog or page update appears.
7. Restore share connectivity. After any native read returns, run Check Library and reopen details. Expect recovery without re-import. Verify the original IDs, metadata, watch history, list membership, and file relationships.
8. Repeat with access denied and a child disappearing during a scan. Expect conservative unavailable/error reporting and healthy sibling preservation. Include paths with spaces and non-ASCII names, plus extended UNC paths where applicable.

### 11. Remaining risks and boundaries

- Real SMB disconnect/reconnect, permission-denied behavior, and interactive UI responsiveness still require the manual sequence above.
- Slow but reachable directories can exceed the two-second snapshot budget and be reported unavailable conservatively. Temporary-file diagnostics have a bounded total network enumeration wait.
- A native call can remain blocked after the caller returns. At most one guarded read remains in flight per share. Recovery waits for that call to finish.
- UNC operation journals remain pending during startup; automatic background network mutation recovery is outside this change.
- Mapped network drive letters retain existing drive behavior. This pass hardens explicit UNC paths.
- Shell activation runs off the UI thread. Once Windows accepts activation, cancellation cannot recall it.
- Home / Recently Watched stale availability hints remain explicitly deferred.
- The unsigned installer is unchanged and does **not** include these subsequent source changes. No Release build or installer rebuild ran.
- No USB/removable-drive changes, network discovery, or credential management were added.

## Repository boundary

All changes remain uncommitted on `codex`, HEAD `3ef3187`. Unrelated working-tree changes were preserved; 170 source/test files outside this change retained their initial hashes, with no removed source/test files. `main` remained at `02ba6ba4d42a316357530c661993277e0723f9ee`. No commit or push was performed.
