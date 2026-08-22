# DropSort Phase 4D Report

## Result

**Phase 4D - Safe Local Actions + Finalized Visual Identity: GREEN**

This report covers only the implementation, testing, review, fixes, and verification performed for
Phase 4D. No Move/Rename workflow, automatic organization, watcher, TV support, or packaging was
implemented.

## Implemented

### Safe local actions

- Added a provider-neutral `LocalMediaActions` protocol and controlled error types under
  `src/dropsort/library/playback/`.
- Added `WindowsLocalMediaActions`:
  - validates an absolute path with no-follow `lstat`;
  - requires an existing regular file;
  - rejects directories, links, and reparse points;
  - uses the Windows default association for Play Movie;
  - invokes Explorer with an explicit `("explorer.exe", "/select,", path)` argument tuple;
  - never uses `shell=True` or command-string interpolation;
  - translates inspection, association, Explorer, and TOCTOU launch failures into controlled errors.
- Injected this boundary through desktop composition into Movie Details. Widgets never call OS
  launch or filesystem validation APIs directly.
- Added one **Play Movie** and one **Open Folder** button to every physical-media row. With multiple
  files, each pair remains bound to its exact file; no quality-ranking preference was invented.
- Missing files and launch failures show per-file messages while the rest of Movie Details remains
  usable.

### Visual identity

The centralized active palette is:

```css
--text:       #FFF1A6;
--background: #0B1E1B;
--primary:    #013C35;
--secondary:  #6B352A;
--accent:     #E87454;
```

Typography is centralized in `src/dropsort/ui/common/theme.py`:

```text
Typeface: Margarine
Body weight: 400
Heading weight: 700
Body: 16px
H1: 67.36px
H2: 50.56px
H3: 37.92px
H4: 28.48px
H5: 21.28px
Small: 12px
```

`Margarine-Regular.ttf` and the SIL Open Font License 1.1 are bundled under
`src/dropsort/ui/assets/fonts/`. Qt registers the asset once with
`QFontDatabase.addApplicationFont`; registration failure falls back centrally to Segoe UI without
crashing. Google Fonts is the source. No runtime font networking or new package dependency exists.
Because the official bundled asset contains one regular face, Qt synthesizes the requested 700
heading emphasis.

### Phase 4C regression fixed during the gate

Coverage exposed a real timestamp-tie defect in poster-cache LRU eviction: under NTFS timing and
instrumentation, two accesses could share a modification time and the wrong asset could be evicted.
Cache touches now use strictly monotonic nanosecond timestamps. A tied-clock regression test makes
the eviction order deterministic.

## Files created

- `src/dropsort/library/playback/contracts.py`
- `src/dropsort/library/playback/errors.py`
- `src/dropsort/library/playback/windows.py`
- `src/dropsort/ui/assets/fonts/Margarine-Regular.ttf`
- `src/dropsort/ui/assets/fonts/OFL.txt`
- `src/dropsort/ui/assets/fonts/README.md`
- `tests/unit/library/playback/__init__.py`
- `tests/unit/library/playback/test_architecture.py`
- `tests/unit/library/playback/test_windows_actions.py`
- `tests/integration/application/test_local_media_actions_zero_mutation.py`
- `PHASE_4D_REPORT.md`

## Files modified

- `pyproject.toml`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/library/playback/__init__.py`
- `src/dropsort/posters/cache.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/library/library_view.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/scan/import_view.py`
- `src/dropsort/ui/settings/settings_view.py`
- `tests/unit/posters/test_cache.py`
- `tests/unit/ui/test_movie_details_view.py`
- `tests/unit/ui/test_theme.py`
- `tests/unit/ui/test_ui_architecture.py`
- `PROJECT_STATUS.md`
- `README.md`

## Tests and coverage

Pre-change baseline:

```text
628 passed
5 skipped
0 failed
95% total branch coverage
```

Final verification:

```text
Phase 4D + complete unit UI focused: 99 passed, 0 failed
Local-action boundary: 18 passed, 100% branch coverage
Poster-cache regression: 31 passed, 1 skipped, 0 failed
Full suite: 653 passed, 5 skipped, 0 failed
Total branch coverage: 95%
```

The five full-suite skips are the established native-Windows symlink-creation privilege
limitations. Tests cover special-character and Unicode paths, missing files, directories,
permission failures, absent associations, Explorer failure, TOCTOU disappearance, exact per-file
UI binding, multiple files, controlled feedback, theme/font contracts, no shell interpolation, no
UI platform calls, no File Engine coupling, and zero catalog/journal/media mutation.

## Review findings and fixes

1. **LRU timestamp ties**
   - Risk: the wrong cached poster could be evicted on tied NTFS timestamps.
   - Fix: strictly monotonic access timestamps plus deterministic tied-clock regression coverage.

2. **Command injection and quoting**
   - Risk: spaces, punctuation, Unicode, ampersands, parentheses, apostrophes, hashes, or commas
     could become shell syntax.
   - Fix: paths remain individual data arguments; Play uses `os.startfile`; Explorer uses an
     explicit tuple; architecture tests prohibit `shell=True` and direct widget launch APIs.

3. **Stale or substituted media paths**
   - Risk: a missing, directory, link/reparse, inaccessible, or post-validation-disappearing target
     could be launched incorrectly.
   - Fix: no-follow runtime validation and controlled error translation. No catalog reconciliation
     or mutation occurs.

4. **Theme/font duplication and startup failure**
   - Risk: scattered colors, runtime font networking, or a missing asset could make the UI
     inconsistent or prevent startup.
   - Fix: authoritative centralized tokens, bundled licensed asset, one registration boundary, and
     a tested safe fallback.

No unresolved BLOCKER or CRITICAL finding remains.

## Manual native-Windows verification

The cataloged movie **Prisoners (2013)** was used.

- DropSort launched with the new palette and Margarine typography.
- Library, Recently Added, Movie Details, Add Movies, and Settings were visually reviewed at the
  current Windows desktop scale; no clipping, overflow, or broken poster layout was observed.
- **Play Movie** launched the exact MP4 through its association in MPC-HC.
- **Open Folder** opened the correct Explorer folder with the exact movie selected; Explorer
  reported one selected item at 2.04 GB.
- DropSort remained responsive after both launches and retained the poster/details view.
- Before and after: the catalog path stayed identical, the physical file remained present and
  regular at 2,201,235,337 bytes, and the file-operation journal count remained zero.
- Application stdout/stderr logs were empty; no exception, Qt lifecycle error, credential leak, or
  path-safety error was observed.

Import Review row presentation was covered by the existing automated UI suite rather than a new
live TMDB scan during this phase. Windows 125% and 150% system scaling were not changed manually;
the visual smoke used the current desktop scale.

## Dependencies

Dependencies changed: none. Margarine is a bundled application asset, not a Python dependency.

## Known limitations

- Play Movie relies on the user's Windows default association; DropSort does not embed a player.
- Missing-file reconciliation, relinking, and status updates are not performed by local actions.
- Local actions are Windows-specific in this phase.
- The single Margarine regular face requires Qt-synthesized heading emphasis.
- No Move/Rename authorization, automatic organization, watcher, TV workflow, or packaging exists.

## Recommended next phase

**Phase 5A - Safe Organize Preview + Explicit Move/Rename Authorization UI.**

This phase is recommended only and was not implemented.
