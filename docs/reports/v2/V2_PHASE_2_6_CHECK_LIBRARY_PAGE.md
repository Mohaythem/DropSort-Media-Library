# V2 Phase 2.6 — Check Library Permanent Page

## 1. Scope

Implemented the approved Phase 2.6 presentation and navigation change only:
Check Library is now a permanent MainWindow sidebar/page-stack destination.
The existing file and metadata checking application use cases remain the source
of checking behavior. No roadmap feature outside Check Library was started.

## 2. Previous Dialog Architecture

Before this phase, `LibraryFileCheckDialog` in
`src/dropsort/ui/reconciliation/dialogs.py` was the normal Check Library
presentation. `MainWindow.show_library_file_check()` created and managed that
dialog, while the Library page button emitted `check_files_requested` directly
to that dialog path.

## 3. New Page Architecture

`LibraryCheckPage` in `src/dropsort/ui/reconciliation/page.py` is a persistent
`QWidget` owned by `MainWindow` and added to the existing `QStackedWidget`.
It owns only UI/session coordination: state, progress, cancellation, result
presentation, and navigation signals. It calls the existing
`ReconciliationUiActions` contract through the existing `TaskRunner` boundary.

## 4. Sidebar Integration

`MainWindow` now adds `checkLibraryNavButton` using the existing navigation
button system. The primary order is Library, Personal Library when available,
Add Movies when available, Check Library, then Settings when available.
Operations Log remains in its existing Settings-linked location.

## 5. Library Shortcut

The Library page Check Library button remains available, but its signal now
calls `show_check_library_from_library()`. It navigates to the permanent page
only. It does not start a check, create a worker, or create a dialog.

## 6. Navigation / Back / Escape

Library → Check Library records Library as the return section. Escape returns to
Library. Direct sidebar entry safely no-ops on Escape when there is no meaningful
previous Library context. Existing modal/popup precedence and Movie Details
back navigation remain in MainWindow. Escape while the page is running requests
cancellation instead of closing the application.

## 7. Running Session Ownership

The page instance persists in the page stack while users navigate to Library,
Movie Details, Settings, or other sections. A running page check therefore
continues with the same token and cancellation object. `start_check()` ignores
duplicate starts while running. If the automatic startup file-only
reconciliation is active, a user-started full check is queued behind it and
starts only after that existing job finishes. The startup job remains the
cheap/file-only reconciliation path.

## 8. Idle State

Idle shows the title, existing localized description, status, and primary Check
Library action. It hides the progress bar, percentage, Cancel action, result
summary, issue list, and empty diagnostic content. No last-checked persistence
was invented.

## 9. Running State

Running shows concise stage status, an authoritative checked/total progress
value when supplied by the DTO, an LTR-safe percentage, and Cancel. It does not
render a live developer log or successful-item list.

## 10. Completed Summary

Completion uses two primary values only: `Passed` and `Needs attention`.
Attention includes authoritative file missing/error counts and non-complete
metadata health results. Repaired metadata that is complete at the end of the
check is not mislabeled as needing attention.

## 11. Issue List

Only metadata items whose final status is not COMPLETE are rendered. Healthy
movies are omitted. File missing/error totals are represented by one compact
aggregate Files issue row because the existing file progress DTO provides no
per-movie file issue identity. Provider errors are rendered with a short
localized outcome, not raw provider/debug codes. No repair or relink button was
invented where the page lacks an existing safe item-level action contract.

## 12. Failure / Cancellation

Cancellation remains distinct from process failure. Cancel uses the existing
`ReconciliationCancellation` object and repeated clicks do not issue repeated
requests. Cancellation reports that the library was not modified and offers
Check Again. Genuine process failure reports a compact user-facing message and
offers Try Again without exposing the raw exception.

## 13. Backend Reuse

The page reuses `LocalReconciliationActions.check_library()`, the existing
`CheckLibrary` use case, `LibraryHealthProgress`,
`LibraryReconciliationProgress`, `ReconciliationCancellation`, and
`TaskRunner`. No raw SQL, HTTP, TMDB request, direct filesystem mutation, or
new media repair path was added.

## 14. Localization / RTL

Existing Check Library English and Arabic strings were reused. One genuinely
new catalog key was added with parity:

- `TextId.CHECK_LIBRARY_PASSED`: English `Passed`; Arabic `اجتاز الفحص`.

The page uses the existing `UiLocalizer`, follows application RTL direction,
and keeps progress percentages/technical progress values LTR with Western
digits. No unrelated Arabic catalog wording was rewritten.

## 15. Accessibility

Meaningful accessible names were added for the Check Library navigation item,
primary action, progress and percentage, Cancel, Passed, Needs attention,
issue rows, Check Again, and Try Again. Status is communicated through text
and semantic roles, not color alone.

## 16. Themes

The page uses existing semantic theme roles and shared geometry. Source tests
construct it under Main, Dark, Slate, and Light themes. No raw theme-specific
page colors, images, or illustrations were added.

## 17. Files Created

- `src/dropsort/ui/reconciliation/page.py`
- `tests/unit/ui/test_phase26_check_library_page.py`
- `V2_PHASE_2_6_CHECK_LIBRARY_PAGE.md`

## 18. Files Modified

- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/reconciliation/__init__.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/common/theme.py`
- `tests/unit/ui/test_main_window.py`

The existing `LibraryFileCheckDialog` implementation was not rewritten and its
focused legacy tests remain green.

## 19. Tests

Focused Phase 2.6 tests cover idle, running, duplicate-start protection,
queueing, attaching, navigation persistence, cancellation, failure, healthy
completion, issue completion, issue-only rendering, fallback file-only DTOs,
Arabic/RTL progress, all themes, and Library/sidebar navigation.

Focused result: `8 passed` for the Phase 2.6 page tests.

## 20. Full Regression

Final repository run:

`1120 passed, 5 skipped, 0 failed` in `525.94s`.

The five skips are the existing Windows symlink-privilege skips. No new skip
was introduced.

## 21. Coverage

Final branch coverage: `95%`.

The new `LibraryCheckPage` is covered at `98%` branch-aware file coverage in
the final project run.

## 22. Packaging

Built with the existing PyInstaller specification into the isolated
`.build-phase26-dist` directory. The executable is:

`.build-phase26-dist/DropSort/DropSort.exe`

SHA-256:

`7D477EE2B04A0E85BC42644380C4F18A1D692745C9301B7AFCA6220986B86D71`

The package contains all migration up/down files 0001 through 0004 under
`_internal/dropsort/database/migrations`. The executable launched against
`.build-phase26-smoke-local` and created an empty isolated database/log. The
smoke database contains migration rows `(1), (2), (3), (4)`, zero movies, and
`PRAGMA user_version = 0`; no real media library was accessed.

Source-level construction tests cover English, Arabic/RTL, Main, Dark, Slate,
and Light. The package smoke confirms the current executable starts from this
build; visual package retest remains user-owned below.

## 23. Schema

No schema or migration file was changed. Logical schema remains v4, with
migrations 1–4 applied. No cosmetic last-checked persistence was introduced.

## 24. File-Safety Review

No physical media mutation path was added. The page does not move, rename,
delete, copy, relink, or directly inspect real media paths. Existing
PathPolicy, SafeTransferEngine, journal, undo, recovery, matcher, metadata
provider, and poster-cache boundaries remain untouched. The check continues to
use the existing safe application actions.

## 25. Old Dialog Disposition

`LibraryFileCheckDialog` remains as non-normal legacy presentation because its
existing focused tests and compatibility callers still reference it. It is no
longer connected to the Library shortcut or Check Library sidebar navigation.
The normal user-facing Check Library workflow is the persistent page. The old
dialog was not duplicated into the normal page path and does not own the new
page session.

## 26. Normal Review

The normal workflow is intentionally narrow: navigate, choose when to start,
observe concise progress, cancel if needed, and review only attention items.
The implementation uses the current MainWindow stack, current theme tokens,
current task runner, and existing application action contracts.

## 27. Adversarial Review

Checked duplicate starts, stale callback tokens, cancellation idempotence,
navigation while running, automatic startup reconciliation ordering, empty and
zero-count results, process failure versus discovered issues, RTL progress
direction, absent actions, and isolated package startup. No schema, credential,
real-media, or destructive Git operation was used.

## 28. User Visual Retest

`USER VISUAL RETEST REQUIRED: YES`

Please visually retest the sidebar Check Library item, the Library shortcut,
idle/running/healthy/issues/failure/cancelled states, Main/Dark/Slate/Light,
English/Arabic RTL, and expanded/compact sidebar modes. Also verify that
returning to the page after navigating away preserves the active run.

## 29. Phase Decision

`PASS`

Check Library is now a permanent sidebar/page-stack destination. The Library
Check Library button is a navigation shortcut only and does not start a check
automatically. The normal workflow no longer opens the old modal dialog. The
completion summary is Passed/Needs attention, only attention items are listed,
healthy items are omitted, and discovered issues are not labeled as failed
checks. Running state survives normal navigation without duplicate workers.
Arabic/RTL, Western digits, Main/Dark/Slate/Light, Python/PySide6/Qt, schema
v4, and existing file-safety boundaries remain supported. No new images or
illustrations were added. Fluent icon migration and later typography work are
out of scope.
