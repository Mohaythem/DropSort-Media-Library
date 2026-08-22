# ADR-0013: Read-Only Initial Media Discovery

**Status:** Accepted for Phase 3C

**Date:** 2026-08-11

## Context

DropSort needs to inspect a caller-selected folder and produce parser-ready movie discovery results
without metadata lookup, matching, catalog writes, or filesystem mutation. Recursive traversal must
not escape through Windows junctions/reparse points or loop indefinitely.

## Decision

Define immutable discovery models and a `MediaDiscoveryScanner` protocol under `media/discovery`.
The `DiscoverMedia` application use case depends only on that protocol. The concrete
`ReadOnlyMediaScanner` is the sole filesystem-read boundary and reuses Phase 2A's supported-extension
detector and filename parser.

Before scanning, every existing component of the selected absolute root is inspected with `lstat`.
Symbolic links and Windows reparse points are rejected in the root chain. During traversal, directory
and file entries are inspected without following links. Linked/reparse entries are reported and not
followed. Directory `(st_dev, st_ino)` identities prevent recursion loops.

Traversal is iterative rather than recursive in Python, and output is sorted using a case-folded path
key plus the original path as a deterministic tie-breaker. Unsupported regular files are ignored.
Supported videos become movie candidates, skipped TV episodes, or unknown-media results according to
the existing parser.

Root failures raise controlled `DiscoveryRootError`. Per-directory, per-file, and parser failures
produce controlled error items so one damaged entry does not terminate the remaining scan.

## Consequences

- The scanner reads directory entries and file metadata only; it never opens or mutates media.
- Scan results contain the path, observed size, parsed filename data, and a controlled classification.
- No metadata provider, matcher, catalog repository, file-operation service, or UI is involved.
- Results are point-in-time observations and may become stale before a later explicit import.
- Linked folders are deliberately not scanned, even when they point inside the selected root.
