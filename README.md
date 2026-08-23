# DropSort Media Library

DropSort is a local-first Windows movie library built with Python, PySide6, SQLite, and a
safety-first filesystem core.

> **Audited baseline (2026-08-23):** This README describes the implemented Python application,
> not a claim that every current product contract or test is satisfied. The factual baseline is
> recorded in [`docs/audits/Deep-Audit.md`](docs/audits/Deep-Audit.md). The current source of product
> and architecture truth is [`docs/source/The Idea-v3.md`](docs/source/The%20Idea-v3.md). The audit found 1,172 passing tests,
> 11 failing tests, and 5 host-privilege skips, plus important gaps around startup reconciliation,
> offline registration, poster network activity, missing-state persistence, and Clear Library
> semantics. Feature work should start from those documented facts.

The current desktop app can display the local movie library, movie details, linked physical media
files, and locally cached poster artwork. Its **Add Movies** workflow performs a caller-selected read-only
folder scan, obtains cached TMDB candidates, explains matching decisions, and lets the user choose a
candidate. Catalog persistence occurs only after an explicit per-item **Add to DropSort Library**
click and never moves, renames, copies, or deletes the physical file.

Metadata proposal includes a bounded deterministic fallback for common release-name noise such as
leading site prefixes, bracketed tokens, and edition/cut suffixes. It makes at most four deduplicated
title/year queries and does not weaken matcher thresholds or turn a match into import authorization.

Large scans show truthful live directory/file/media counters, use determinate progress only when the
metadata proposal total is known, and remain responsive through bounded UI batching. **Cancel Scan**
is cooperative and read-only: incomplete results are discarded, no new provider work is scheduled,
and no catalog or file-operation record is created. A cancelled scan can be restarted immediately;
late callbacks from the previous session are ignored.

Movie Details provides explicit **Play Movie**, **Open Folder**, and **Organize File** actions for
each linked physical file. Play/Open use Windows file associations/Explorer and do not modify media
or catalog state. Organize requires the user to choose one destination, review the exact source,
destination, operation, volume, and validation result, then explicitly confirm the journaled
Move/Rename. The catalog path changes only after filesystem verification. There is no automatic or
bulk organization.

The centralized desktop theme uses the DropSort palette and bundled offline Inter Regular/Bold
fonts. Because Inter has no Arabic glyph coverage, bundled Noto Sans Arabic Regular/Bold provides a
deterministic offline Arabic companion; no web-font or system-font dependency is required.

The **Operation History** page reads the durable journal newest first and shows exact paths, states,
and details. Undo requires a separate dynamic safety check, exact read-only reverse preview, and one
explicit confirmation. It creates a new linked journal operation through the Phase 1 safe pipeline;
it never edits history or overwrites an occupied destination. Recovery exposes only deterministic
safe actions and preserves both files whenever state is ambiguous.

The first Library visit in a desktop session automatically checks only cataloged paths in the
background; the screen renders first and shows truthful inline present/missing/error progress. The
explicit **Check Library Files** dialog has distinct running, completed, failed, and cancelled
states; its terminal result, not the progress count, controls the Done/Cancel controls. Automatic
and manual requests coalesce onto one check and never run duplicate jobs. Missing records are never deleted and remain
visible in Movie Details with their last known location. **Locate File** opens a conservative
read-only preview; only **Confirm Relink** corrects the same catalog row after size, extension,
title/year/technical, reparse, path-ownership, and TOCTOU checks. Relink closes after success,
refreshes Library/Details immediately, never moves or modifies the selected file, and creates no
filesystem-operation journal.

Settings provides **Clear Library Data** behind an explicit confirmation. It transactionally forgets
cataloged movies, media-file links, and cached metadata while preserving operation/recovery history
and leaving every physical movie file unchanged. Poster-cache cleanup is confined to DropSort's
application-owned cache root. Clearing is blocked while a journal operation is nonterminal.

Settings also provides a persisted **Language** selector for English and Arabic. English is the
default LTR interface. Arabic switches the application chrome to RTL immediately while filesystem
paths, filenames, codec/resolution facts, IDs, and other technical values remain LTR. Switching back
restores English/LTR behavior.

Settings also provides **Appearance** with exactly four offline themes: **Main**, **Dark**, **Slate**,
and **Light**. Main preserves the original DropSort identity, Dark is a neutral charcoal direction,
Slate uses the supplied dark palette, and Light uses the supplied light palette with no dark Settings
gaps. The choice is persisted locally and can be changed at runtime. The primary sidebar is
user-resizable, switches directly between fully readable expanded and localized icon-only compact
mode, and persists its width.
Operation History is available from Settings > **History & Recovery**, not the primary sidebar.

If automatic metadata matching is not useful, a movie-relevant Add Movies row provides **Edit
Search**. Enter an optional title/year, review up to five deduplicated provider candidates in
structured, vertically scrollable cards, explicitly select one, and then explicitly press **Add to
DropSort Library**. Exactly one valid automatic
candidate is preselected when available, but it still requires that explicit import click. Add Movies
is a remaining-work queue: successful imports disappear, failed items remain retryable, and each row
has a session-only dismiss X. When the queue is exhausted it shows **All done / No movies are waiting
for review**. Already-cataloged paths are omitted from the queue. Add Movies shows the filename rather
than the full filesystem path; technical/details surfaces retain the path.
Manual search text and candidate selection never move, rename, copy, delete, or organize a physical
media file.

Posters load through a bounded background cache and fall back to intentional placeholders when no
poster is available. Automatic/bulk organization, folder watching, and TV library support are not
implemented.

## Source of truth

The active product and architecture source of truth is `docs/source/The Idea-v3.md`. The root
`Skills.md` preserves the repository skill plan. See `docs/README.md` for the documentation map;
historical source material does not override `The Idea-v3.md`.

## Run the Windows release

Download or copy the complete `DropSort` portable directory and run. The packaged executable carries
the branded DropSort application icon, while the `_internal` directory contains the runtime resources:

```text
DropSort.exe
```

Keep the `_internal` directory beside the executable. DropSort does not require an activated virtual
environment, administrator privileges, an installer, or the source repository. The V1 executable is
unsigned, so Windows SmartScreen may show an unrecognized-app warning when it is obtained outside a
trusted distribution channel.

DropSort is single-instance per Windows user/session. Launching `DropSort.exe` again sends a small
local `ACTIVATE` request to the existing process, restores/raises its window, and exits the secondary
process. Ownership uses an atomic Qt lock file; the SQLite catalog is never used as the process lock.
Stale ownership is recovered once after a crash, and no database, media file, or file-operation journal
record is created by a duplicate launch.

For source development only:

```powershell
.venv\Scripts\python.exe -m dropsort
```

The default database is `%LOCALAPPDATA%\DropSort\dropsort.db`.
Downloaded poster assets are stored separately under
`%LOCALAPPDATA%\DropSort\poster-cache` and may be deleted without affecting the catalog or media.
Bounded diagnostic logs are stored under `%LOCALAPPDATA%\DropSort\logs`; credential patterns are
redacted. Runtime paths are resolved independently of the launch working directory.

## Movie metadata configuration

Open **Settings** in the desktop app to enter a masked TMDB Read Access Token for the current
application session. The value remains in memory only, is cleared when DropSort exits, and is never
written to the catalog or a configuration file. `DROPSORT_TMDB_READ_ACCESS_TOKEN` remains available
as an environment fallback for locally managed launches; an explicit session credential takes
priority.

Without a credential, the app still opens and the local library remains available. Add Movies shows
a controlled, actionable route to Settings. Live TMDB authentication is verified only when a normal
metadata request is made.

## TMDB attribution

DropSort uses TMDB for optional movie metadata and poster images. The approved TMDB logo and required
notice are shown in **Settings > About & Credits**:

> This product uses the TMDB API but is not endorsed or certified by TMDB.

TMDB is optional; local library browsing and existing cached posters remain available without a
network connection or credential.

## Media safety and V1 limitations

- Scanning, matching, previews, and Relink do not move or modify media.
- Catalog import requires an explicit user action and records a file at its current location.
- Move/Rename and Undo require separate previews and confirmations, use the durable operation journal,
  never overwrite an existing destination, and update the catalog only after filesystem verification.
- Missing files remain in the catalog. Relink changes only the catalog path and creates no file-operation
  journal entry.
- Clear Library Data forgets catalog data only; it does not delete, move, rename, copy, or modify media.
- V1 has no automatic or bulk organization, folder watcher, TV library, subtitle management, cloud
  sync, auto-updater, or installer.
- The portable executable is unsigned. The exact noisy-domain live TMDB acceptance case requires a
  user-supplied credential and is the only remaining final acceptance retest; Clear Library Data,
  English/Arabic direction and persistence, offline bundled fonts, and Recently Added removal have
  already been verified in the packaged build.
- DropSort itself currently has no explicit project license. Bundled third-party notices and license
  texts are included with the Windows release.

## Development

```powershell
.venv\Scripts\python.exe -m pytest -q -W error
.venv\Scripts\python.exe -m pytest --cov=src/dropsort --cov-branch --cov-report=term-missing -q -W error
```

See `docs/status/PROJECT_STATUS.md` for the detailed implementation boundary, current release-candidate baseline,
and historical limitations. For the verified current baseline, including the current failing tests
and source-traced product-contract gaps, use `docs/audits/Deep-Audit.md`.
