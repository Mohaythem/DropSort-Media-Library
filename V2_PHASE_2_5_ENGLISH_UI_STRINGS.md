# DropSort Media Library — English UI String Inventory

This inventory records the current English user-facing source copy relevant to the Phase 2.5 review. It is intentionally English-only. Arabic wording is not rewritten here; `src/dropsort/ui/localization.py` remains the localization source of truth.

Placeholders are preserved exactly as they appear in the source catalog.

## Main Window and Navigation

Key: `window.title`  
Screen: Main Window  
English: DropSort Media Library  
Context: Native application window title.

Key: `brand.subtitle`  
Screen: Main Window  
English: Local movie library  
Context: Brand/sidebar subtitle.

Key: `nav.library`  
Screen: Main Window  
English: Library  
Context: Navigation item and Library section.

Key: `nav.add_movies`  
Screen: Main Window  
English: Add Movies  
Context: Navigation item and import section.

Key: `nav.history`  
Screen: Main Window  
English: Operations Log  
Context: Navigation item for managed file operations.

Key: `nav.settings`  
Screen: Main Window  
English: Settings  
Context: Navigation item.

Key: `nav.personal_library`  
Screen: Main Window  
English: Personal Library  
Context: Navigation item.

Key: `library.heading`  
Screen: Library  
English: Your Library  
Context: Library page heading.

Key: `personal.heading`  
Screen: Personal Library  
English: Personal Library  
Context: Personal Library page heading.

Key: `settings.title`  
Screen: Settings  
English: Settings  
Context: Settings page heading.

## Settings, Themes, and Language

Key: `settings.appearance`  
Screen: Settings  
English: Appearance  
Context: Appearance card heading.

Key: `settings.theme`  
Screen: Settings  
English: Theme  
Context: Theme ComboBox label and accessible name.

Key: `settings.theme.main`  
Screen: Settings  
English: Main  
Context: Theme option.

Key: `settings.theme.dark`  
Screen: Settings  
English: Dark  
Context: Theme option.

Key: `settings.theme.slate`  
Screen: Settings  
English: Slate  
Context: Theme option.

Key: `settings.theme.light`  
Screen: Settings  
English: Light  
Context: Theme option.

Key: `settings.language.title`  
Screen: Settings  
English: Language  
Context: Language control heading.

Key: `settings.language.description`  
Screen: Settings  
English: Choose the language used by the DropSort interface.  
Context: Language control description.

Key: `settings.language.english`  
Screen: Settings  
English: English  
Context: Language toggle state.

Key: `settings.language.arabic`  
Screen: Settings  
English: Arabic  
Context: English inventory label for the Arabic language selection.

Key: `settings.language.accessible`  
Screen: Settings  
English: Language: English or Arabic  
Context: Language toggle accessible description.

Key: `settings.danger_zone`  
Screen: Settings  
English: Danger Zone  
Context: Clear Library Data warning container heading.

Key: `settings.library_data`  
Screen: Settings  
English: Library Data  
Context: Clear Library Data subsection heading.

Key: `settings.clear_library.description`  
Screen: Settings  
English: Remove indexed movie and file data from DropSort. Your actual movie files will not be deleted.  
Context: Danger Zone explanation.

Key: `settings.clear_library`  
Screen: Settings  
English: Clear Library Data  
Context: Danger Zone action button.

Key: `settings.clear_library.title`  
Screen: Settings  
English: Clear Library Data?  
Context: Confirmation dialog title.

Key: `settings.clear_library.confirm`  
Screen: Settings  
English: This clears local media-file links, metadata cache, and poster cache. Movies with personal data are retained; movies without retained personal data are forgotten. DropSort will NOT delete, move, rename, copy, or modify your physical movie files. Operation history and recovery records are preserved. Continue?  
Context: Confirmation dialog body; no placeholders.

Key: `settings.clear_library.running`  
Screen: Settings  
English: Clearing local library data...  
Context: Clear operation progress.

Key: `settings.clear_library.result`  
Screen: Settings  
English: Library cleared: {movies} movies and {files} media-file links removed.  
Context: Clear operation result; placeholders `{movies}` and `{files}` are preserved.

Key: `settings.clear_library.cache_warning`  
Screen: Settings  
English:  Poster cache cleanup could not be completed; media files were unaffected.  
Context: Clear operation warning.

## TMDB Setup and Metadata

Key: `settings.tmdb.title`  
Screen: Settings  
English: TMDB Metadata  
Context: Metadata settings heading.

Key: `settings.tmdb.infobar.title`  
Screen: Settings  
English: TMDB setup  
Context: Offline setup help title.

Key: `settings.tmdb.infobar.description`  
Screen: Settings  
English: DropSort uses TMDB for movie metadata and posters. Core setup instructions are available offline.  
Context: InfoBar description.

Key: `settings.tmdb.setup_guide`  
Screen: Settings  
English: Setup Guide  
Context: Opens the local setup guide.

Key: `settings.tmdb.open_official`  
Screen: Settings  
English: Open TMDB  
Context: Opens the official TMDB site in the system browser.

Key: `settings.tmdb.not_configured`  
Screen: Settings  
English: Not configured  
Context: Credential status.

Key: `settings.tmdb.environment`  
Screen: Settings  
English: Configured from environment  
Context: Credential status.

Key: `settings.tmdb.session`  
Screen: Settings  
English: Configured for this session  
Context: Credential status.

Key: `settings.tmdb.session_notice`  
Screen: Settings  
English: A token entered here is used only for this application session and is not permanently stored.  
Context: Credential safety notice.

Key: `settings.tmdb.token_placeholder`  
Screen: Settings  
English: TMDB Read Access Token  
Context: Masked token field placeholder.

Key: `settings.tmdb.save_session`  
Screen: Settings  
English: Save for Session  
Context: Session-only credential action.

Key: `settings.tmdb.clear_session`  
Screen: Settings  
English: Clear Session Token  
Context: Clears session credential.

Key: `tmdb.rating.label`  
Screen: Movie Card; Movie Details  
English: TMDB  
Context: Read-only provider rating prefix.

Key: `tmdb.rating.unavailable`  
Screen: Movie Details  
English: TMDB rating unavailable  
Context: Missing provider rating state.

## Library and Search

Key: `library.search.placeholder`  
Screen: Library; Personal Library  
English: Search your library...  
Context: Local-only header search placeholder.

Key: `library.search.clear`  
Screen: Library; Personal Library  
English: Clear library search  
Context: Search accessibility label.

Key: `library.search.no_results`  
Screen: Library; Personal Library  
English: No movies found  
Context: Empty filtered result state.

Key: `library.search.suggestion`  
Screen: Library; Personal Library  
English: Local movie suggestion  
Context: Search completer accessible description.

Key: `library.empty`  
Screen: Library  
English: Your movie library is empty.  
Context: Initial empty state.

Key: `library.load_error`  
Screen: Library  
English: DropSort could not load the local library. Please try again.  
Context: Local query error.

Key: `library.check_files`  
Screen: Library  
English: Check Library  
Context: Existing Check Library action; inventory only, no Phase 2.5 redesign.

Key: `library.missing_file`  
Screen: Library; Movie Card  
English: Missing file  
Context: Missing media state.

Key: `library.missing_files`  
Screen: Library; Movie Card  
English: {count} missing files  
Context: Missing media count; placeholder `{count}` is preserved.

Key: `library.file` / `library.files`  
Screen: Movie Card  
English: file / files  
Context: File-count noun selection.

## Movie Details and Watch History

Key: `details.back`  
Screen: Movie Details  
English: ← Back  
Context: Back navigation action.

Key: `details.overview`  
Screen: Movie Details  
English: Overview  
Context: Overview section heading.

Key: `details.media_files`  
Screen: Movie Details  
English: Media Files  
Context: Linked media section heading.

Key: `details.original_title`  
Screen: Movie Details  
English: Original title: {title}  
Context: Original title metadata; placeholder `{title}` is preserved.

Key: `details.your_library`  
Screen: Movie Details  
English: Your Library  
Context: Personal state panel heading.

Key: `details.preference_group`  
Screen: Movie Details  
English: Preference  
Context: Preference group heading.

Key: `details.watchlist_group`  
Screen: Movie Details  
English: Watchlist  
Context: Watchlist group heading.

Key: `details.watching_group`  
Screen: Movie Details  
English: Watching  
Context: Watch event group heading.

Key: `details.like` / `details.blacklist` / `details.clear_preference`  
Screen: Movie Details  
English: Like / Blacklist / Clear preference  
Context: Personal preference actions.

Key: `details.add_watchlist` / `details.in_watchlist`  
Screen: Movie Details  
English: Add to Watchlist / In Watchlist  
Context: Watchlist state action.

Key: `details.mark_watched`  
Screen: Movie Details  
English: Mark Watched  
Context: Records a watch using the existing WatchEvent action.

Key: `details.mark_watched_date`  
Screen: Movie Details  
English: Mark Watched on Date  
Context: Records a watch using the selected DatePicker date.

Key: `details.watch_date`  
Screen: Movie Details  
English: Watched date  
Context: DatePicker label and accessible name.

Key: `details.watch_date_future`  
Screen: Movie Details  
English: Choose today or an earlier date.  
Context: Date selection guidance.

Key: `details.watched_count`  
Screen: Movie Details  
English: Watched {count} time(s)  
Context: Watch count; placeholder `{count}` is preserved.

Key: `details.last_watched`  
Screen: Movie Details  
English: Last watched: {date}  
Context: Last watch date; placeholder `{date}` is preserved.

Key: `details.not_watched`  
Screen: Movie Details  
English: Not watched yet  
Context: Empty watch state.

Key: `details.watch_history`  
Screen: Movie Details  
English: Watch History  
Context: Watch event list heading.

Key: `details.first_watch` / `details.rewatch` / `details.remove_watch_event`  
Screen: Movie Details  
English: First watch / Rewatch / Remove  
Context: Watch history row labels/actions.

## Personal Library

Key: `personal.tab_watchlist` / `personal.tab_ready` / `personal.tab_liked` / `personal.tab_blacklisted`  
Screen: Personal Library  
English: Watchlist / Ready to Watch / Liked / Blacklisted  
Context: Personal Library tabs.

Key: `personal.empty_watchlist`  
Screen: Personal Library  
English: Your watchlist is empty.  
Context: Watchlist empty state.

Key: `personal.empty_watchlist_description`  
Screen: Personal Library  
English: Movies you save for later will appear here.  
Context: Watchlist empty-state description.

Key: `personal.empty_ready`  
Screen: Personal Library  
English: Nothing is ready to watch yet.  
Context: Ready-to-watch empty state.

Key: `personal.empty_ready_description`  
Screen: Personal Library  
English: Watchlisted movies with a local copy will appear here.  
Context: Ready-to-watch empty-state description.

## Add Movies and Dialogs

Key: `scan.title` / `scan.guidance`  
Screen: Add Movies  
English: Add Movies / Choose a folder. DropSort scans it read-only, then prepares metadata candidates for your review.  
Context: Import page heading and guidance.

Key: `scan.folder_dialog`  
Screen: Add Movies  
English: Choose a movie folder  
Context: Folder picker title.

Key: `scan.folder_dialog` / `scan.no_folder` / `scan.choose_folder`  
Screen: Add Movies  
English: Choose Folder and Scan / No folder selected / Choose a folder to begin a read-only scan.  
Context: Scan controls and empty state.

Key: `scan.cancel` / `scan.recursive`  
Screen: Add Movies  
English: Cancel Scan / Include subfolders  
Context: Scan controls.

Key: `scan.progress.discovery`  
Screen: Add Movies  
English: Folders: {folders} · Files inspected: {files} · Media candidates: {movies}  
Context: Scan progress; placeholders are preserved.

Key: `scan.complete`  
Screen: Add Movies  
English: Scan complete. Files inspected: {files}. Ready to add/review: {ready}. Already in library: {existing}. Errors: {errors}.  
Context: Scan result; placeholders are preserved.

Key: `scan.cancelled`  
Screen: Add Movies  
English: Scan cancelled. Files inspected before cancellation: {files}. Partial results were discarded. No files were changed.  
Context: Safe cancellation result; placeholder `{files}` is preserved.

Key: `scan.search_manually` / `scan.search_tmdb` / `scan.select_this_movie`  
Screen: Add Movies  
English: Search Manually / Search TMDB / Select This Movie  
Context: Manual metadata search controls.

Key: `scan.year` / `scan.invalid_year`  
Screen: Add Movies  
English: Year (optional) / Enter a valid four-digit year or leave the year blank.  
Context: Technical year input and validation.

## Operations Log

Key: `history.title`  
Screen: Operations Log  
English: Operations Log  
Context: Main page title.

Key: `history.guidance`  
Screen: Operations Log  
English: Recent file operations performed by DropSort.  
Context: Page subtitle.

Key: `common.refresh` / `history.copy` / `history.save`  
Screen: Operations Log  
English: Refresh / Copy / Save  
Context: Command toolbar actions.

Key: `history.choose_operation`  
Screen: Operations Log  
English: Select operation  
Context: Operation-row checkbox accessible name.

Key: `history.empty`  
Screen: Operations Log  
English: No file operations yet.\n\nFile moves and other managed operations will appear here.  
Context: Empty state.

Key: `history.copy_empty`  
Screen: Operations Log  
English: Select an operation to copy.  
Context: Copy command with no selection.

Key: `history.save_success` / `history.save_error`  
Screen: Operations Log  
English: Operations log saved. / The operations log could not be saved.  
Context: Save result states.

Key: `history.from` / `history.to` / `history.operation_id`  
Screen: Operations Log  
English: From / To / Operation ID  
Context: Technical operation details and plain-text export.

Key: `history.operation.move` / `history.operation.rename`  
Screen: Operations Log  
English: Move / Rename  
Context: User-facing operation type labels.

Key: `history.status.planned` / `history.status.validated` / `history.status.in_progress`  
Screen: Operations Log  
English: Planned / Validated / In progress  
Context: Operation status labels.

Key: `history.status.verified` / `history.status.completed` / `history.status.failed`  
Screen: Operations Log  
English: Verified / Completed / Failed  
Context: Operation status labels.

Key: `history.status.recovery_required`  
Screen: Operations Log  
English: Recovery required  
Context: Safety-sensitive operation status.

Key: `history.details` / `common.details` / `common.close`  
Screen: Operations Log; Operation Details  
English: Operation Details / Details / Close  
Context: Details dialog and row controls.

Key: `history.read_only`  
Screen: Operation Details  
English: History details are read-only.  
Context: Details safety notice.

Key: `history.field.state` / `history.field.operation` / `history.field.source` / `history.field.destination`  
Screen: Operation Details  
English: State / Operation / Source / Destination  
Context: Technical details fields.

Key: `history.field.strategy` / `history.field.current_path` / `history.field.created`  
Screen: Operation Details  
English: Strategy / Current catalog path / Created  
Context: Technical details fields.

Key: `history.not_recorded` / `history.not_linked`  
Screen: Operation Details  
English: Not recorded / Not linked  
Context: Missing technical detail values.

Key: `history.recovery` / `history.preview_undo` / `history.inspect_recovery` / `history.attempt_recovery`  
Screen: Settings; Operation Details  
English: History & Recovery / Preview Undo / Inspect Recovery / Attempt Safe Recovery  
Context: Safety and recovery controls.

## Check Library Inventory Only

These strings are included for completeness because Check Library exists in the application. Check Library was not redesigned or moved in Phase 2.5.

Key: `reconcile.title` / `reconcile.ready` / `reconcile.check_again`  
Screen: Check Library  
English: Check Library / Ready to check cataloged media paths. / Check Again  
Context: Existing Check Library states/actions.

Key: `reconcile.running` / `reconcile.cancel` / `reconcile.cancelled`  
Screen: Check Library  
English: Checking library files and movie metadata... / Cancel Check / Check cancelled  
Context: Existing progress and cancellation states.

Key: `reconcile.issues_section` / `reconcile.results` / `reconcile.issue` / `reconcile.outcome`  
Screen: Check Library  
English: Issues to review / Results / Issue / Outcome  
Context: Existing result labels.

## Literal Accessible Names and Descriptions

Key: `accessibility.tmdb_rating_visual`  
Screen: Movie Card; Movie Details  
English: TMDB rating visual  
Context: Read-only star visual.

Key: `accessibility.operation_paths`  
Screen: Operations Log  
English: Operation source and destination paths  
Context: Bounded path row with full tooltip/details access.

Key: `accessibility.language_toggle`  
Screen: Settings  
English: Language: English or Arabic  
Context: Semantic two-state language control.

Key: `accessibility.watched_date_calendar`  
Screen: Movie Details  
English: Watched date calendar  
Context: Calendar action adjacent to the DatePicker.

## English Copy Review

- “Operations Log” is now used consistently for the navigation label, Settings entry point, page title, and report terminology.
- “History & Recovery” remains where the application intentionally groups read-only journal access with safety/recovery actions.
- “Clear Library Data” remains the authoritative action name because it describes indexed catalog data, while the Danger Zone explanation explicitly states that physical movie files are not deleted.
- “Mark Watched on Date” is retained for compatibility with the existing WatchEvent action; the adjacent “Watched date” label and native calendar affordance provide the clearer input context.
- Existing source copy outside these trivial consistency adjustments is inventory-only and was not silently rewritten.
