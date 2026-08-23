# DropSort Media Library — V2 Phase 2 Personal Library UI Integration

## 1. Scope

Phase 2 makes the Phase 1 personal-library foundation usable from the desktop UI. The implementation covers Movie Details personal state, preference controls, Watchlist, watched events and history, Personal Library navigation and sections, Ready to Watch consumption, metadata-only retained Movies, localization, RTL-safe presentation, themes, accessibility-oriented labels, tests, and packaging.

## 2. Status

PASS with `USER VISUAL RETEST REQUIRED` for human confirmation of the complete English/Arabic and four-theme visual presentation.

## 3. Baseline

Phase 1 baseline was 1,033 passed and 5 expected Windows symlink/reparse-related skips, with branch coverage reported at 95%. Phase 2 began from that passing source state and preserved the Phase 1 migration and safety boundaries.

## 4. UI architecture

Widgets consume `PersonalLibraryUiActions` through the application bootstrap composition layer. SQL remains in the database repository, and widgets do not instantiate repositories, issue SQL, call metadata providers, or call the File Engine. Personal reads and writes use the existing Qt task-runner boundary with token checks for stale delivery.

## 5. Movie Details state

Movie Details now includes a `Your Library` panel. It loads authoritative personal state and history for the selected logical Movie and refreshes the panel after every successful personal change.

## 6. Like and Blacklist

Like and Blacklist are mutually exclusive through the existing application use case. The UI exposes a clear-preference action, disables duplicate actions while a request is running, and displays a localized failure without claiming an unsaved state.

## 7. Watchlist

Movie Details can add or remove a Movie from the Watchlist. The control works for Movies with no physical media file. Its label reflects the authoritative current state after the operation.

## 8. Mark Watched

Mark Watched records the current date/time through the application boundary. Duplicate clicks are blocked while the request is active. The personal summary updates the count, last-watched date, and history after success.

## 9. Historical watch date

A calendar date control supports historical watch entries and limits UI selection to today or earlier. Phase 1 deliberately permits future timestamps at the backend boundary for compatibility with its existing contract/tests; Phase 2 preserves that backend behavior and constrains the desktop picker.

## 10. Watch History

History is presented newest-first with a localized date and First watch/Rewatch label. Event IDs are not shown to users. Each row has a localized remove action; removal refreshes the authoritative state and derived summary.

## 11. Personal Library navigation

The sidebar now has one compact Personal Library entry. Returning from Movie Details preserves whether the user came from Local Library or Personal Library.

## 12. Personal Library views

The page uses internal tabs for Watchlist, Ready to Watch, Liked, and Blacklisted. It reuses the existing MovieGrid/MovieCard presentation and loads each section through the application boundary.

## 13. Ready to Watch

Ready to Watch is consumed from the backend query. The UI does not duplicate the ready predicate. It therefore respects the Phase 1 rules: watchlisted, at least one PRESENT media file, and no watch event.

## 14. Live refresh

Personal Library refreshes after personal changes and section navigation. Token checks ignore stale asynchronous results after a newer request, Movie change, invalidation, or window close.

## 15. Metadata-only UX

Retained logical Movies with no MediaFile can appear in Watchlist, Liked, Blacklisted, and other personal views. Their Details page shows metadata, personal controls, and history without creating physical file actions.

## 16. File availability

The existing Local Library presentation filters fileless logical Movies so it remains a local-file collection. Existing PRESENT/MISSING file status and media actions remain governed by their existing File Engine boundaries. Personal controls never infer or mutate physical file availability.

## 17. Local-library clear integration

The Phase 1 clear confirmation remains accurate: local links/cache are cleared, personal Movies are retained when personal state exists, physical files are not changed, and operation history/recovery records are preserved. After clear, Personal Library and metadata-only Details remain safe.

## 18. Operation History

No personal action writes an operation-history record because personal state changes are database-only and do not authorize physical filesystem work. Existing operation history remains preserved across local-library clear and was covered by the regression suite.

## 19. Localization

All new UI strings were added to both English and Arabic dictionaries with parity validation. New labels include navigation, tabs, personal actions, date/history labels, empty states, and user-facing load/save failures.

## 20. RTL

The Personal Library controls use ordinary Qt layouts and localizer-bound strings, so Arabic layout direction follows the existing application behavior. Dates, paths, and technical values remain suitable for LTR rendering where applicable.

## 21. Themes

Main, Dark, Slate, and Light themes reuse semantic colors. Personal state panels, history rows, tabs, disabled controls, and action roles were added to the shared theme stylesheet without introducing per-widget hardcoded theme colors.

## 22. Accessibility

Controls have stable object names, existing action roles, readable text, selectable technical values where appropriate, and ordinary keyboard-focusable Qt widgets. The MovieCard retains its accessible detail-opening name.

## 23. Files created

- `src/dropsort/application/dto/personal_library.py`
- `src/dropsort/ui/personal_library/__init__.py`
- `src/dropsort/ui/personal_library/personal_library_view.py`
- `tests/unit/ui/test_personal_library_ui.py`
- `V2_PHASE_2_PERSONAL_LIBRARY_UI.md`

## 24. Files modified

The Phase 2 implementation modified the personal domain exports/repository protocol, SQLite personal repository, personal use-case exports, application DTO exports, desktop bootstrap composition, UI contracts, MovieGrid/MovieCard, Movie Details, MainWindow, localization, shared theme, and the Phase 1 personal integration test module.

## 25. Database/schema scope

No Phase 2 migration was added and no database schema was changed. The existing Phase 1 migration 0004 remains the source of `movie_personal_state` and `watch_events`.

## 26. Focused tests

Focused UI architecture, Movie Details, MainWindow, desktop bootstrap, localization, personal application, and new Personal Library UI tests passed. The new tests cover section routing, stale callbacks, failure states, metadata-only controls, preference transitions, Watchlist transitions, historical watched dates, history removal, navigation return context, and composition routing.

## 27. Full test suite

Final full run: `1042 passed, 5 skipped, 0 failed`. The five skips are expected host privilege limitations for symlink/reparse-point scenarios on this Windows environment.

## 28. Coverage

Final command used branch coverage over the full `dropsort` package. Reported result: 95% coverage, with the Phase 2 personal UI and application paths included.

## 29. Packaging

PyInstaller 6.22.0 completed successfully using `DropSort.spec` with disposable output directories `.build-phase2-dist` and `.build-phase2-work`. The output contains `DropSort.exe`, all eight migration SQL files, and the existing SVG resources.

Final executable evidence: 2,386,092 bytes; SHA-256 `D339F4A06C71A899C170CB283F1B25EFDB767FAC8B8F80827A4BD0ADF2B6CC94`.

## 30. Packaged smoke

The packaged executable started under `QT_QPA_PLATFORM=offscreen` with `LOCALAPPDATA` redirected to the workspace-local `.build-phase2-smoke-local` directory. It initialized the isolated database, was stopped after five seconds, and did not access the real user media library. Read-only inspection confirmed schema versions 1–4 and the personal tables.

## 31. Safety verification

Personal UI operations call only database-backed personal use cases. They do not invoke Play/Open Folder/Organize/Relink for metadata-only Movies and do not call the File Engine. Existing file safety, journaling, recovery, matcher, and scanner behavior was not redesigned.

## 32. Normal and adversarial checks

Covered scenarios include empty personal sections, metadata-only watchlist entries, Ready to Watch exclusion for missing files and watched Movies, mutually exclusive preferences, stale async results, failed personal reads/writes, removal of historical events, local-library clear retention, preserved operation history, and packaged startup against an isolated runtime.

## 33. Explicit exclusions

No personal numeric rating system or `rating_snapshot` was implemented.

No `Favorite` feature was implemented.

No Discover, Letterboxd, Analytics, Diary/Reviews/Tags, TV, Subtitles, or Folder Watcher scope was implemented.

## 34. Limitations and deferred work

Human visual confirmation is still required for the complete desktop presentation in English and Arabic across Main, Dark, Slate, and Light themes, including RTL spacing and long translated labels. Future work may improve visual polish, pagination, and richer personal-library summaries without changing the Phase 2 safety boundary.

## 35. Decision

Phase 2 implementation is accepted as code-complete and verification-complete for automated checks and isolated packaging. The remaining manual visual check is explicitly not claimed as completed.

Personal Library actions do not authorize or perform physical filesystem mutation.
