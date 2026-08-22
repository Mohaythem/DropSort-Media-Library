# Phase 1 Named-Persona Adversarial Review

Method follows the selected `named-persona-adversarial-review` skill. Findings stand on technical merit; persona names are only lenses grounded in the skill's sourced principles.

Sources:
- https://github.com/alirezarezvani/claude-skills/tree/main/engineering-team/skills/named-persona-adversarial-review
- https://github.com/alirezarezvani/claude-skills/blob/main/engineering-team/skills/named-persona-adversarial-review/references/persona_principles.md

## Round 1

### CRITICAL — crash boundary after source deletion

**Lens: Linus Torvalds — eliminate fragile special cases / preserve compatibility assumptions (confidence: high per skill reference).**

Initial implementation could remove the source before durable `FS_VERIFIED` evidence was stored. A process crash in that window left an `EXECUTING` journal with source missing and destination present but insufficient verification evidence. Media bytes were likely present, but automatic recovery could not prove the completed filesystem state, violating the restart-recovery invariant.

**Fix:** destination creation, durable flush, verification, and `FS_VERIFIED` journal persistence now happen before source removal. Cross-volume preparation stores SHA-256 evidence. Source and destination identity are rechecked immediately before unlink.

### HIGH — recovery trusted a path without reapplying link/reparse policy

**Lens: Ken Thompson — trust boundaries (confidence: high per skill reference).**

A destination could be externally replaced between operation and restart. Recovery must not trust the persisted path merely because it is in the journal.

**Fix:** recovery now receives `PathPolicy`, rejects symlink/reparse traversal, rechecks approved-root membership for an existing destination, and then validates stored size/mtime/device/inode/hash evidence before database commit.

### HIGH — database/filesystem divergence on commit failure

**Lens: Ken Thompson — explicit trust boundary between filesystem state and catalog state (confidence: high per skill reference).**

Filesystem success followed by SQLite failure must not cause the operation to be rerun or the catalog to lie.

**Fix:** authoritative media path and `COMMITTED` transition are one SQLite transaction after filesystem verification/removal. A failed DB transaction leaves the operation `FS_VERIFIED`, allowing a restart to retry only the database commit.

### WARNING — ambiguous both-exist state

**Lens: Steve Jobs — user experience must follow from the safe outcome (confidence: high per skill reference).**

Guessing which copy to remove would make recovery convenient but unsafe.

**Decision:** preserve both and mark `RECOVERY_REQUIRED`. No automatic cleanup exists in Phase 1.

## Round 2 after fixes

Re-review emphasis: data loss, Windows path behavior, crash recovery, database/filesystem divergence.

Result: **CLEAN for implemented Phase 1 invariants**, with environmental risks documented separately:

- Native Windows/NTFS/exFAT/SMB semantics have not been executed in this Linux sandbox.
- A narrow path-validation TOCTOU race remains possible if another process replaces directory components after validation and before the OS mutation. Address with Windows-native acceptance testing and, if needed, handle-based hardening before real-media beta.
- Cross-volume SHA-256 is intentionally expensive; safety is preferred over speed until measurement justifies another strategy.

No unresolved BLOCKER/CRITICAL finding remains.
