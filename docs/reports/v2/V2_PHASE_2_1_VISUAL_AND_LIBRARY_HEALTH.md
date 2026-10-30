# DropSort Media Library — V2 Phase 2.1 Visual and Library Health

## 1. Scope

Phase 2.1 covers the requested visual polish, semantic theme redesign, Personal Library empty states, and the explicit metadata-aware Check Library workflow. The populated Personal Library grid, Phase 1/2 personal-library model, local-first architecture, and file safety boundaries were preserved.

Status: PASS for automated implementation and verification, with USER VISUAL RETEST REQUIRED for human confirmation of English/Arabic presentation across Main, Dark, Slate, and Light themes.

## 2. User findings addressed

The populated Personal Library path already used the existing MovieGrid and personal application boundary. The empty sections were the visual gap: they showed only a plain state label. The Dark, Slate, and Light palettes also still used the earlier dull/near-black mappings. The previous manual Check Library action reconciled cataloged file paths only.

Phase 2.1 adds deliberate empty-state panels, replaces the three requested semantic palettes while preserving theme identifiers, and extends the manual action to file health plus bounded metadata health and safe repair.

## 3. Personal Library empty states

Each empty section now renders a centered, responsive, theme-aware panel with:

- a decorative icon;
- a localized section-specific title;
- a localized section-specific description;
- a stable accessible name and object names;
- ordinary Qt layouts that follow the existing Arabic RTL direction.

Watchlist, Ready to Watch, Liked, and Blacklisted have distinct English and Arabic copy. Populated sections continue to render the existing MovieGrid/MovieCard path without a redesign.

## 4. Theme redesign

The Main brand tokens remain unchanged: `#FFF1A6`, `#0B1E1B`, `#013C35`, `#6B352A`, and `#E87454`.

The user-facing semantic palettes are:

| Theme | Background | Surface | Raised surface | Card | Card hover | Border | Primary | Accent |
|---|---|---|---|---|---|---|---|---|
| Dark Warm Graphite | `#1C1D1F` | `#232528` | `#2A2D31` | `#26282C` | `#303338` | `#3A3D42` | `#D9A441` | `#C86A4A` |
| Slate Cool Midnight | `#151B23` | `#1C2530` | `#24303D` | `#202B37` | `#293746` | `#344556` | `#7EA6C9` | `#B88A65` |
| Light Warm Paper | `#F4F1EA` | `#FBF9F4` | `#FFFFFF` | `#F8F6F1` | `#EFEAE1` | `#DDD6CB` | `#356A61` | `#C96F4F` |

Each palette also derives semantic text-secondary/text-muted, selected, focus, disabled, success, warning, danger, primary-hover, and accent-hover tokens. Shared stylesheet rules now use those tokens for the sidebar, cards, tabs, buttons, fields, progress bars, scroll areas, disabled states, focus states, Personal Library empty states, and metadata/search surfaces. A repository scan found raw colors only in the shared theme source, not in individual widgets.

Existing theme IDs and persisted names remain compatible: Main, Dark, Slate, and Light.

## 5. Explicit Check Library architecture

The existing file reconciliation use case remains the file-health engine. `CheckLibrary` composes it with a separate bounded Movie repository scan. The manual dialog prefers the new `check_library` application action and retains a file-only fallback for older composition doubles.

Startup reconciliation remains explicitly file-only. It still calls `reconcile_library_files` and does not perform a TMDB sweep. If a user opens the full Check Library dialog while startup reconciliation is active, the explicit full check is queued behind it rather than running two reconciliation jobs concurrently.

The Qt task runner owns background execution and progress delivery. Widgets do not issue SQL, filesystem operations, HTTP requests, provider requests, or cache writes.

## 6. File-health behavior

The existing PRESENT/MISSING/error reconciliation behavior is preserved, including known-path-only inspection, bounded pages, cancellation, stale-token delivery protection, and relink safety. File reconciliation performs no physical media mutation and continues to report zero `file_operations` for status checks.

The explicit result presents a Files section with checked, present, missing, and error counts. Automatic startup status remains the existing file-only presentation.

## 7. Metadata-health rules

The health scan inspects the current Movie catalog for provider identity, title validity, year, overview, runtime, genres, poster reference, and poster cache availability when the existing PosterAssetService is configured. Populated `original_title` and rating values are preserved; their absence is not treated as corruption.

Missing or unreliable provider identity produces `NEEDS_MATCH`. The checker does not guess a provider ID, perform a title-based identity mutation, or call the provider for such a Movie.

The scan uses key statuses only: COMPLETE, INCOMPLETE, MISSING_POSTER, NEEDS_MATCH, PROVIDER_UNAVAILABLE, and PROVIDER_VALUE_UNAVAILABLE. Issue lists are bounded to 100 items and metadata pages are processed sequentially with a configurable positive batch size.

## 8. Metadata repair behavior

With valid provider identity, the existing provider/cache boundary is used. Only missing or invalid local values are filled. Existing title, provider identity, external ID, original title, year, rating, and already-populated descriptive values are preserved. A provider response with a mismatched identity is treated as invalid and is not written.

A legitimately empty provider value is reported as `PROVIDER_VALUE_UNAVAILABLE`, not as a transport or authentication error. Authentication, unavailable/network, rate-limit, and invalid-response failures remain distinguishable in the health result and UI issue list.

No database migration was added. The current Movie schema and repository update contract are sufficient for derived health and merged missing-field updates.

## 9. Poster repair behavior

Existing poster references are checked through the app-owned PosterAssetService and cache. A cache miss may recover through the existing provider source and cache only. A poster reference is never interpreted as a user media path. Poster cache failures degrade to a safe missing-poster result; they do not mutate media files or Movie identity.

The deterministic poster test confirms a cache miss can recover through the app-owned cache and that the catalog update list remains empty for a poster-only cache repair.

## 10. Offline and credential behavior

The safe environment check found no configured TMDB credential; no credential value was printed or exposed. A missing credential still allows file reconciliation, local metadata inspection, and completion of the health report. Provider repair is skipped for unavailable metadata and the UI reports the authentication/configuration reason without claiming repair.

No live TMDB verification was performed because no safe credential was available. Live provider behavior is therefore explicitly NOT VERIFIED in this environment.

## 11. Files created

- `src/dropsort/application/dto/library_health.py`
- `src/dropsort/application/use_cases/check_library.py`
- `tests/unit/application/test_check_library.py`
- `V2_PHASE_2_1_VISUAL_AND_LIBRARY_HEALTH.md`

## 12. Files modified

- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/dto/__init__.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/database/repositories/movies.py`
- `src/dropsort/library/movies/repositories.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/personal_library/personal_library_view.py`
- `src/dropsort/ui/reconciliation/dialogs.py`
- `tests/integration/database/test_movie_catalog_repository.py`
- `tests/unit/ui/test_main_window.py`
- `tests/unit/ui/test_reconciliation_dialogs.py`
- `tests/unit/ui/test_theme.py`

No migration or production file-engine mutation behavior was added.

## 13. Deterministic and adversarial tests

The new tests cover complete Movies with zero provider requests, missing overview/runtime/genres/year/poster repair, preservation of populated fields, missing provider identity, no-credential/authentication failure, network/unavailable/rate-limit/invalid-response failures, provider-legitimately-empty values, provider identity mismatch, missing and recoverable poster cache, metadata-only Movies, bounded multiple pages, bounded issue output, cancellation, invalid timestamps, zero catalog update for poster-only repair, localized health results, empty health results, startup/manual queueing, stale delivery, and the preserved file-only fallback.

The existing reconciliation, File Engine, journal, recovery, matcher, scanner, personal-library, UI architecture, and packaging tests remain in the full suite.

## 14. Full test and coverage verification

Final full-suite command:

```text
.venv\Scripts\python.exe -m pytest -q --basetemp=.pytest-v2-phase21-final
```

Result: `1073 passed, 5 skipped, 0 failed`. The five skips are expected Windows host privilege limitations for symlink/reparse-point scenarios.

Final branch-coverage command:

```text
.venv\Scripts\python.exe -m pytest -q --cov=dropsort --cov-branch --cov-report=term --basetemp=.pytest-v2-phase21-final
```

Result: `95%` combined statement/branch coverage with branch measurement enabled. The new `check_library.py` path itself reports 99%. No coverage threshold failure is configured in `pyproject.toml`.

## 15. Packaging and isolated smoke

PyInstaller 6.22.0 completed successfully with disposable directories `.build-phase21-dist` and `.build-phase21-work`.

Artifact:

- `.build-phase21-dist\DropSort\DropSort.exe`
- Size: 2,402,271 bytes
- SHA-256: `3991ECDBD8BC2ADEF72494F0843E4D2E54023D24AEE3BF0FF4C61B2452ADF49E`

The packaged output contains all eight existing migration SQL files and the existing fonts/SVG/icon resources. The executable was launched with `QT_QPA_PLATFORM=offscreen` and `LOCALAPPDATA` redirected to `.build-phase21-smoke-local`; it remained running after five seconds and was then stopped. Read-only SQLite inspection confirmed the isolated database, all four schema migrations, and the expected catalog/personal/cache tables. The real user media library was not accessed.

## 16. File-safety review

Library health calls only the existing file reconciliation status path and app-owned metadata/poster cache boundaries. It does not call organize, move, copy, rename, delete, relink, or recovery operations. It does not accept a user media root as a poster cache root. Metadata repair uses identity-preserving repository updates and does not create `file_operations` rows.

The required safety confirmations are explicit:

Library metadata repair performs zero physical-media mutation.

Metadata repair does not weaken Movie matching/identity safety.

## 17. Scope confirmations

No personal numeric rating or rating_snapshot was introduced.

No Favorite feature was introduced.

No Discover, Letterboxd, Analytics, TV, Subtitles, Folder Watcher, or Storage Dashboard was implemented.

## 18. User visual retest items

Human confirmation remains requested for the desktop application in English and Arabic across Main, Dark Warm Graphite, Slate Cool Midnight, and Light Warm Paper. Please inspect empty Watchlist/Ready/Liked/Blacklisted panels, populated card grids, details, Add Movies/search, Settings, History, dialogs, buttons, tabs, disabled/focus states, progress bars, and RTL spacing with long translated strings.

## 19. Phase result

Phase 2.1 is implementation-complete and automated-verification complete. It is ready for the requested human visual retest. Git/GitHub work remains out of scope.
