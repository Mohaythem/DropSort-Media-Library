from __future__ import annotations

from enum import StrEnum
import weakref

from PySide6.QtCore import QObject, Qt, Signal
from PySide6.QtWidgets import QApplication, QWidget

from dropsort.application.configuration.localization import UiLanguage


class TextId(StrEnum):
    WINDOW_TITLE = "window.title"
    BRAND_SUBTITLE = "brand.subtitle"
    NAV_LIBRARY = "nav.library"
    NAV_ADD_MOVIES = "nav.add_movies"
    NAV_HISTORY = "nav.history"
    NAV_SETTINGS = "nav.settings"
    NAV_PERSONAL_LIBRARY = "nav.personal_library"
    SETTINGS_TITLE = "settings.title"
    APPEARANCE = "settings.appearance"
    THEME = "settings.theme"
    THEME_MAIN = "settings.theme.main"
    THEME_DARK = "settings.theme.dark"
    THEME_SLATE = "settings.theme.slate"
    THEME_LIGHT = "settings.theme.light"
    # Compatibility aliases for callers from earlier V1 builds.
    THEME_DEEP_INK = "settings.theme.main"
    THEME_CHARCOAL = "settings.theme.dark"
    THEME_LIGHT_BLUE = "settings.theme.light"
    HISTORY_RECOVERY = "settings.history_recovery"
    VIEW_OPERATION_HISTORY = "settings.view_operation_history"
    LANGUAGE_TITLE = "settings.language.title"
    LANGUAGE_DESCRIPTION = "settings.language.description"
    LANGUAGE_ENGLISH = "settings.language.english"
    LANGUAGE_ARABIC = "settings.language.arabic"
    LANGUAGE_ACCESSIBLE = "settings.language.accessible"
    ACCESSIBILITY_TMDB_RATING_VISUAL = "accessibility.tmdb_rating_visual"
    ACCESSIBILITY_OPERATION_PATHS = "accessibility.operation_paths"
    ACCESSIBILITY_WATCHED_DATE_CALENDAR = "accessibility.watched_date_calendar"
    TMDB_METADATA = "settings.tmdb.title"
    TMDB_NOT_CONFIGURED = "settings.tmdb.not_configured"
    TMDB_ENVIRONMENT = "settings.tmdb.environment"
    TMDB_SESSION = "settings.tmdb.session"
    TMDB_SESSION_NOTICE = "settings.tmdb.session_notice"
    TMDB_TOKEN_PLACEHOLDER = "settings.tmdb.token_placeholder"
    TMDB_SAVE_SESSION = "settings.tmdb.save_session"
    TMDB_CLEAR_SESSION = "settings.tmdb.clear_session"
    TMDB_ENTER_TOKEN = "settings.tmdb.enter_token"
    TMDB_INVALID_TOKEN = "settings.tmdb.invalid_token"
    TMDB_READY = "settings.tmdb.ready"
    TMDB_CLEARED = "settings.tmdb.cleared"
    ABOUT_CREDITS = "settings.about"
    TMDB_NOTICE = "settings.tmdb.notice"
    TMDB_SOURCE_NOTICE = "settings.tmdb.source_notice"
    TMDB_RATING_LABEL = "tmdb.rating.label"
    TMDB_RATING_UNAVAILABLE = "tmdb.rating.unavailable"
    TMDB_INFOBAR_TITLE = "settings.tmdb.infobar.title"
    TMDB_INFOBAR_DESCRIPTION = "settings.tmdb.infobar.description"
    TMDB_SETUP_GUIDE = "settings.tmdb.setup_guide"
    TMDB_OPEN_OFFICIAL = "settings.tmdb.open_official"
    TMDB_SETUP_GUIDE_TITLE = "settings.tmdb.setup_guide.title"
    TMDB_SETUP_GUIDE_BODY = "settings.tmdb.setup_guide.body"
    LIBRARY_DATA = "settings.library_data"
    LIBRARY_DATA_NOTICE = "settings.library_data.notice"
    CLEAR_LIBRARY = "settings.clear_library"
    CLEAR_LIBRARY_TITLE = "settings.clear_library.title"
    CLEAR_LIBRARY_CONFIRM = "settings.clear_library.confirm"
    CLEAR_LIBRARY_RUNNING = "settings.clear_library.running"
    CLEAR_LIBRARY_RESULT = "settings.clear_library.result"
    CLEAR_LIBRARY_CACHE_WARNING = "settings.clear_library.cache_warning"
    LIBRARY_HEADING = "library.heading"
    LIBRARY_COUNT = "library.count"
    LIBRARY_COUNT_FILTERED = "library.count_filtered"
    CHECK_LIBRARY_FILES = "library.check_files"
    LIBRARY_SEARCH_PLACEHOLDER = "library.search.placeholder"
    LIBRARY_SEARCH_CLEAR = "library.search.clear"
    LIBRARY_SEARCH_NO_RESULTS = "library.search.no_results"
    LIBRARY_SEARCH_NO_RESULTS_HELPER = "library.search.no_results_helper"
    LIBRARY_SEARCH_SUGGESTION = "library.search.suggestion"
    LIBRARY_EMPTY = "library.empty"
    LIBRARY_EMPTY_HELPER = "library.empty_helper"
    LIBRARY_LOAD_ERROR = "library.load_error"
    MISSING_FILE = "library.missing_file"
    MISSING_FILES = "library.missing_files"
    FILE_SINGULAR = "library.file"
    FILE_PLURAL = "library.files"
    DETAILS_BACK = "details.back"
    DETAILS_OVERVIEW = "details.overview"
    DETAILS_MEDIA_FILES = "details.media_files"
    DETAILS_GENRES_UNAVAILABLE = "details.genres_unavailable"
    DETAILS_ORIGINAL_TITLE = "details.original_title"
    DETAILS_OVERVIEW_UNAVAILABLE = "details.overview_unavailable"
    DETAILS_NO_FILES = "details.no_files"
    DETAILS_REMOVED = "details.removed"
    DETAILS_YOUR_LIBRARY = "details.your_library"
    DETAILS_PREFERENCE_GROUP = "details.preference_group"
    DETAILS_WATCHLIST_GROUP = "details.watchlist_group"
    DETAILS_WATCHING_GROUP = "details.watching_group"
    DETAILS_LIKE = "details.like"
    DETAILS_BLACKLIST = "details.blacklist"
    DETAILS_CLEAR_PREFERENCE = "details.clear_preference"
    DETAILS_ADD_WATCHLIST = "details.add_watchlist"
    DETAILS_IN_WATCHLIST = "details.in_watchlist"
    DETAILS_MARK_WATCHED = "details.mark_watched"
    DETAILS_MARK_WATCHED_DATE = "details.mark_watched_date"
    DETAILS_WATCHED_COUNT = "details.watched_count"
    DETAILS_LAST_WATCHED = "details.last_watched"
    DETAILS_NOT_WATCHED = "details.not_watched"
    DETAILS_WATCH_HISTORY = "details.watch_history"
    DETAILS_FIRST_WATCH = "details.first_watch"
    DETAILS_REWATCH = "details.rewatch"
    DETAILS_REMOVE_WATCH_EVENT = "details.remove_watch_event"
    DETAILS_PERSONAL_LOAD_ERROR = "errors.personal_load"
    DETAILS_PERSONAL_SAVE_ERROR = "errors.personal_save"
    DETAILS_WATCH_DATE = "details.watch_date"
    DETAILS_PICK_DATE = "details.pick_date"
    DETAILS_WATCH_DATE_FUTURE = "details.watch_date_future"
    DETAILS_WATCH_SAVED = "details.watch_saved"
    PERSONAL_LIBRARY_HEADING = "personal.heading"
    PERSONAL_TAB_WATCHLIST = "personal.tab_watchlist"
    PERSONAL_TAB_READY = "personal.tab_ready"
    PERSONAL_TAB_LIKED = "personal.tab_liked"
    PERSONAL_TAB_BLACKLISTED = "personal.tab_blacklisted"
    PERSONAL_EMPTY_WATCHLIST = "personal.empty_watchlist"
    PERSONAL_EMPTY_WATCHLIST_DESCRIPTION = "personal.empty_watchlist_description"
    PERSONAL_EMPTY_READY = "personal.empty_ready"
    PERSONAL_EMPTY_READY_DESCRIPTION = "personal.empty_ready_description"
    PERSONAL_EMPTY_LIKED = "personal.empty_liked"
    PERSONAL_EMPTY_LIKED_DESCRIPTION = "personal.empty_liked_description"
    PERSONAL_EMPTY_BLACKLISTED = "personal.empty_blacklisted"
    PERSONAL_EMPTY_BLACKLISTED_DESCRIPTION = "personal.empty_blacklisted_description"
    PERSONAL_LOADING = "personal.loading"
    PERSONAL_LOAD_ERROR = "personal.load_error"
    PERSONAL_NO_LOCAL_COPY = "personal.no_local_copy"
    STATUS_PRESENT = "status.present"
    STATUS_MISSING = "status.missing"
    LAST_KNOWN_PATH = "details.last_known_path"
    CURRENT_PATH = "details.current_path"
    PLAY_MOVIE = "action.play_movie"
    OPEN_FOLDER = "action.open_folder"
    ORGANIZE_FILE = "action.organize_file"
    LOCATE_FILE = "action.locate_file"
    ADD_MOVIES_TITLE = "scan.title"
    ADD_MOVIES_GUIDANCE = "scan.guidance"
    ADD_MOVIES_FOLDER_LABEL = "scan.folder_label"
    ADD_MOVIES_DETECTED_HEADING = "scan.detected_heading"
    ADD_MOVIES_RESULTS_TITLE = "scan.results_title"
    ADD_MOVIES_RESULTS_YEAR = "scan.results_year"
    ADD_MOVIES_RESULTS_RESOLUTION = "scan.results_resolution"
    ADD_MOVIES_RESULTS_STATUS = "scan.results_status"
    ADD_MOVIES_RESULTS_ACTION = "scan.results_action"
    CHOOSE_FOLDER_SCAN = "scan.choose_folder"
    CHOOSE_MOVIE_FOLDER = "scan.folder_dialog"
    CANCEL_SCAN = "scan.cancel"
    INCLUDE_SUBFOLDERS = "scan.recursive"
    NO_FOLDER = "scan.no_folder"
    SCAN_READY = "scan.ready"
    SCANNING_MOVIES = "scan.scanning"
    CANCELLING_SCAN = "scan.cancelling"
    PREPARING_MATCHES = "scan.preparing_matches"
    BUILDING_RESULTS = "scan.building_results"
    INVALID_SCAN_RESULT = "scan.invalid_result"
    ADD_TO_LIBRARY = "scan.add_to_library"
    OPEN_SETTINGS = "scan.open_settings"
    EDIT_SEARCH = "scan.edit_search"
    SEARCH_MANUALLY = "scan.search_manually"
    SEARCH_TMDB = "scan.search_tmdb"
    DETECTED_TITLE = "scan.detected_title"
    SEARCH_AS = "scan.search_as"
    YEAR = "scan.year"
    NO_RESULTS = "scan.no_results"
    SELECT_THIS_MOVIE = "scan.select_this_movie"
    MANUAL_SEARCH_RESULTS = "scan.manual_search.results"
    MANUAL_SEARCH_SELECT = "scan.manual_search.choose"
    MANUAL_SEARCH_RATING = "scan.manual_search.rating"
    MANUAL_SEARCH_NO_OVERVIEW = "scan.manual_search.no_overview"
    MANUAL_SEARCH_SEARCHING = "scan.manual_search.searching"
    INVALID_YEAR = "scan.invalid_year"
    MANUAL_SEARCH_PROVIDER_FAILED = "scan.manual_search_provider_failed"
    IMPORT_MANUAL_SELECTED = "import.manual_selected"
    IMPORT_MANUAL_EXPLANATION = "import.manual_explanation"
    COPY = "common.copy"
    ADDED_TO_LIBRARY = "scan.added"
    DISMISS_PROPOSAL = "scan.dismiss"
    ALL_DONE = "scan.all_done"
    NO_MOVIES_WAITING = "scan.no_movies_waiting"
    ADDING_TO_LIBRARY = "scan.adding"
    SCAN_PROGRESS_DISCOVERY = "scan.progress.discovery"
    SCAN_PROGRESS_METADATA = "scan.progress.metadata"
    SCAN_PROGRESS_ROWS = "scan.progress.rows"
    SCAN_COMPLETE_EMPTY = "scan.complete.empty"
    SCAN_COMPLETE = "scan.complete"
    SCAN_CANCELLED = "scan.cancelled"
    SCAN_ROOT_MISSING = "scan.root_missing"
    SCAN_ROOT_NOT_FOLDER = "scan.root_not_folder"
    SCAN_ROOT_LINK = "scan.root_link"
    SCAN_PERMISSION = "scan.permission"
    SCAN_SAFE_ERROR = "scan.safe_error"
    SCAN_FAILED = "scan.failed"
    SCAN_TV_SKIPPED_SUFFIX = "scan.tv_skipped_suffix"
    SCAN_SUMMARY_COUNTS = "scan.summary_counts"
    IMPORT_MATCH_PROPOSED = "import.match_proposed"
    IMPORT_REVIEW_REQUIRED = "import.review_required"
    IMPORT_NO_MATCH = "import.no_match"
    IMPORT_METADATA_UNAVAILABLE = "import.metadata_unavailable"
    IMPORT_ALREADY_LIBRARY = "import.already_library"
    IMPORT_TV_SKIPPED = "import.tv_skipped"
    IMPORT_UNKNOWN = "import.unknown"
    IMPORT_SCAN_ERROR = "import.scan_error"
    IMPORT_TV_EXPLANATION = "import.tv_explanation"
    IMPORT_UNKNOWN_EXPLANATION = "import.unknown_explanation"
    IMPORT_DISCOVERY_EXPLANATION = "import.discovery_explanation"
    IMPORT_AUTH_EXPLANATION = "import.auth_explanation"
    IMPORT_RATE_EXPLANATION = "import.rate_explanation"
    IMPORT_RESPONSE_EXPLANATION = "import.response_explanation"
    IMPORT_NO_MATCH_EXPLANATION = "import.no_match_explanation"
    IMPORT_UNAVAILABLE_EXPLANATION = "import.unavailable_explanation"
    IMPORT_ALREADY_EXPLANATION = "import.already_explanation"
    IMPORT_REVIEW_EXPLANATION = "import.review_explanation"
    IMPORT_CONFIDENCE = "import.confidence"
    IMPORT_INVALID_CANDIDATE = "import.invalid_candidate"
    IMPORT_DETAILS_UNAVAILABLE = "import.details_unavailable"
    IMPORT_CATALOG_FAILED = "import.catalog_failed"
    IMPORT_FAILED = "import.failed"
    HISTORY_TITLE = "history.title"
    REFRESH = "common.refresh"
    HISTORY_GUIDANCE = "history.guidance"
    HISTORY_LOADING = "history.loading"
    HISTORY_EMPTY = "history.empty"
    HISTORY_READ_ERROR = "history.read_error"
    HISTORY_UNLINKED = "history.unlinked"
    HISTORY_REVERSE = "history.reverse"
    HISTORY_COPY = "history.copy"
    HISTORY_SAVE = "history.save"
    HISTORY_SELECT = "history.choose_operation"
    HISTORY_COPY_EMPTY = "history.copy_empty"
    HISTORY_COPY_SUCCESS = "history.copy_success"
    HISTORY_SAVE_SUCCESS = "history.save_success"
    HISTORY_SAVE_ERROR = "history.save_error"
    HISTORY_FROM = "history.from"
    HISTORY_TO = "history.to"
    HISTORY_OPERATION_ID = "history.operation_id"
    HISTORY_OPERATION_MOVE = "history.operation.move"
    HISTORY_OPERATION_RENAME = "history.operation.rename"
    HISTORY_STATUS_PLANNED = "history.status.planned"
    HISTORY_STATUS_VALIDATED = "history.status.validated"
    HISTORY_STATUS_IN_PROGRESS = "history.status.in_progress"
    HISTORY_STATUS_VERIFIED = "history.status.verified"
    HISTORY_STATUS_COMPLETED = "history.status.completed"
    HISTORY_STATUS_FAILED = "history.status.failed"
    HISTORY_STATUS_RECOVERY_REQUIRED = "history.status.recovery_required"
    DETAILS = "common.details"
    OPERATION_DETAILS = "history.details"
    HISTORY_READ_ONLY = "history.read_only"
    PREVIEW_UNDO = "history.preview_undo"
    INSPECT_RECOVERY = "history.inspect_recovery"
    ATTEMPT_RECOVERY = "history.attempt_recovery"
    CLOSE = "common.close"
    UNDO_PREVIEW = "history.undo_preview"
    UNDO_WARNING = "history.undo_warning"
    UNDO_NO_CHANGE = "history.undo_no_change"
    CANCEL = "common.cancel"
    CONFIRM_UNDO = "history.confirm_undo"
    UNDO_RUNNING = "history.undo_running"
    HISTORY_LOADING_DETAILS = "history.loading_details"
    HISTORY_INVALID = "history.invalid"
    HISTORY_INVALID_DETAILS = "history.invalid_details"
    UNDO_REVALIDATING = "history.undo_revalidating"
    UNDO_PREPARED = "history.undo_prepared"
    UNDO_INVALID = "history.undo_invalid"
    UNDO_VERIFY_FAILED = "history.undo_verify_failed"
    UNDO_PREPARE_FAILED = "history.undo_prepare_failed"
    UNDO_COMPLETED = "history.undo_completed"
    UNDO_RESULT_INVALID = "history.undo_result_invalid"
    UNDO_CATALOG_UPDATED = "history.undo_catalog_updated"
    UNDO_RECOVERY_REQUIRED = "history.undo_recovery_required"
    UNDO_FAILED = "history.undo_failed"
    UNDO_FAILED_GENERIC = "history.undo_failed_generic"
    RECOVERY_INVALID = "history.recovery_invalid"
    RECOVERY_INSPECT_FAILED = "history.recovery_inspect_failed"
    RECOVERY_COMPLETE = "history.recovery_complete"
    HISTORY_FIELD_STATE = "history.field.state"
    HISTORY_FIELD_OPERATION = "history.field.operation"
    HISTORY_FIELD_SOURCE = "history.field.source"
    HISTORY_FIELD_DESTINATION = "history.field.destination"
    HISTORY_FIELD_STRATEGY = "history.field.strategy"
    HISTORY_FIELD_CURRENT_PATH = "history.field.current_path"
    HISTORY_FIELD_CREATED = "history.field.created"
    HISTORY_NOT_RECORDED = "history.not_recorded"
    HISTORY_NOT_LINKED = "history.not_linked"
    HISTORY_FIELD_FILE_SIZE = "history.field.file_size"
    HISTORY_FIELD_TRANSFER = "history.field.transfer"
    DANGER_ZONE = "settings.danger_zone"
    CLEAR_LIBRARY_DESCRIPTION = "settings.clear_library.description"
    CHECK_FILES_TITLE = "reconcile.title"
    CHECK_FILES_READY = "reconcile.ready"
    CHECK_FILES_CANCEL = "reconcile.cancel"
    CHECK_FILES_RUNNING = "reconcile.running"
    CHECK_FILES_CANCELLING = "reconcile.cancelling"
    CHECK_FILES_CANCELLED = "reconcile.cancelled"
    CHECK_FILES_FAILED = "reconcile.failed"
    CHECK_FILES_PROGRESS = "reconcile.progress"
    CHECK_FILES_BACKGROUND = "reconcile.background"
    CHECK_FILES_ALREADY_RUNNING = "reconcile.already_running"
    CHECK_FILES_COMPLETE = "reconcile.complete"
    CHECK_FILES_DONE = "reconcile.done"
    CHECK_FILES_BACKGROUND_CANCELLED = "reconcile.background_cancelled"
    CHECK_FILES_BACKGROUND_FAILED = "reconcile.background_failed"
    CHECK_LIBRARY_PROGRESS = "reconcile.library_progress"
    CHECK_LIBRARY_COMPLETE = "reconcile.library_complete"
    CHECK_LIBRARY_COMPLETE_TITLE = "reconcile.library_complete_title"
    CHECK_LIBRARY_IDLE_DESCRIPTION = "reconcile.library_idle_description"
    CHECK_LIBRARY_RUNNING_STATUS = "reconcile.library_running_status"
    CHECK_LIBRARY_HEALTHY_TITLE = "reconcile.library_healthy_title"
    CHECK_LIBRARY_FAILURE_TITLE = "reconcile.library_failure_title"
    CHECK_LIBRARY_FAILURE_DESCRIPTION = "reconcile.library_failure_description"
    CHECK_LIBRARY_CANCELLED_DESCRIPTION = "reconcile.library_cancelled_description"
    CHECK_LIBRARY_TRY_AGAIN = "reconcile.library_try_again"
    CHECK_LIBRARY_FILES_CHECKED = "reconcile.library_files_checked"
    CHECK_LIBRARY_ALL_FILES_PRESENT = "reconcile.library_all_files_present"
    CHECK_LIBRARY_MISSING_FILES_SUMMARY = "reconcile.library_missing_files_summary"
    CHECK_LIBRARY_FILE_ERRORS_SUMMARY = "reconcile.library_file_errors_summary"
    CHECK_LIBRARY_METADATA_COMPLETE_SUMMARY = "reconcile.library_metadata_complete_summary"
    CHECK_LIBRARY_METADATA_ISSUES_SUMMARY = "reconcile.library_metadata_issues_summary"
    CHECK_LIBRARY_REPAIRED_SUMMARY = "reconcile.library_repaired_summary"
    CHECK_LIBRARY_NEEDS_ATTENTION_SUMMARY = "reconcile.library_needs_attention_summary"
    CHECK_LIBRARY_PROVIDER_UNAVAILABLE_SUMMARY = "reconcile.library_provider_unavailable_summary"
    CHECK_LIBRARY_ISSUES_SECTION = "reconcile.library_issues_section"
    CHECK_LIBRARY_AGAIN = "reconcile.library_again"
    CHECK_LIBRARY_FILES_SECTION = "reconcile.library_files_section"
    CHECK_LIBRARY_METADATA_SECTION = "reconcile.library_metadata_section"
    CHECK_LIBRARY_CHECKED = "reconcile.library_checked"
    CHECK_LIBRARY_PASSED = "reconcile.library_passed"
    CHECK_LIBRARY_PRESENT = "reconcile.library_present"
    CHECK_LIBRARY_MISSING = "reconcile.library_missing"
    CHECK_LIBRARY_ERRORS = "reconcile.library_errors"
    CHECK_LIBRARY_COMPLETE_COUNT = "reconcile.library_complete_count"
    CHECK_LIBRARY_ISSUES = "reconcile.library_issues"
    CHECK_LIBRARY_REPAIRED_COUNT = "reconcile.library_repaired_count"
    CHECK_LIBRARY_NEEDS_ATTENTION = "reconcile.library_needs_attention"
    CHECK_LIBRARY_PROVIDER_UNAVAILABLE_COUNT = "reconcile.library_provider_unavailable_count"
    CHECK_LIBRARY_RESULTS = "reconcile.library_results"
    CHECK_LIBRARY_ISSUE = "reconcile.library_issue"
    CHECK_LIBRARY_OUTCOME = "reconcile.library_outcome"
    CHECK_LIBRARY_NOT_REPAIRED = "reconcile.library_not_repaired"
    CHECK_LIBRARY_RESULT_PROVIDER_UNAVAILABLE = "reconcile.library_result_provider_unavailable"
    CHECK_LIBRARY_PROVIDER_SKIPPED = "reconcile.provider_skipped"
    CHECK_LIBRARY_NO_ISSUES = "reconcile.no_issues"
    CHECK_LIBRARY_ISSUE_OVERVIEW = "reconcile.issue_overview"
    CHECK_LIBRARY_ISSUE_RUNTIME = "reconcile.issue_runtime"
    CHECK_LIBRARY_ISSUE_GENRES = "reconcile.issue_genres"
    CHECK_LIBRARY_ISSUE_YEAR = "reconcile.issue_year"
    CHECK_LIBRARY_ISSUE_POSTER = "reconcile.issue_poster"
    CHECK_LIBRARY_ISSUE_NEEDS_MATCH = "reconcile.issue_needs_match"
    CHECK_LIBRARY_REPAIRED = "reconcile.repaired"
    CHECK_LIBRARY_NEEDS_REVIEW = "reconcile.needs_review"
    CHECK_LIBRARY_PROVIDER_UNAVAILABLE = "reconcile.provider_unavailable"
    CHECK_LIBRARY_AUTHENTICATION = "reconcile.authentication"
    CHECK_LIBRARY_RATE_LIMIT = "reconcile.rate_limit"
    CHECK_LIBRARY_INVALID_RESPONSE = "reconcile.invalid_response"
    RELINK_TITLE = "relink.title"
    RELINK_CHOOSE = "relink.choose"
    RELINK_CONFIRM = "relink.confirm"
    RELINK_VALIDATING = "relink.validating"
    RELINK_CONFIRMING = "relink.confirming"
    RELINK_VALID = "relink.valid"
    RELINK_COMPLETE = "relink.complete"
    RELINK_OLD_NEW = "relink.old_new"
    RELINK_PREVIEW = "relink.preview"
    RELINK_BLOCKED = "relink.blocked"
    RELINK_STALE = "relink.stale"
    RELINK_FAILED = "relink.failed"
    RELINK_FILE_DIALOG = "relink.file_dialog"
    VIDEO_FILES = "common.video_files"
    ORGANIZE_TITLE = "organize.title"
    ORGANIZE_GUIDANCE = "organize.guidance"
    CHOOSE_DESTINATION = "organize.choose_destination"
    CHOOSE_DESTINATION_DIALOG = "organize.destination_dialog"
    REFRESH_PREVIEW = "organize.refresh_preview"
    NOT_VALIDATED = "organize.not_validated"
    ORGANIZE_READY = "organize.ready"
    CONFIRM_MOVE_RENAME = "organize.confirm"
    ORGANIZE_VALIDATING = "organize.validating"
    ORGANIZE_RUNNING = "organize.running"
    ORGANIZE_VALID = "organize.valid"
    ORGANIZE_COMPLETE = "organize.complete"
    ORGANIZE_INVALID_PREVIEW = "organize.invalid_preview"
    ORGANIZE_SAME_DRIVE = "organize.same_drive"
    ORGANIZE_CROSS_DRIVE = "organize.cross_drive"
    ORGANIZE_CONFIRM_MOVE = "organize.confirm_move"
    ORGANIZE_CONFIRM_RENAME = "organize.confirm_rename"
    ORGANIZE_CONFIRM_MOVE_AND_RENAME = "organize.confirm_move_and_rename"
    ORGANIZE_FROM = "organize.from"
    ORGANIZE_TO = "organize.to"
    ORGANIZE_OPERATION = "organize.operation"
    ORGANIZE_FILE_SIZE = "organize.file_size"
    ORGANIZE_VOLUMES = "organize.volumes"
    ORGANIZE_TRANSFER = "organize.transfer"
    ORGANIZE_FILENAME_CHANGED = "organize.filename_changed"
    ORGANIZE_RESULT_INVALID = "organize.result_invalid"
    ORGANIZE_ERROR_DEST_EXISTS = "organize.error.destination_exists"
    ORGANIZE_ERROR_CASE_COLLISION = "organize.error.case_collision"
    ORGANIZE_ERROR_SAME_FILE = "organize.error.same_file"
    ORGANIZE_ERROR_SOURCE_MISSING = "organize.error.source_missing"
    ORGANIZE_ERROR_LINK = "organize.error.link"
    ORGANIZE_ERROR_UNSAFE = "organize.error.unsafe"
    ORGANIZE_ERROR_CATALOG = "organize.error.catalog"
    ORGANIZE_ERROR_VALIDATE = "organize.error.validate"
    ORGANIZE_ERROR_PREPARE = "organize.error.prepare"
    ORGANIZE_ERROR_STALE = "organize.error.stale"
    ORGANIZE_ERROR_RECOVERY = "organize.error.recovery"
    ORGANIZE_ERROR_EXECUTION = "organize.error.execution"
    ORGANIZE_ERROR_GENERIC = "organize.error.generic"
    MEDIA_MISSING_ACTION = "details.media_missing_action"
    PLAY_FAILED = "details.play_failed"
    OPEN_FOLDER_FAILED = "details.open_folder_failed"
    BUSY_CLEAR = "errors.busy_clear"
    CLEAR_UNAVAILABLE = "errors.clear_unavailable"
    CLEAR_BLOCKED = "errors.clear_blocked"
    CLEAR_DATABASE = "errors.clear_database"
    CLEAR_FAILED = "errors.clear_failed"
    DETAILS_LOAD_ERROR = "errors.details_load"


_ENGLISH: dict[TextId, str] = {
    TextId.WINDOW_TITLE: "DropSort Media Library",
    TextId.BRAND_SUBTITLE: "Local movie library",
    TextId.NAV_LIBRARY: "Library",
    TextId.NAV_ADD_MOVIES: "Add Movies",
    TextId.NAV_HISTORY: "Operations Log",
    TextId.NAV_SETTINGS: "Settings",
    TextId.NAV_PERSONAL_LIBRARY: "Personal Library",
    TextId.SETTINGS_TITLE: "Settings",
    TextId.APPEARANCE: "Appearance",
    TextId.THEME: "Theme",
    TextId.THEME_MAIN: "Main",
    TextId.THEME_DARK: "Dark",
    TextId.THEME_SLATE: "Slate",
    TextId.THEME_LIGHT: "Light",
    TextId.HISTORY_RECOVERY: "History & Recovery",
    TextId.VIEW_OPERATION_HISTORY: "View Operations Log",
    TextId.LANGUAGE_TITLE: "Language",
    TextId.LANGUAGE_DESCRIPTION: "Choose the language used by the DropSort interface.",
    TextId.LANGUAGE_ENGLISH: "English",
    TextId.LANGUAGE_ARABIC: "العربية",
    TextId.LANGUAGE_ACCESSIBLE: "Language: English or Arabic",
    TextId.ACCESSIBILITY_TMDB_RATING_VISUAL: "TMDB rating visual",
    TextId.ACCESSIBILITY_OPERATION_PATHS: "Operation source and destination paths",
    TextId.ACCESSIBILITY_WATCHED_DATE_CALENDAR: "Watched date calendar",
    TextId.TMDB_METADATA: "TMDB Metadata",
    TextId.TMDB_NOT_CONFIGURED: "Not configured",
    TextId.TMDB_ENVIRONMENT: "Configured from environment",
    TextId.TMDB_SESSION: "Configured for this session",
    TextId.TMDB_SESSION_NOTICE: "A token entered here is used only for this application session and is not permanently stored.",
    TextId.TMDB_TOKEN_PLACEHOLDER: "TMDB Read Access Token",
    TextId.TMDB_SAVE_SESSION: "Save for Session",
    TextId.TMDB_CLEAR_SESSION: "Clear Session Token",
    TextId.TMDB_ENTER_TOKEN: "Enter a TMDB Read Access Token for this session.",
    TextId.TMDB_INVALID_TOKEN: "Enter a valid TMDB Read Access Token.",
    TextId.TMDB_READY: "TMDB metadata is ready for this session.",
    TextId.TMDB_CLEARED: "The session token was cleared.",
    TextId.ABOUT_CREDITS: "About & Credits",
    TextId.TMDB_NOTICE: "This product uses the TMDB API but is not endorsed or certified by TMDB.",
    TextId.TMDB_SOURCE_NOTICE: "Movie metadata and poster images are provided by TMDB.",
    TextId.TMDB_RATING_LABEL: "TMDB",
    TextId.TMDB_RATING_UNAVAILABLE: "TMDB rating unavailable",
    TextId.TMDB_INFOBAR_TITLE: "TMDB setup",
    TextId.TMDB_INFOBAR_DESCRIPTION: "DropSort uses TMDB for movie metadata and posters. Core setup instructions are available offline.",
    TextId.TMDB_SETUP_GUIDE: "Setup Guide",
    TextId.TMDB_OPEN_OFFICIAL: "Open TMDB",
    TextId.TMDB_SETUP_GUIDE_TITLE: "TMDB setup guide",
    TextId.TMDB_SETUP_GUIDE_BODY: "TMDB provides movie metadata and poster references. DropSort uses a TMDB Read Access Token for optional metadata and poster recovery. Create or retrieve the token from your TMDB account, paste it into the secure session field in Settings, and use the status message to confirm it. Without a token, local library browsing and local metadata checks still work. Never share the token in screenshots, logs, exports, or messages.",
    TextId.LIBRARY_DATA: "Library Data",
    TextId.LIBRARY_DATA_NOTICE: "Forget cataloged movies and cached metadata. Physical movie files are never changed.",
    TextId.DANGER_ZONE: "Danger Zone",
    TextId.CLEAR_LIBRARY_DESCRIPTION: "Remove indexed movie and file data from DropSort. Your actual movie files will not be deleted.",
    TextId.CLEAR_LIBRARY: "Clear Library Data",
    TextId.CLEAR_LIBRARY_TITLE: "Clear Library Data?",
    TextId.CLEAR_LIBRARY_CONFIRM: "This clears local media-file links, metadata cache, and poster cache. Movies with personal data are retained; movies without retained personal data are forgotten. DropSort will NOT delete, move, rename, copy, or modify your physical movie files. Operation history and recovery records are preserved. Continue?",
    TextId.CLEAR_LIBRARY_RUNNING: "Clearing local library data...",
    TextId.CLEAR_LIBRARY_RESULT: "Library cleared: {movies} movies and {files} media-file links removed.",
    TextId.CLEAR_LIBRARY_CACHE_WARNING: " Poster cache cleanup could not be completed; media files were unaffected.",
    TextId.LIBRARY_HEADING: "Library",
    TextId.LIBRARY_COUNT: "{count} movies",
    TextId.LIBRARY_COUNT_FILTERED: '{count} movies matching "{query}"',
    TextId.CHECK_LIBRARY_FILES: "Check Library",
    TextId.LIBRARY_SEARCH_PLACEHOLDER: "Search your library...",
    TextId.LIBRARY_SEARCH_CLEAR: "Clear library search",
    TextId.LIBRARY_SEARCH_NO_RESULTS: "No movies found",
    TextId.LIBRARY_SEARCH_NO_RESULTS_HELPER: "Try another title or clear the current search.",
    TextId.LIBRARY_SEARCH_SUGGESTION: "Local movie suggestion",
    TextId.LIBRARY_EMPTY: "Your movie library is empty.",
    TextId.LIBRARY_EMPTY_HELPER: "Add a local folder to start building your library.",
    TextId.LIBRARY_LOAD_ERROR: "DropSort could not load the local library. Please try again.",
    TextId.MISSING_FILE: "Missing file",
    TextId.MISSING_FILES: "{count} missing files",
    TextId.FILE_SINGULAR: "file",
    TextId.FILE_PLURAL: "files",
    TextId.DETAILS_BACK: "Back",
    TextId.DETAILS_OVERVIEW: "Overview",
    TextId.DETAILS_MEDIA_FILES: "Media Files",
    TextId.DETAILS_GENRES_UNAVAILABLE: "Genres unavailable",
    TextId.DETAILS_ORIGINAL_TITLE: "Original title: {title}",
    TextId.DETAILS_OVERVIEW_UNAVAILABLE: "Overview unavailable.",
    TextId.DETAILS_NO_FILES: "No physical media files are linked.",
    TextId.DETAILS_REMOVED: "This movie is no longer in the local library.",
    TextId.DETAILS_YOUR_LIBRARY: "Your Library",
    TextId.DETAILS_PREFERENCE_GROUP: "Preference",
    TextId.DETAILS_WATCHLIST_GROUP: "Watchlist",
    TextId.DETAILS_WATCHING_GROUP: "Watching",
    TextId.DETAILS_LIKE: "Like",
    TextId.DETAILS_BLACKLIST: "Blacklist",
    TextId.DETAILS_CLEAR_PREFERENCE: "Clear preference",
    TextId.DETAILS_ADD_WATCHLIST: "Add to Watchlist",
    TextId.DETAILS_IN_WATCHLIST: "In Watchlist",
    TextId.DETAILS_MARK_WATCHED: "Mark Watched",
    TextId.DETAILS_MARK_WATCHED_DATE: "Mark Watched on Date",
    TextId.DETAILS_WATCHED_COUNT: "Watched {count} time(s)",
    TextId.DETAILS_LAST_WATCHED: "Last watched: {date}",
    TextId.DETAILS_NOT_WATCHED: "Not watched yet",
    TextId.DETAILS_WATCH_HISTORY: "Watch History",
    TextId.DETAILS_FIRST_WATCH: "First watch",
    TextId.DETAILS_REWATCH: "Rewatch",
    TextId.DETAILS_REMOVE_WATCH_EVENT: "Remove",
    TextId.DETAILS_PERSONAL_LOAD_ERROR: "Personal information could not be loaded. Please try again.",
    TextId.DETAILS_PERSONAL_SAVE_ERROR: "Personal change could not be saved. Your current state was restored.",
    TextId.DETAILS_WATCH_DATE: "Watched date",
    TextId.DETAILS_PICK_DATE: "Pick a date",
    TextId.DETAILS_WATCH_DATE_FUTURE: "Choose today or an earlier date.",
    TextId.DETAILS_WATCH_SAVED: "Watched entry saved.",
    TextId.PERSONAL_LIBRARY_HEADING: "Personal Library",
    TextId.PERSONAL_TAB_WATCHLIST: "Watchlist",
    TextId.PERSONAL_TAB_READY: "Ready to Watch",
    TextId.PERSONAL_TAB_LIKED: "Liked",
    TextId.PERSONAL_TAB_BLACKLISTED: "Blacklisted",
    TextId.PERSONAL_EMPTY_WATCHLIST: "Your watchlist is empty.",
    TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION: "Movies you save for later will appear here.",
    TextId.PERSONAL_EMPTY_READY: "Nothing is ready to watch yet.",
    TextId.PERSONAL_EMPTY_READY_DESCRIPTION: "Watchlisted movies with a local copy will appear here.",
    TextId.PERSONAL_EMPTY_LIKED: "You have not liked any movies yet.",
    TextId.PERSONAL_EMPTY_LIKED_DESCRIPTION: "Movies you like will appear here.",
    TextId.PERSONAL_EMPTY_BLACKLISTED: "Your blacklist is empty.",
    TextId.PERSONAL_EMPTY_BLACKLISTED_DESCRIPTION: "Movies you blacklist will appear here.",
    TextId.PERSONAL_LOADING: "Loading Personal Library...",
    TextId.PERSONAL_LOAD_ERROR: "Personal Library could not be loaded. Please try again.",
    TextId.PERSONAL_NO_LOCAL_COPY: "No local copy",
    TextId.STATUS_PRESENT: "Present",
    TextId.STATUS_MISSING: "Missing",
    TextId.LAST_KNOWN_PATH: "Last known location: ",
    TextId.CURRENT_PATH: "Path: ",
    TextId.PLAY_MOVIE: "Play Movie",
    TextId.OPEN_FOLDER: "Open Folder",
    TextId.ORGANIZE_FILE: "Organize File",
    TextId.LOCATE_FILE: "Locate File",
    TextId.ADD_MOVIES_TITLE: "Add Movies",
    TextId.ADD_MOVIES_GUIDANCE: "Select a local folder to scan. DropSort identifies movie candidates and keeps every import explicit.",
    TextId.ADD_MOVIES_FOLDER_LABEL: "Folder",
    TextId.ADD_MOVIES_DETECTED_HEADING: "Detected Movies",
    TextId.ADD_MOVIES_RESULTS_TITLE: "Title",
    TextId.ADD_MOVIES_RESULTS_YEAR: "Year",
    TextId.ADD_MOVIES_RESULTS_RESOLUTION: "Resolution",
    TextId.ADD_MOVIES_RESULTS_STATUS: "Status",
    TextId.ADD_MOVIES_RESULTS_ACTION: "Action",
    TextId.CHOOSE_FOLDER_SCAN: "Choose Folder and Scan",
    TextId.CHOOSE_MOVIE_FOLDER: "Choose a movie folder",
    TextId.CANCEL_SCAN: "Cancel Scan",
    TextId.INCLUDE_SUBFOLDERS: "Include subfolders",
    TextId.NO_FOLDER: "No folder selected",
    TextId.SCAN_READY: "Choose a folder to begin a read-only scan.",
    TextId.SCANNING_MOVIES: "Scanning movies...",
    TextId.CANCELLING_SCAN: "Cancelling scan...",
    TextId.PREPARING_MATCHES: "Preparing metadata matches...",
    TextId.BUILDING_RESULTS: "Building review results...",
    TextId.INVALID_SCAN_RESULT: "DropSort received an invalid scan result. Please try again.",
    TextId.ADD_TO_LIBRARY: "Add to Library",
    TextId.OPEN_SETTINGS: "Open Settings",
    TextId.EDIT_SEARCH: "Edit Search",
    TextId.SEARCH_MANUALLY: "Search Manually",
    TextId.SEARCH_TMDB: "Search TMDB",
    TextId.DETECTED_TITLE: "Detected title",
    TextId.SEARCH_AS: "Search as",
    TextId.YEAR: "Year (optional)",
    TextId.NO_RESULTS: "No results found.",
    TextId.SELECT_THIS_MOVIE: "Se" + "lect This Movie",
    TextId.MANUAL_SEARCH_RESULTS: "Results",
    TextId.MANUAL_SEARCH_SELECT: "Se" + "lect",
    TextId.MANUAL_SEARCH_RATING: "Rating",
    TextId.MANUAL_SEARCH_NO_OVERVIEW: "No overview available.",
    TextId.MANUAL_SEARCH_SEARCHING: "Searching TMDB...",
    TextId.INVALID_YEAR: "Enter a valid four-digit year or leave the year blank.",
    TextId.MANUAL_SEARCH_PROVIDER_FAILED: "TMDB search is unavailable right now. Try again later.",
    TextId.IMPORT_MANUAL_SELECTED: "Manual candidate selected",
    TextId.IMPORT_MANUAL_EXPLANATION: "Review the selected candidate, then explicitly add it to the local library.",
    TextId.COPY: "Copy",
    TextId.ADDED_TO_LIBRARY: "Added to library",
    TextId.DISMISS_PROPOSAL: "Dismiss",
    TextId.ALL_DONE: "All done",
    TextId.NO_MOVIES_WAITING: "No movies are waiting for review.",
    TextId.ADDING_TO_LIBRARY: "Adding to library…",
    TextId.SCAN_PROGRESS_DISCOVERY: "Folders: {folders} · Files inspected: {files} · Media candidates: {movies}",
    TextId.SCAN_PROGRESS_METADATA: "Preparing metadata proposals {done} / {total}",
    TextId.SCAN_PROGRESS_ROWS: "Preparing review rows...",
    TextId.SCAN_COMPLETE_EMPTY: "Scan complete. No movie candidates found in this folder.",
    TextId.SCAN_COMPLETE: "Scan complete. Files inspected: {files}. Ready to add/review: {ready}. Already in library: {existing}. Errors: {errors}.",
    TextId.SCAN_CANCELLED: "Scan cancelled. Files inspected before cancellation: {files}. Partial results were discarded. No files were changed.",
    TextId.SCAN_ROOT_MISSING: "The selected folder no longer exists.",
    TextId.SCAN_ROOT_NOT_FOLDER: "The selected path is not a folder.",
    TextId.SCAN_ROOT_LINK: "Linked or reparse-point scan roots are not allowed.",
    TextId.SCAN_PERMISSION: "DropSort does not have permission to read the selected folder.",
    TextId.SCAN_SAFE_ERROR: "DropSort could not safely inspect the selected folder.",
    TextId.SCAN_FAILED: "DropSort could not scan the selected folder. Please try again.",
    TextId.SCAN_TV_SKIPPED_SUFFIX: " {count} TV episodes were skipped.",
    TextId.SCAN_SUMMARY_COUNTS: "Movie candidates: {movies} · TV skipped: {tv} · Unknown media: {unknown}",
    TextId.IMPORT_MATCH_PROPOSED: "Match proposed",
    TextId.IMPORT_REVIEW_REQUIRED: "Review required",
    TextId.IMPORT_NO_MATCH: "No match",
    TextId.IMPORT_METADATA_UNAVAILABLE: "Metadata unavailable",
    TextId.IMPORT_ALREADY_LIBRARY: "Already in library",
    TextId.IMPORT_TV_SKIPPED: "TV episode skipped",
    TextId.IMPORT_UNKNOWN: "Unknown media",
    TextId.IMPORT_SCAN_ERROR: "Scan error",
    TextId.IMPORT_TV_EXPLANATION: "A TV episode pattern was detected. TV imports are not supported yet.",
    TextId.IMPORT_UNKNOWN_EXPLANATION: "This supported video filename could not be classified as a movie.",
    TextId.IMPORT_DISCOVERY_EXPLANATION: "DropSort could not safely read this filesystem entry. It was skipped.",
    TextId.IMPORT_AUTH_EXPLANATION: "TMDB is not configured, or the current credential was rejected. Add a TMDB Read Access Token in Settings to enable movie metadata and matching.",
    TextId.IMPORT_RATE_EXPLANATION: "TMDB is rate limiting requests. Wait briefly, then scan again.",
    TextId.IMPORT_RESPONSE_EXPLANATION: "TMDB returned an invalid response. No import was attempted.",
    TextId.IMPORT_NO_MATCH_EXPLANATION: "No sufficiently reliable movie candidate was found.",
    TextId.IMPORT_UNAVAILABLE_EXPLANATION: "Movie metadata could not be obtained. No import was attempted.",
    TextId.IMPORT_ALREADY_EXPLANATION: "This physical path is already cataloged.",
    TextId.IMPORT_REVIEW_EXPLANATION: "Review this discovery before importing.",
    TextId.IMPORT_CONFIDENCE: "Confidence {confidence}% · {reasons}",
    TextId.IMPORT_INVALID_CANDIDATE: "The selected candidate is invalid. Please rescan.",
    TextId.IMPORT_DETAILS_UNAVAILABLE: "Movie details are unavailable. Please try again later.",
    TextId.IMPORT_CATALOG_FAILED: "Could not add this movie to the library. Please try again.",
    TextId.IMPORT_FAILED: "DropSort could not complete this import. Please try again.",
    TextId.HISTORY_TITLE: "Operations Log",
    TextId.REFRESH: "Refresh",
    TextId.HISTORY_GUIDANCE: "Recent file operations performed by DropSort.",
    TextId.HISTORY_LOADING: "Loading local operation history...",
    TextId.HISTORY_EMPTY: "No file operations yet.\n\nFile moves and other managed operations will appear here.",
    TextId.HISTORY_READ_ERROR: "DropSort could not read operation history. Please try again.",
    TextId.HISTORY_UNLINKED: "Unlinked media operation",
    TextId.HISTORY_REVERSE: "Reverse operation",
    TextId.HISTORY_COPY: "Copy",
    TextId.HISTORY_SAVE: "Save",
    TextId.HISTORY_SELECT: "Se" + "lect operation",
    TextId.HISTORY_COPY_EMPTY: "Se" + "lect an operation to copy.",
    TextId.HISTORY_COPY_SUCCESS: "Operations log copied to clipboard.",
    TextId.HISTORY_SAVE_SUCCESS: "Operations log saved.",
    TextId.HISTORY_SAVE_ERROR: "The operations log could not be saved.",
    TextId.HISTORY_FROM: "From",
    TextId.HISTORY_TO: "To",
    TextId.HISTORY_OPERATION_ID: "Operation ID",
    TextId.HISTORY_OPERATION_MOVE: "Move",
    TextId.HISTORY_OPERATION_RENAME: "Rename",
    TextId.HISTORY_STATUS_PLANNED: "Planned",
    TextId.HISTORY_STATUS_VALIDATED: "Validated",
    TextId.HISTORY_STATUS_IN_PROGRESS: "In progress",
    TextId.HISTORY_STATUS_VERIFIED: "Verified",
    TextId.HISTORY_STATUS_COMPLETED: "Completed",
    TextId.HISTORY_STATUS_FAILED: "Failed",
    TextId.HISTORY_STATUS_RECOVERY_REQUIRED: "Recovery required",
    TextId.DETAILS: "Details",
    TextId.OPERATION_DETAILS: "Operation Details",
    TextId.HISTORY_READ_ONLY: "History details are read-only.",
    TextId.PREVIEW_UNDO: "Preview Undo",
    TextId.INSPECT_RECOVERY: "Inspect Recovery",
    TextId.ATTEMPT_RECOVERY: "Attempt Safe Recovery",
    TextId.CLOSE: "Close",
    TextId.UNDO_PREVIEW: "Undo Preview",
    TextId.UNDO_WARNING: "Undo creates a new reverse journal operation. The original history record remains unchanged.",
    TextId.UNDO_NO_CHANGE: "No journal or filesystem change has occurred.",
    TextId.CANCEL: "Cancel",
    TextId.CONFIRM_UNDO: "Confirm Undo",
    TextId.UNDO_RUNNING: "The safe reverse operation is running...",
    TextId.HISTORY_LOADING_DETAILS: "Loading operation details...",
    TextId.HISTORY_INVALID: "DropSort received invalid operation history data.",
    TextId.HISTORY_INVALID_DETAILS: "DropSort received invalid operation details.",
    TextId.UNDO_REVALIDATING: "Revalidating the current file and exact historical paths...",
    TextId.UNDO_PREPARED: "Undo preview prepared. No filesystem change has occurred.",
    TextId.UNDO_INVALID: "DropSort received an invalid undo preview.",
    TextId.UNDO_VERIFY_FAILED: "DropSort could not verify undo eligibility. Please refresh history.",
    TextId.UNDO_PREPARE_FAILED: "DropSort could not prepare an undo preview.",
    TextId.UNDO_COMPLETED: "Undo completed through a new committed reverse operation.",
    TextId.UNDO_RESULT_INVALID: "The reverse operation returned an invalid result; recovery may be required.",
    TextId.UNDO_CATALOG_UPDATED: "Undo completed and the catalog path was updated.",
    TextId.UNDO_RECOVERY_REQUIRED: "The reverse operation requires recovery. Both files are preserved when ambiguous.",
    TextId.UNDO_FAILED: "DropSort could not safely complete this undo operation.",
    TextId.UNDO_FAILED_GENERIC: "DropSort could not complete this undo operation.",
    TextId.RECOVERY_INVALID: "DropSort received an invalid recovery assessment.",
    TextId.RECOVERY_INSPECT_FAILED: "DropSort could not inspect this recovery state safely.",
    TextId.RECOVERY_COMPLETE: "Recovery completed with journal state {state}.",
    TextId.HISTORY_FIELD_STATE: "State",
    TextId.HISTORY_FIELD_OPERATION: "Operation",
    TextId.HISTORY_FIELD_SOURCE: "Source",
    TextId.HISTORY_FIELD_DESTINATION: "Destination",
    TextId.HISTORY_FIELD_STRATEGY: "Strategy",
    TextId.HISTORY_FIELD_CURRENT_PATH: "Current catalog path",
    TextId.HISTORY_FIELD_CREATED: "Created",
    TextId.HISTORY_NOT_RECORDED: "Not recorded",
    TextId.HISTORY_NOT_LINKED: "Not linked",
    TextId.HISTORY_FIELD_FILE_SIZE: "File size",
    TextId.HISTORY_FIELD_TRANSFER: "Transfer",
    TextId.CHECK_FILES_TITLE: "Check Library",
    TextId.CHECK_FILES_READY: "Ready to check cataloged media paths.",
    TextId.CHECK_FILES_CANCEL: "Cancel Check",
    TextId.CHECK_FILES_RUNNING: "Checking library files...",
    TextId.CHECK_FILES_CANCELLING: "Cancelling library check...",
    TextId.CHECK_FILES_CANCELLED: "Check cancelled",
    TextId.CHECK_FILES_FAILED: "DropSort could not complete the library check.",
    TextId.CHECK_FILES_PROGRESS: "{prefix}: {checked} / {total} | Present: {present} | Missing: {missing} | Errors: {errors}",
    TextId.CHECK_FILES_BACKGROUND: "Checking cataloged media locations in the background...",
    TextId.CHECK_FILES_ALREADY_RUNNING: "A library file check is already running in the background.",
    TextId.CHECK_FILES_COMPLETE: "Library file check complete: Present: {present} | Missing: {missing} | Errors: {errors}",
    TextId.CHECK_FILES_DONE: "Done",
    TextId.CHECK_FILES_BACKGROUND_CANCELLED: "Background library file check was cancelled.",
    TextId.CHECK_FILES_BACKGROUND_FAILED: "DropSort could not complete the background library file check.",
    TextId.CHECK_LIBRARY_PROGRESS: "{prefix}\n\nFiles: {file_checked} / {file_total} checked | {present} present | {missing} missing | {errors} errors\nMetadata: {metadata_checked} / {metadata_total} checked | {complete} complete | {issues} issues | {repaired} repaired",
    TextId.CHECK_LIBRARY_COMPLETE: "Library check complete\n\nFiles\nChecked: {file_checked}\nPresent: {present}\nMissing: {missing}\nErrors: {errors}\n\nMetadata\nComplete: {complete}\nIssues found: {issues}\nRepaired: {repaired}\nNeeds review: {needs_review}\nProvider unavailable: {provider_unavailable}",
    TextId.CHECK_LIBRARY_COMPLETE_TITLE: "Library check complete",
    TextId.CHECK_LIBRARY_IDLE_DESCRIPTION: "Check your local files and movie metadata.\nMissing files and incomplete metadata will be reported.\nRepairable missing metadata can be restored safely.",
    TextId.CHECK_LIBRARY_RUNNING_STATUS: "Checking library files and movie metadata...",
    TextId.CHECK_LIBRARY_HEALTHY_TITLE: "Library looks good",
    TextId.CHECK_LIBRARY_FAILURE_TITLE: "Library check could not finish",
    TextId.CHECK_LIBRARY_FAILURE_DESCRIPTION: "Your library was not modified. Technical details are available in the application log.",
    TextId.CHECK_LIBRARY_CANCELLED_DESCRIPTION: "The check was cancelled. Your library was not modified.",
    TextId.CHECK_LIBRARY_TRY_AGAIN: "Try Again",
    TextId.CHECK_LIBRARY_FILES_CHECKED: "{count} files checked",
    TextId.CHECK_LIBRARY_ALL_FILES_PRESENT: "All cataloged files are present.",
    TextId.CHECK_LIBRARY_MISSING_FILES_SUMMARY: "{count} missing files.",
    TextId.CHECK_LIBRARY_FILE_ERRORS_SUMMARY: "{count} file errors.",
    TextId.CHECK_LIBRARY_METADATA_COMPLETE_SUMMARY: "Metadata is complete.",
    TextId.CHECK_LIBRARY_METADATA_ISSUES_SUMMARY: "{count} metadata issues found.",
    TextId.CHECK_LIBRARY_REPAIRED_SUMMARY: "{count} repaired.",
    TextId.CHECK_LIBRARY_NEEDS_ATTENTION_SUMMARY: "{count} still needs attention.",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE_SUMMARY: "{count} provider results unavailable.",
    TextId.CHECK_LIBRARY_ISSUES_SECTION: "Issues to review",
    TextId.CHECK_LIBRARY_AGAIN: "Check Again",
    TextId.CHECK_LIBRARY_FILES_SECTION: "Files",
    TextId.CHECK_LIBRARY_METADATA_SECTION: "Metadata",
    TextId.CHECK_LIBRARY_CHECKED: "Checked",
    TextId.CHECK_LIBRARY_PASSED: "Passed",
    TextId.CHECK_LIBRARY_PRESENT: "Present",
    TextId.CHECK_LIBRARY_MISSING: "Missing",
    TextId.CHECK_LIBRARY_ERRORS: "Errors",
    TextId.CHECK_LIBRARY_COMPLETE_COUNT: "Complete",
    TextId.CHECK_LIBRARY_ISSUES: "Issues found",
    TextId.CHECK_LIBRARY_REPAIRED_COUNT: "Repaired",
    TextId.CHECK_LIBRARY_NEEDS_ATTENTION: "Needs attention",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE_COUNT: "Provider unavailable",
    TextId.CHECK_LIBRARY_RESULTS: "Results",
    TextId.CHECK_LIBRARY_ISSUE: "Issue",
    TextId.CHECK_LIBRARY_OUTCOME: "Outcome",
    TextId.CHECK_LIBRARY_NOT_REPAIRED: "Not repaired",
    TextId.CHECK_LIBRARY_RESULT_PROVIDER_UNAVAILABLE: "Provider unavailable",
    TextId.CHECK_LIBRARY_PROVIDER_SKIPPED: "Metadata repair skipped: TMDB is not configured.",
    TextId.CHECK_LIBRARY_NO_ISSUES: "No metadata issues found.",
    TextId.CHECK_LIBRARY_ISSUE_OVERVIEW: "Missing overview",
    TextId.CHECK_LIBRARY_ISSUE_RUNTIME: "Missing runtime",
    TextId.CHECK_LIBRARY_ISSUE_GENRES: "Missing genres",
    TextId.CHECK_LIBRARY_ISSUE_YEAR: "Missing year",
    TextId.CHECK_LIBRARY_ISSUE_POSTER: "Missing poster",
    TextId.CHECK_LIBRARY_ISSUE_NEEDS_MATCH: "Needs a provider match",
    TextId.CHECK_LIBRARY_REPAIRED: "Repaired: {fields}",
    TextId.CHECK_LIBRARY_NEEDS_REVIEW: "Needs review",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE: "Provider unavailable: {reason}",
    TextId.CHECK_LIBRARY_AUTHENTICATION: "TMDB is not configured or authentication failed",
    TextId.CHECK_LIBRARY_RATE_LIMIT: "TMDB rate limit reached",
    TextId.CHECK_LIBRARY_INVALID_RESPONSE: "TMDB returned an invalid response",
    TextId.RELINK_TITLE: "Relink Media File",
    TextId.RELINK_CHOOSE: "Choose the replacement media file.",
    TextId.RELINK_CONFIRM: "Confirm Relink",
    TextId.RELINK_VALIDATING: "Validating selected file...",
    TextId.RELINK_CONFIRMING: "Confirming catalog relink...",
    TextId.RELINK_VALID: "Candidate validated. Confirm Relink to correct the catalog only.",
    TextId.RELINK_COMPLETE: "Relink completed. The physical file was not moved.",
    TextId.RELINK_OLD_NEW: "OLD / LAST KNOWN PATH\n{old}\n\nNEW SELECTED PATH\n{new}",
    TextId.RELINK_PREVIEW: "{paths}\n\nSize: {size} bytes\nValidation: Candidate available; no catalog conflict",
    TextId.RELINK_BLOCKED: "Relink blocked: {reason}.",
    TextId.RELINK_STALE: "Relink is no longer valid. Choose the file again.",
    TextId.RELINK_FAILED: "DropSort could not complete the relink.",
    TextId.RELINK_FILE_DIALOG: "Locate media file",
    TextId.VIDEO_FILES: "Video files",
    TextId.ORGANIZE_TITLE: "Organize File",
    TextId.ORGANIZE_GUIDANCE: "Choose one destination, review the exact paths, then explicitly confirm the safe journaled operation.",
    TextId.CHOOSE_DESTINATION: "Choose Destination Folder",
    TextId.CHOOSE_DESTINATION_DIALOG: "Choose the destination folder",
    TextId.REFRESH_PREVIEW: "Refresh Preview",
    TextId.NOT_VALIDATED: "Not validated",
    TextId.ORGANIZE_READY: "Choose a destination to validate this operation.",
    TextId.CONFIRM_MOVE_RENAME: "Confirm Move / Rename",
    TextId.ORGANIZE_VALIDATING: "Validating the exact source and destination…",
    TextId.ORGANIZE_RUNNING: "The verified file operation is running. DropSort will finish safely.",
    TextId.ORGANIZE_VALID: "Source verified. Destination available. No collision found.",
    TextId.ORGANIZE_COMPLETE: "File organization completed and the catalog path was updated.",
    TextId.ORGANIZE_INVALID_PREVIEW: "DropSort received an invalid preview. Please try again.",
    TextId.ORGANIZE_SAME_DRIVE: "Same-drive move: DropSort will use the established safe local transfer.",
    TextId.ORGANIZE_CROSS_DRIVE: "Cross-drive move: DropSort will copy, flush, hash-verify, and only then remove the original.",
    TextId.ORGANIZE_CONFIRM_MOVE: "Move File",
    TextId.ORGANIZE_CONFIRM_RENAME: "Rename File",
    TextId.ORGANIZE_CONFIRM_MOVE_AND_RENAME: "Move & Rename File",
    TextId.ORGANIZE_FROM: "FROM",
    TextId.ORGANIZE_TO: "TO",
    TextId.ORGANIZE_OPERATION: "Operation",
    TextId.ORGANIZE_FILE_SIZE: "File size",
    TextId.ORGANIZE_VOLUMES: "Volumes",
    TextId.ORGANIZE_TRANSFER: "Transfer",
    TextId.ORGANIZE_FILENAME_CHANGED: "The destination filename changed. Refresh the preview before confirming.",
    TextId.ORGANIZE_RESULT_INVALID: "The operation finished with an invalid result. Recovery may be required.",
    TextId.ORGANIZE_ERROR_DEST_EXISTS: "Destination already exists. Choose another folder or filename; DropSort never overwrites.",
    TextId.ORGANIZE_ERROR_CASE_COLLISION: "Another file uses this destination with different letter casing. Choose another destination.",
    TextId.ORGANIZE_ERROR_SAME_FILE: "The source and destination identify the same file. Choose a different destination.",
    TextId.ORGANIZE_ERROR_SOURCE_MISSING: "The cataloged source file is no longer available. Nothing was changed.",
    TextId.ORGANIZE_ERROR_LINK: "Linked or reparse-point paths are not allowed for organization.",
    TextId.ORGANIZE_ERROR_UNSAFE: "This destination does not satisfy DropSort's approved-root safety policy.",
    TextId.ORGANIZE_ERROR_CATALOG: "The cataloged file path changed. Reload Movie Details before organizing.",
    TextId.ORGANIZE_ERROR_VALIDATE: "DropSort could not validate this destination. Choose another folder or filename.",
    TextId.ORGANIZE_ERROR_PREPARE: "DropSort could not prepare a safe preview. Please try again.",
    TextId.ORGANIZE_ERROR_STALE: "The source or destination changed after preview. Nothing was authorized; prepare a new preview.",
    TextId.ORGANIZE_ERROR_RECOVERY: "The filesystem operation reached a recoverable state, but DropSort could not finish safely. Recovery is required; do not alter either file.",
    TextId.ORGANIZE_ERROR_EXECUTION: "DropSort could not complete the file operation. The catalog path was not advanced.",
    TextId.ORGANIZE_ERROR_GENERIC: "DropSort could not complete this operation. Please inspect the file and try again.",
    TextId.MEDIA_MISSING_ACTION: "This media file is no longer available at its cataloged location.",
    TextId.PLAY_FAILED: "DropSort could not play this movie. Please try again.",
    TextId.OPEN_FOLDER_FAILED: "DropSort could not open this folder. Please try again.",
    TextId.BUSY_CLEAR: "DropSort is busy. Finish or cancel active scan, import, file check, or poster work before clearing the library.",
    TextId.CLEAR_UNAVAILABLE: "Library clearing is unavailable.",
    TextId.CLEAR_BLOCKED: "Library clearing is blocked until active or recoverable operations are resolved.",
    TextId.CLEAR_DATABASE: "DropSort could not clear the local library; existing catalog data was preserved.",
    TextId.CLEAR_FAILED: "DropSort could not clear the local library.",
    TextId.DETAILS_LOAD_ERROR: "DropSort could not load these movie details. Please return to the library and try again.",
}


_ARABIC: dict[TextId, str] = {
    TextId.WINDOW_TITLE: "مكتبة DropSort للأفلام",
    TextId.BRAND_SUBTITLE: "مكتبة أفلام محلية",
    TextId.NAV_LIBRARY: "المكتبة",
    TextId.NAV_ADD_MOVIES: "إضافة أفلام",
    TextId.NAV_HISTORY: "سجل العمليات",
    TextId.NAV_SETTINGS: "الإعدادات",
    TextId.NAV_PERSONAL_LIBRARY: "مكتبتي",
    TextId.SETTINGS_TITLE: "الإعدادات",
    TextId.APPEARANCE: "المظهر",
    TextId.THEME: "النمط",
    TextId.THEME_MAIN: "الأساسي",
    TextId.THEME_DARK: "الداكن",
    TextId.THEME_SLATE: "سليت",
    TextId.THEME_LIGHT: "الفاتح",
    TextId.HISTORY_RECOVERY: "السجل والاسترداد",
    TextId.VIEW_OPERATION_HISTORY: "عرض سجل العمليات",
    TextId.LANGUAGE_TITLE: "اللغة",
    TextId.LANGUAGE_DESCRIPTION: "اختر لغة واجهة DropSort.",
    TextId.LANGUAGE_ENGLISH: "الإنجليزية",
    TextId.LANGUAGE_ARABIC: "العربية",
    TextId.LANGUAGE_ACCESSIBLE: "اللغة: الإنجليزية أو العربية",
    TextId.ACCESSIBILITY_TMDB_RATING_VISUAL: "عرض تقييم TMDB",
    TextId.ACCESSIBILITY_OPERATION_PATHS: "مسارات المصدر والوجهة للعملية",
    TextId.ACCESSIBILITY_WATCHED_DATE_CALENDAR: "تقويم تاريخ المشاهدة",
    TextId.TMDB_METADATA: "بيانات TMDB",
    TextId.TMDB_NOT_CONFIGURED: "غير مُعد",
    TextId.TMDB_ENVIRONMENT: "تم الإعداد من متغيرات النظام",
    TextId.TMDB_SESSION: "مُعد لهذه الجلسة",
    TextId.TMDB_SESSION_NOTICE: "الرمز الذي تدخله هنا يُستخدم خلال جلسة التطبيق الحالية فقط ولا يتم حفظه بشكل دائم.",
    TextId.TMDB_TOKEN_PLACEHOLDER: "رمز وصول القراءة من TMDB",
    TextId.TMDB_SAVE_SESSION: "استخدام في هذه الجلسة",
    TextId.TMDB_CLEAR_SESSION: "مسح رمز الجلسة",
    TextId.TMDB_ENTER_TOKEN: "أدخل رمز وصول القراءة من TMDB لهذه الجلسة.",
    TextId.TMDB_INVALID_TOKEN: "أدخل رمز وصول صالحًا من TMDB.",
    TextId.TMDB_READY: "بيانات TMDB جاهزة لهذه الجلسة.",
    TextId.TMDB_CLEARED: "تم مسح رمز الجلسة.",
    TextId.ABOUT_CREDITS: "حول التطبيق والاعتمادات",
    TextId.TMDB_NOTICE: "يستخدم هذا المنتج واجهة TMDB البرمجية، لكنه غير معتمد أو مصدّق من TMDB.",
    TextId.TMDB_SOURCE_NOTICE: "يوفّر TMDB بيانات الأفلام وصور الملصقات.",
    TextId.TMDB_RATING_LABEL: "TMDB",
    TextId.TMDB_RATING_UNAVAILABLE: "تقييم TMDB غير متاح",
    TextId.TMDB_INFOBAR_TITLE: "إعداد TMDB",
    TextId.TMDB_INFOBAR_DESCRIPTION: "يستخدم DropSort خدمة TMDB لجلب معلومات الأفلام والملصقات. تعليمات الإعداد الأساسية متاحة داخل التطبيق دون اتصال.",
    TextId.TMDB_SETUP_GUIDE: "دليل الإعداد",
    TextId.TMDB_OPEN_OFFICIAL: "فتح TMDB",
    TextId.TMDB_SETUP_GUIDE_TITLE: "دليل إعداد TMDB",
    TextId.TMDB_SETUP_GUIDE_BODY: "يوفّر TMDB بيانات الأفلام ومراجع الملصقات. يستخدم دروب سورت رمز وصول القراءة من TMDB لاستعادة البيانات والملصقات اختياريًا. أنشئ الرمز أو استرجعه من حساب TMDB، ثم الصقه في حقل الجلسة الآمن في الإعدادات، وتحقق من رسالة الحالة. من دون رمز، يظل تصفح المكتبة المحلية وفحوصات البيانات المحلية متاحًا. لا تشارك الرمز في لقطات الشاشة أو السجلات أو الصادرات أو الرسائل.",
    TextId.LIBRARY_DATA: "بيانات المكتبة",
    TextId.LIBRARY_DATA_NOTICE: "انسَ الأفلام المفهرسة والبيانات المخبأة. لن تتغير ملفات الأفلام الفعلية.",
    TextId.DANGER_ZONE: "إجراءات حساسة",
    TextId.CLEAR_LIBRARY_DESCRIPTION: "امسح بيانات الأفلام والملفات المفهرسة من DropSort. لن يتم حذف ملفات أفلامك الفعلية.",
    TextId.CLEAR_LIBRARY: "مسح بيانات المكتبة",
    TextId.CLEAR_LIBRARY_TITLE: "مسح بيانات المكتبة؟",
    TextId.CLEAR_LIBRARY_CONFIRM: "سيتم مسح روابط ملفات الوسائط المحلية وذاكرة البيانات الوصفية وذاكرة الملصقات. ستظل الأفلام المرتبطة ببياناتك الشخصية محفوظة، بينما ستُزال من المكتبة الأفلام التي لا ترتبط ببيانات شخصية محفوظة. لن يحذف DropSort ملفات أفلامك الفعلية أو ينقلها أو يعيد تسميتها أو ينسخها أو يعدّلها. سيظل سجل العمليات وبيانات الاسترداد محفوظين. هل تريد المتابعة؟",
    TextId.CLEAR_LIBRARY_RUNNING: "جارٍ مسح بيانات المكتبة المحلية...",
    TextId.CLEAR_LIBRARY_RESULT: "تم مسح المكتبة: أُزيل {movies} فيلمًا و{files} رابطًا لملفات الوسائط.",
    TextId.CLEAR_LIBRARY_CACHE_WARNING: "تعذر إكمال تنظيف ذاكرة الملصقات؛ لم تتأثر ملفات الوسائط.",
    TextId.LIBRARY_HEADING: "المكتبة",
    TextId.LIBRARY_COUNT: "{count} فيلم",
    TextId.LIBRARY_COUNT_FILTERED: '{count} فيلم يطابق "{query}"',
    TextId.CHECK_LIBRARY_FILES: "فحص المكتبة",
    TextId.LIBRARY_SEARCH_PLACEHOLDER: "ابحث في مكتبتك...",
    TextId.LIBRARY_SEARCH_CLEAR: "مسح بحث المكتبة",
    TextId.LIBRARY_SEARCH_NO_RESULTS: "لم يتم العثور على أفلام",
    TextId.LIBRARY_SEARCH_NO_RESULTS_HELPER: "جرّب عنوانًا آخر أو امسح البحث الحالي.",
    TextId.LIBRARY_SEARCH_SUGGESTION: "اقتراح من أفلام مكتبتك",
    TextId.LIBRARY_EMPTY: "مكتبتك فارغة حاليًا.",
    TextId.LIBRARY_EMPTY_HELPER: "أضف مجلدًا محليًا لبدء إنشاء مكتبتك.",
    TextId.LIBRARY_LOAD_ERROR: "تعذر على DropSort تحميل المكتبة المحلية. حاول مرة أخرى.",
    TextId.MISSING_FILE: "الملف غير موجود",
    TextId.MISSING_FILES: "{count} ملفات غير موجودة",
    TextId.FILE_SINGULAR: "ملف",
    TextId.FILE_PLURAL: "ملفات",
    TextId.DETAILS_BACK: "رجوع",
    TextId.DETAILS_OVERVIEW: "نبذة",
    TextId.DETAILS_MEDIA_FILES: "ملفات الوسائط",
    TextId.DETAILS_GENRES_UNAVAILABLE: "التصنيفات غير متاحة",
    TextId.DETAILS_ORIGINAL_TITLE: "العنوان الأصلي: {title}",
    TextId.DETAILS_OVERVIEW_UNAVAILABLE: "الملخص غير متاح.",
    TextId.DETAILS_NO_FILES: "لا توجد ملفات وسائط فعلية مرتبطة.",
    TextId.DETAILS_REMOVED: "لم يعد هذا الفيلم موجودًا في المكتبة المحلية.",
    TextId.DETAILS_YOUR_LIBRARY: "مكتبتك",
    TextId.DETAILS_PREFERENCE_GROUP: "رأيك",
    TextId.DETAILS_WATCHLIST_GROUP: "قائمة المشاهدة",
    TextId.DETAILS_WATCHING_GROUP: "المشاهدة",
    TextId.DETAILS_LIKE: "أعجبني",
    TextId.DETAILS_BLACKLIST: "استبعاد",
    TextId.DETAILS_CLEAR_PREFERENCE: "إلغاء الاختيار",
    TextId.DETAILS_ADD_WATCHLIST: "إضافة إلى قائمة المشاهدة",
    TextId.DETAILS_IN_WATCHLIST: "في قائمة المشاهدة",
    TextId.DETAILS_MARK_WATCHED: "تسجيل المشاهدة",
    TextId.DETAILS_MARK_WATCHED_DATE: "تسجيل مشاهدة بتاريخ محدد",
    TextId.DETAILS_WATCHED_COUNT: "عدد مرات المشاهدة: {count}",
    TextId.DETAILS_LAST_WATCHED: "آخر مشاهدة: {date}",
    TextId.DETAILS_NOT_WATCHED: "لم تتم مشاهدته بعد",
    TextId.DETAILS_WATCH_HISTORY: "سجل المشاهدة",
    TextId.DETAILS_FIRST_WATCH: "أول مشاهدة",
    TextId.DETAILS_REWATCH: "إعادة مشاهدة",
    TextId.DETAILS_REMOVE_WATCH_EVENT: "إزالة",
    TextId.DETAILS_PERSONAL_LOAD_ERROR: "تعذر تحميل المعلومات الشخصية. حاول مرة أخرى.",
    TextId.DETAILS_PERSONAL_SAVE_ERROR: "تعذر حفظ التغيير الشخصي. تمت استعادة حالتك الحالية.",
    TextId.DETAILS_WATCH_DATE: "تاريخ المشاهدة",
    TextId.DETAILS_PICK_DATE: "اختر تاريخًا",
    TextId.DETAILS_WATCH_DATE_FUTURE: "اختر تاريخ اليوم أو تاريخًا سابقًا.",
    TextId.DETAILS_WATCH_SAVED: "تم حفظ المشاهدة.",
    TextId.PERSONAL_LIBRARY_HEADING: "مكتبتي",
    TextId.PERSONAL_TAB_WATCHLIST: "قائمة المشاهدة",
    TextId.PERSONAL_TAB_READY: "جاهزة للمشاهدة",
    TextId.PERSONAL_TAB_LIKED: "أعجبتني",
    TextId.PERSONAL_TAB_BLACKLISTED: "المستبعدة",
    TextId.PERSONAL_EMPTY_WATCHLIST: "قائمة المشاهدة فارغة.",
    TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION: "الأفلام التي تحفظها لوقت لاحق ستظهر هنا.",
    TextId.PERSONAL_EMPTY_READY: "لا توجد أفلام جاهزة للمشاهدة حاليًا.",
    TextId.PERSONAL_EMPTY_READY_DESCRIPTION: "الأفلام الموجودة في قائمة المشاهدة والمتوفرة محليًا ستظهر هنا.",
    TextId.PERSONAL_EMPTY_LIKED: "لم تُبدِ إعجابك بأي فيلم بعد.",
    TextId.PERSONAL_EMPTY_LIKED_DESCRIPTION: "ستظهر هنا الأفلام التي تعجبك.",
    TextId.PERSONAL_EMPTY_BLACKLISTED: "قائمة الحظر فارغة.",
    TextId.PERSONAL_EMPTY_BLACKLISTED_DESCRIPTION: "ستظهر هنا الأفلام التي تحظرها.",
    TextId.PERSONAL_LOADING: "جارٍ تحميل المكتبة الشخصية...",
    TextId.PERSONAL_LOAD_ERROR: "تعذر تحميل المكتبة الشخصية. حاول مرة أخرى.",
    TextId.PERSONAL_NO_LOCAL_COPY: "لا توجد نسخة محلية",
    TextId.STATUS_PRESENT: "موجود",
    TextId.STATUS_MISSING: "مفقود",
    TextId.LAST_KNOWN_PATH: "آخر مسار معروف: ",
    TextId.CURRENT_PATH: "المسار: ",
    TextId.PLAY_MOVIE: "تشغيل الفيلم",
    TextId.OPEN_FOLDER: "فتح المجلد",
    TextId.ORGANIZE_FILE: "تنظيم الملف",
    TextId.LOCATE_FILE: "تحديد موقع الملف",
    TextId.ADD_MOVIES_TITLE: "إضافة أفلام",
    TextId.ADD_MOVIES_GUIDANCE: "اختر مجلدًا محليًا للفحص. سيحدد DropSort الأفلام المرشحة مع إبقاء كل عملية إضافة صريحة.",
    TextId.ADD_MOVIES_FOLDER_LABEL: "المجلد",
    TextId.ADD_MOVIES_DETECTED_HEADING: "الأفلام المكتشفة",
    TextId.ADD_MOVIES_RESULTS_TITLE: "العنوان",
    TextId.ADD_MOVIES_RESULTS_YEAR: "السنة",
    TextId.ADD_MOVIES_RESULTS_RESOLUTION: "الدقة",
    TextId.ADD_MOVIES_RESULTS_STATUS: "الحالة",
    TextId.ADD_MOVIES_RESULTS_ACTION: "الإجراء",
    TextId.CHOOSE_FOLDER_SCAN: "اختيار مجلد وبدء الفحص",
    TextId.CHOOSE_MOVIE_FOLDER: "اختر مجلد الأفلام",
    TextId.CANCEL_SCAN: "إلغاء الفحص",
    TextId.INCLUDE_SUBFOLDERS: "تضمين المجلدات الفرعية",
    TextId.NO_FOLDER: "لم يتم اختيار مجلد",
    TextId.SCAN_READY: "اختر مجلدًا لبدء فحص للقراءة فقط.",
    TextId.SCANNING_MOVIES: "جارٍ فحص الأفلام...",
    TextId.CANCELLING_SCAN: "جارٍ إلغاء الفحص...",
    TextId.PREPARING_MATCHES: "جارٍ تجهيز مطابقات البيانات الوصفية...",
    TextId.BUILDING_RESULTS: "جارٍ إعداد نتائج المراجعة...",
    TextId.INVALID_SCAN_RESULT: "استلم دروب سورت نتيجة فحص غير صالحة. حاول مرة أخرى.",
    TextId.ADD_TO_LIBRARY: "إضافة إلى المكتبة",
    TextId.OPEN_SETTINGS: "فتح الإعدادات",
    TextId.EDIT_SEARCH: "تعديل البحث",
    TextId.SEARCH_MANUALLY: "بحث يدوي",
    TextId.SEARCH_TMDB: "البحث في TMDB",
    TextId.DETECTED_TITLE: "العنوان المكتشف",
    TextId.SEARCH_AS: "البحث باسم",
    TextId.YEAR: "السنة (اختياري)",
    TextId.NO_RESULTS: "لم يتم العثور على نتائج.",
    TextId.SELECT_THIS_MOVIE: "اختيار هذا الفيلم",
    TextId.MANUAL_SEARCH_RESULTS: "النتائج",
    TextId.MANUAL_SEARCH_SELECT: "اختيار",
    TextId.MANUAL_SEARCH_RATING: "التقييم",
    TextId.MANUAL_SEARCH_NO_OVERVIEW: "لا يوجد وصف متاح.",
    TextId.MANUAL_SEARCH_SEARCHING: "جارٍ البحث في TMDB...",
    TextId.INVALID_YEAR: "أدخل سنة صحيحة من 4 أرقام أو اترك الحقل فارغًا.",
    TextId.MANUAL_SEARCH_PROVIDER_FAILED: "بحث TMDB غير متاح الآن. حاول مرة أخرى لاحقًا.",
    TextId.IMPORT_MANUAL_SELECTED: "تم اختيار مرشح يدوي",
    TextId.IMPORT_MANUAL_EXPLANATION: "راجع المرشح ثم أضفه صراحةً إلى المكتبة المحلية.",
    TextId.COPY: "نسخ",
    TextId.ADDED_TO_LIBRARY: "تمت الإضافة إلى المكتبة",
    TextId.DISMISS_PROPOSAL: "تجاهل",
    TextId.ALL_DONE: "اكتمل كل شيء",
    TextId.NO_MOVIES_WAITING: "لا توجد أفلام بانتظار المراجعة.",
    TextId.ADDING_TO_LIBRARY: "جارٍ الإضافة إلى المكتبة…",
    TextId.SCAN_PROGRESS_DISCOVERY: "المجلدات: {folders} · الملفات التي تم فحصها: {files} · ملفات الأفلام المحتملة: {movies}",
    TextId.SCAN_PROGRESS_METADATA: "جارٍ إعداد مقترحات البيانات {done} / {total}",
    TextId.SCAN_PROGRESS_ROWS: "جارٍ إعداد نتائج المراجعة...",
    TextId.SCAN_COMPLETE_EMPTY: "اكتمل الفحص. لم يُعثر على أفلام مرشحة في هذا المجلد.",
    TextId.SCAN_COMPLETE: "اكتمل الفحص. الملفات التي تم فحصها: {files}. جاهز للإضافة أو المراجعة: {ready}. موجود بالفعل في المكتبة: {existing}. الأخطاء: {errors}.",
    TextId.SCAN_CANCELLED: "تم إلغاء الفحص. الملفات التي تم فحصها قبل الإلغاء: {files}. تم تجاهل النتائج الجزئية. لم يتم تغيير أي ملفات.",
    TextId.SCAN_ROOT_MISSING: "لم يعد المجلد المحدد موجودًا.",
    TextId.SCAN_ROOT_NOT_FOLDER: "المسار المحدد ليس مجلدًا.",
    TextId.SCAN_ROOT_LINK: "لا يُسمح بجذور فحص مرتبطة أو نقاط إعادة تحليل.",
    TextId.SCAN_PERMISSION: "لا يملك دروب سورت إذن قراءة المجلد المحدد.",
    TextId.SCAN_SAFE_ERROR: "تعذر على دروب سورت فحص المجلد المحدد بأمان.",
    TextId.SCAN_FAILED: "تعذر على دروب سورت فحص المجلد المحدد. حاول مرة أخرى.",
    TextId.SCAN_TV_SKIPPED_SUFFIX: " تم تخطي {count} حلقات تلفزيونية.",
    TextId.SCAN_SUMMARY_COUNTS: "أفلام مرشحة: {movies} · حلقات متخطاة: {tv} · وسائط غير معروفة: {unknown}",
    TextId.IMPORT_MATCH_PROPOSED: "تم اقتراح تطابق",
    TextId.IMPORT_REVIEW_REQUIRED: "تتطلب مراجعة",
    TextId.IMPORT_NO_MATCH: "لا يوجد تطابق",
    TextId.IMPORT_METADATA_UNAVAILABLE: "البيانات الوصفية غير متاحة",
    TextId.IMPORT_ALREADY_LIBRARY: "موجود بالفعل في المكتبة",
    TextId.IMPORT_TV_SKIPPED: "تم تخطي حلقة تلفزيونية",
    TextId.IMPORT_UNKNOWN: "وسائط غير معروفة",
    TextId.IMPORT_SCAN_ERROR: "خطأ في الفحص",
    TextId.IMPORT_TV_EXPLANATION: "تم اكتشاف نمط حلقة تلفزيونية. استيراد المسلسلات غير مدعوم بعد.",
    TextId.IMPORT_UNKNOWN_EXPLANATION: "تعذر تصنيف اسم ملف الفيديو المدعوم كفيلم.",
    TextId.IMPORT_DISCOVERY_EXPLANATION: "تعذر على دروب سورت قراءة هذا العنصر بأمان، لذلك تم تخطيه.",
    TextId.IMPORT_AUTH_EXPLANATION: "لم يتم إعداد TMDB أو رُفض الرمز الحالي. أضف رمز وصول قراءة TMDB في الإعدادات لتفعيل البيانات والمطابقة.",
    TextId.IMPORT_RATE_EXPLANATION: "يحد TMDB من معدل الطلبات. انتظر قليلًا ثم أعد الفحص.",
    TextId.IMPORT_RESPONSE_EXPLANATION: "أعاد TMDB استجابة غير صالحة. لم تتم محاولة الاستيراد.",
    TextId.IMPORT_NO_MATCH_EXPLANATION: "لم يُعثر على فيلم مرشح بموثوقية كافية.",
    TextId.IMPORT_UNAVAILABLE_EXPLANATION: "تعذر الحصول على بيانات الفيلم. لم تتم محاولة الاستيراد.",
    TextId.IMPORT_ALREADY_EXPLANATION: "هذا المسار الفعلي مفهرس بالفعل.",
    TextId.IMPORT_REVIEW_EXPLANATION: "راجع نتيجة الاكتشاف قبل الاستيراد.",
    TextId.IMPORT_CONFIDENCE: "الثقة {confidence}% · {reasons}",
    TextId.IMPORT_INVALID_CANDIDATE: "الفيلم المرشح المحدد غير صالح. أعد الفحص.",
    TextId.IMPORT_DETAILS_UNAVAILABLE: "تفاصيل الفيلم غير متاحة. حاول لاحقًا.",
    TextId.IMPORT_CATALOG_FAILED: "تعذرت إضافة هذا الفيلم إلى المكتبة. حاول مرة أخرى.",
    TextId.IMPORT_FAILED: "تعذر على دروب سورت إكمال الاستيراد. حاول مرة أخرى.",
    TextId.HISTORY_TITLE: "سجل العمليات",
    TextId.REFRESH: "تحديث",
    TextId.HISTORY_GUIDANCE: "أحدث عمليات الملفات التي نفذها DropSort.",
    TextId.HISTORY_LOADING: "جارٍ تحميل سجل العمليات المحلي...",
    TextId.HISTORY_EMPTY: "لا توجد عمليات ملفات حتى الآن.\n\nستظهر هنا عمليات نقل الملفات وغيرها من العمليات التي يديرها DropSort.",
    TextId.HISTORY_READ_ERROR: "تعذر على دروب سورت قراءة سجل العمليات. حاول مرة أخرى.",
    TextId.HISTORY_UNLINKED: "عملية وسائط غير مرتبطة",
    TextId.HISTORY_REVERSE: "عملية عكسية",
    TextId.HISTORY_COPY: "نسخ",
    TextId.HISTORY_SAVE: "حفظ",
    TextId.HISTORY_SELECT: "تحديد العملية",
    TextId.HISTORY_COPY_EMPTY: "حدد عملية لنسخها.",
    TextId.HISTORY_COPY_SUCCESS: "تم نسخ سجل العمليات إلى الحافظة.",
    TextId.HISTORY_SAVE_SUCCESS: "تم حفظ سجل العمليات.",
    TextId.HISTORY_SAVE_ERROR: "تعذر حفظ سجل العمليات.",
    TextId.HISTORY_FROM: "من",
    TextId.HISTORY_TO: "إلى",
    TextId.HISTORY_OPERATION_ID: "معرّف العملية",
    TextId.HISTORY_OPERATION_MOVE: "نقل",
    TextId.HISTORY_OPERATION_RENAME: "إعادة تسمية",
    TextId.HISTORY_STATUS_PLANNED: "مخطط لها",
    TextId.HISTORY_STATUS_VALIDATED: "تم التحقق",
    TextId.HISTORY_STATUS_IN_PROGRESS: "قيد التنفيذ",
    TextId.HISTORY_STATUS_VERIFIED: "تم التأكد",
    TextId.HISTORY_STATUS_COMPLETED: "مكتملة",
    TextId.HISTORY_STATUS_FAILED: "فشلت",
    TextId.HISTORY_STATUS_RECOVERY_REQUIRED: "يلزم الاسترداد",
    TextId.DETAILS: "التفاصيل",
    TextId.OPERATION_DETAILS: "تفاصيل العملية",
    TextId.HISTORY_READ_ONLY: "تفاصيل السجل للعرض فقط.",
    TextId.PREVIEW_UNDO: "معاينة التراجع",
    TextId.INSPECT_RECOVERY: "فحص الاسترداد",
    TextId.ATTEMPT_RECOVERY: "محاولة استرداد آمنة",
    TextId.CLOSE: "إغلاق",
    TextId.UNDO_PREVIEW: "معاينة التراجع",
    TextId.UNDO_WARNING: "ينشئ التراجع عملية عكسية جديدة في السجل. يبقى سجل العملية الأصلية دون تغيير.",
    TextId.UNDO_NO_CHANGE: "لم يحدث أي تغيير في السجل أو نظام الملفات.",
    TextId.CANCEL: "إلغاء",
    TextId.CONFIRM_UNDO: "تأكيد التراجع",
    TextId.UNDO_RUNNING: "جارٍ تنفيذ العملية العكسية الآمنة...",
    TextId.HISTORY_LOADING_DETAILS: "جارٍ تحميل تفاصيل العملية...",
    TextId.HISTORY_INVALID: "استلم دروب سورت بيانات غير صالحة لسجل العمليات.",
    TextId.HISTORY_INVALID_DETAILS: "استلم دروب سورت تفاصيل عملية غير صالحة.",
    TextId.UNDO_REVALIDATING: "جارٍ إعادة التحقق من الملف الحالي والمسارات التاريخية الدقيقة...",
    TextId.UNDO_PREPARED: "تم إعداد معاينة التراجع. لم يحدث أي تغيير في نظام الملفات.",
    TextId.UNDO_INVALID: "استلم دروب سورت معاينة تراجع غير صالحة.",
    TextId.UNDO_VERIFY_FAILED: "تعذر على دروب سورت التحقق من أهلية التراجع. حدّث السجل.",
    TextId.UNDO_PREPARE_FAILED: "تعذر على دروب سورت إعداد معاينة التراجع.",
    TextId.UNDO_COMPLETED: "اكتمل التراجع من خلال عملية عكسية جديدة وملتزمة.",
    TextId.UNDO_RESULT_INVALID: "أعادت العملية العكسية نتيجة غير صالحة؛ قد يكون الاسترداد مطلوبًا.",
    TextId.UNDO_CATALOG_UPDATED: "اكتمل التراجع وتم تحديث مسار الفهرس.",
    TextId.UNDO_RECOVERY_REQUIRED: "تتطلب العملية العكسية استردادًا. يُحفظ الملفان عند وجود غموض.",
    TextId.UNDO_FAILED: "تعذر على دروب سورت إكمال عملية التراجع بأمان.",
    TextId.UNDO_FAILED_GENERIC: "تعذر على دروب سورت إكمال عملية التراجع.",
    TextId.RECOVERY_INVALID: "استلم دروب سورت تقييم استرداد غير صالح.",
    TextId.RECOVERY_INSPECT_FAILED: "تعذر على دروب سورت فحص حالة الاسترداد بأمان.",
    TextId.RECOVERY_COMPLETE: "اكتمل الاسترداد بحالة السجل {state}.",
    TextId.HISTORY_FIELD_STATE: "الحالة",
    TextId.HISTORY_FIELD_OPERATION: "العملية",
    TextId.HISTORY_FIELD_SOURCE: "المصدر",
    TextId.HISTORY_FIELD_DESTINATION: "الوجهة",
    TextId.HISTORY_FIELD_STRATEGY: "طريقة التنفيذ",
    TextId.HISTORY_FIELD_CURRENT_PATH: "المسار الحالي في المكتبة",
    TextId.HISTORY_FIELD_CREATED: "تاريخ الإنشاء",
    TextId.HISTORY_NOT_RECORDED: "غير مسجل",
    TextId.HISTORY_NOT_LINKED: "غير مرتبط",
    TextId.HISTORY_FIELD_FILE_SIZE: "حجم الملف",
    TextId.HISTORY_FIELD_TRANSFER: "النقل",
    TextId.CHECK_FILES_TITLE: "فحص المكتبة",
    TextId.CHECK_FILES_READY: "جاهز لفحص ملفات المكتبة.",
    TextId.CHECK_FILES_CANCEL: "إلغاء الفحص",
    TextId.CHECK_FILES_RUNNING: "جارٍ فحص ملفات المكتبة وبيانات الأفلام...",
    TextId.CHECK_FILES_CANCELLING: "جارٍ إلغاء فحص المكتبة...",
    TextId.CHECK_FILES_CANCELLED: "تم إلغاء الفحص",
    TextId.CHECK_FILES_FAILED: "تعذر على دروب سورت إكمال فحص المكتبة.",
    TextId.CHECK_FILES_PROGRESS: "{prefix}: {checked} / {total} | موجود: {present} | مفقود: {missing} | أخطاء: {errors}",
    TextId.CHECK_FILES_BACKGROUND: "جارٍ فحص مواقع الوسائط المفهرسة في الخلفية...",
    TextId.CHECK_FILES_ALREADY_RUNNING: "يوجد فحص لملفات المكتبة قيد التشغيل في الخلفية بالفعل.",
    TextId.CHECK_FILES_COMPLETE: "اكتمل فحص ملفات المكتبة: {present} موجود، {missing} مفقود، {errors} غير متاح.",
    TextId.CHECK_FILES_DONE: "تم",
    TextId.CHECK_FILES_BACKGROUND_CANCELLED: "أُلغي فحص ملفات المكتبة في الخلفية.",
    TextId.CHECK_FILES_BACKGROUND_FAILED: "تعذر على دروب سورت إكمال فحص ملفات المكتبة في الخلفية.",
    TextId.CHECK_LIBRARY_PROGRESS: "{prefix}\n\nالملفات: تم فحص {file_checked} من {file_total} | موجود: {present} | مفقود: {missing} | أخطاء: {errors}\nالبيانات الوصفية: تم فحص {metadata_checked} من {metadata_total} | مكتمل: {complete} | مشكلات: {issues} | تم الإصلاح: {repaired}",
    TextId.CHECK_LIBRARY_COMPLETE: "اكتمل فحص المكتبة\n\nالملفات\nتم الفحص: {file_checked}\nموجود: {present}\nمفقود: {missing}\nأخطاء: {errors}\n\nالبيانات الوصفية\nمكتمل: {complete}\nالمشكلات: {issues}\nتم الإصلاح: {repaired}\nبحاجة إلى مراجعة: {needs_review}\nالمزوّد غير متاح: {provider_unavailable}",
    TextId.CHECK_LIBRARY_COMPLETE_TITLE: "اكتمل فحص المكتبة",
    TextId.CHECK_LIBRARY_IDLE_DESCRIPTION: "افحص ملفاتك المحلية وبيانات الأفلام الوصفية.\nسيتم الإبلاغ عن الملفات المفقودة والبيانات غير المكتملة.\nيمكن استعادة البيانات الوصفية المفقودة القابلة للإصلاح بأمان.",
    TextId.CHECK_LIBRARY_RUNNING_STATUS: "جارٍ فحص ملفات المكتبة والبيانات الوصفية للأفلام...",
    TextId.CHECK_LIBRARY_HEALTHY_TITLE: "مكتبتك بحالة جيدة",
    TextId.CHECK_LIBRARY_FAILURE_TITLE: "تعذر إكمال فحص المكتبة",
    TextId.CHECK_LIBRARY_FAILURE_DESCRIPTION: "لم يتم تعديل مكتبتك. تتوفر التفاصيل التقنية في سجل التطبيق.",
    TextId.CHECK_LIBRARY_CANCELLED_DESCRIPTION: "تم إلغاء الفحص. لم يتم تعديل مكتبتك.",
    TextId.CHECK_LIBRARY_TRY_AGAIN: "حاول مرة أخرى",
    TextId.CHECK_LIBRARY_FILES_CHECKED: "تم فحص {count} من الملفات",
    TextId.CHECK_LIBRARY_ALL_FILES_PRESENT: "جميع الملفات المفهرسة موجودة.",
    TextId.CHECK_LIBRARY_MISSING_FILES_SUMMARY: "{count} من الملفات مفقود.",
    TextId.CHECK_LIBRARY_FILE_ERRORS_SUMMARY: "أخطاء في {count} من الملفات.",
    TextId.CHECK_LIBRARY_METADATA_COMPLETE_SUMMARY: "البيانات الوصفية مكتملة.",
    TextId.CHECK_LIBRARY_METADATA_ISSUES_SUMMARY: "تم العثور على {count} من مشكلات البيانات الوصفية.",
    TextId.CHECK_LIBRARY_REPAIRED_SUMMARY: "تم إصلاح {count}.",
    TextId.CHECK_LIBRARY_NEEDS_ATTENTION_SUMMARY: "{count} بحاجة إلى انتباه.",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE_SUMMARY: "نتائج المزوّد غير متاحة لعدد {count}.",
    TextId.CHECK_LIBRARY_ISSUES_SECTION: "مشكلات تحتاج مراجعة",
    TextId.CHECK_LIBRARY_AGAIN: "إعادة الفحص",
    TextId.CHECK_LIBRARY_FILES_SECTION: "الملفات",
    TextId.CHECK_LIBRARY_METADATA_SECTION: "البيانات الوصفية",
    TextId.CHECK_LIBRARY_CHECKED: "تم الفحص",
    TextId.CHECK_LIBRARY_PASSED: "اجتاز الفحص",
    TextId.CHECK_LIBRARY_PRESENT: "موجود",
    TextId.CHECK_LIBRARY_MISSING: "مفقود",
    TextId.CHECK_LIBRARY_ERRORS: "أخطاء",
    TextId.CHECK_LIBRARY_COMPLETE_COUNT: "مكتمل",
    TextId.CHECK_LIBRARY_ISSUES: "المشكلات الموجودة",
    TextId.CHECK_LIBRARY_REPAIRED_COUNT: "تم الإصلاح",
    TextId.CHECK_LIBRARY_NEEDS_ATTENTION: "بحاجة إلى انتباه",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE_COUNT: "المزوّد غير متاح",
    TextId.CHECK_LIBRARY_RESULTS: "النتائج",
    TextId.CHECK_LIBRARY_ISSUE: "المشكلة",
    TextId.CHECK_LIBRARY_OUTCOME: "النتيجة",
    TextId.CHECK_LIBRARY_NOT_REPAIRED: "لم يتم الإصلاح",
    TextId.CHECK_LIBRARY_RESULT_PROVIDER_UNAVAILABLE: "المزوّد غير متاح",
    TextId.CHECK_LIBRARY_PROVIDER_SKIPPED: "تم تخطي إصلاح البيانات الوصفية: لم يتم إعداد TMDB.",
    TextId.CHECK_LIBRARY_NO_ISSUES: "لم يتم العثور على مشكلات في البيانات الوصفية.",
    TextId.CHECK_LIBRARY_ISSUE_OVERVIEW: "الملخص مفقود",
    TextId.CHECK_LIBRARY_ISSUE_RUNTIME: "المدة مفقودة",
    TextId.CHECK_LIBRARY_ISSUE_GENRES: "التصنيفات مفقودة",
    TextId.CHECK_LIBRARY_ISSUE_YEAR: "السنة مفقودة",
    TextId.CHECK_LIBRARY_ISSUE_POSTER: "الملصق مفقود",
    TextId.CHECK_LIBRARY_ISSUE_NEEDS_MATCH: "بحاجة إلى مطابقة مع المزوّد",
    TextId.CHECK_LIBRARY_REPAIRED: "تم الإصلاح: {fields}",
    TextId.CHECK_LIBRARY_NEEDS_REVIEW: "بحاجة إلى مراجعة",
    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE: "المزوّد غير متاح: {reason}",
    TextId.CHECK_LIBRARY_AUTHENTICATION: "لم يتم إعداد TMDB أو فشلت المصادقة",
    TextId.CHECK_LIBRARY_RATE_LIMIT: "تم بلوغ حد معدل طلبات TMDB",
    TextId.CHECK_LIBRARY_INVALID_RESPONSE: "أعاد TMDB استجابة غير صالحة",
    TextId.RELINK_TITLE: "إعادة ربط ملف الوسائط",
    TextId.RELINK_CHOOSE: "اختر ملف الوسائط البديل.",
    TextId.RELINK_CONFIRM: "تأكيد إعادة الربط",
    TextId.RELINK_VALIDATING: "جارٍ التحقق من الملف المحدد...",
    TextId.RELINK_CONFIRMING: "جارٍ تأكيد إعادة ربط الفهرس...",
    TextId.RELINK_VALID: "تم التحقق من الملف. أكّد إعادة الربط لتصحيح الفهرس فقط.",
    TextId.RELINK_COMPLETE: "اكتملت إعادة الربط. لم يُنقل الملف الفعلي.",
    TextId.RELINK_OLD_NEW: "المسار القديم / آخر مسار معروف\n{old}\n\nالمسار الجديد المحدد\n{new}",
    TextId.RELINK_PREVIEW: "{paths}\n\nالحجم: {size} بايت\nالتحقق: الملف متاح ولا يوجد تعارض في الفهرس",
    TextId.RELINK_BLOCKED: "تم منع إعادة الربط: {reason}.",
    TextId.RELINK_STALE: "لم تعد إعادة الربط صالحة. اختر الملف مرة أخرى.",
    TextId.RELINK_FAILED: "تعذر على دروب سورت إكمال إعادة الربط.",
    TextId.RELINK_FILE_DIALOG: "تحديد موقع ملف الوسائط",
    TextId.VIDEO_FILES: "ملفات الفيديو",
    TextId.ORGANIZE_TITLE: "تنظيم الملف",
    TextId.ORGANIZE_GUIDANCE: "اختر وجهة واحدة وراجع المسارات الدقيقة ثم أكّد العملية الآمنة المسجلة صراحةً.",
    TextId.CHOOSE_DESTINATION: "اختيار مجلد الوجهة",
    TextId.CHOOSE_DESTINATION_DIALOG: "اختر مجلد الوجهة",
    TextId.REFRESH_PREVIEW: "تحديث المعاينة",
    TextId.NOT_VALIDATED: "لم يتم التحقق",
    TextId.ORGANIZE_READY: "اختر وجهة للتحقق من هذه العملية.",
    TextId.CONFIRM_MOVE_RENAME: "تأكيد النقل / إعادة التسمية",
    TextId.ORGANIZE_VALIDATING: "جارٍ التحقق من المصدر والوجهة بدقة…",
    TextId.ORGANIZE_RUNNING: "العملية المتحقق منها قيد التنفيذ. سيكملها دروب سورت بأمان.",
    TextId.ORGANIZE_VALID: "تم التحقق من المصدر. الوجهة متاحة ولا يوجد تعارض.",
    TextId.ORGANIZE_COMPLETE: "اكتمل تنظيم الملف وتم تحديث مسار الفهرس.",
    TextId.ORGANIZE_INVALID_PREVIEW: "استلم دروب سورت معاينة غير صالحة. حاول مرة أخرى.",
    TextId.ORGANIZE_SAME_DRIVE: "نقل على محرك واحد: سيستخدم دروب سورت النقل المحلي الآمن المعتمد.",
    TextId.ORGANIZE_CROSS_DRIVE: "نقل بين محركين: سينسخ دروب سورت الملف ويفرغه ويتحقق من بصمته قبل إزالة الأصل.",
    TextId.ORGANIZE_CONFIRM_MOVE: "نقل الملف",
    TextId.ORGANIZE_CONFIRM_RENAME: "إعادة تسمية الملف",
    TextId.ORGANIZE_CONFIRM_MOVE_AND_RENAME: "نقل الملف وإعادة تسميته",
    TextId.ORGANIZE_FROM: "من",
    TextId.ORGANIZE_TO: "إلى",
    TextId.ORGANIZE_OPERATION: "العملية",
    TextId.ORGANIZE_FILE_SIZE: "حجم الملف",
    TextId.ORGANIZE_VOLUMES: "وحدات التخزين",
    TextId.ORGANIZE_TRANSFER: "النقل",
    TextId.ORGANIZE_FILENAME_CHANGED: "تغير اسم ملف الوجهة. حدّث المعاينة قبل التأكيد.",
    TextId.ORGANIZE_RESULT_INVALID: "انتهت العملية بنتيجة غير صالحة. قد يكون الاسترداد مطلوبًا.",
    TextId.ORGANIZE_ERROR_DEST_EXISTS: "الوجهة موجودة بالفعل. اختر مجلدًا أو اسمًا آخر؛ لا يستبدل دروب سورت الملفات أبدًا.",
    TextId.ORGANIZE_ERROR_CASE_COLLISION: "يوجد ملف آخر في الوجهة نفسها باختلاف حالة الأحرف. اختر وجهة أخرى.",
    TextId.ORGANIZE_ERROR_SAME_FILE: "المصدر والوجهة يشيران إلى الملف نفسه. اختر وجهة مختلفة.",
    TextId.ORGANIZE_ERROR_SOURCE_MISSING: "لم يعد ملف المصدر المفهرس متاحًا. لم يتغير شيء.",
    TextId.ORGANIZE_ERROR_LINK: "لا يُسمح بالمسارات المرتبطة أو نقاط إعادة التحليل عند التنظيم.",
    TextId.ORGANIZE_ERROR_UNSAFE: "لا تستوفي هذه الوجهة سياسة الجذور الآمنة المعتمدة في دروب سورت.",
    TextId.ORGANIZE_ERROR_CATALOG: "تغير مسار الملف المفهرس. أعد تحميل تفاصيل الفيلم قبل التنظيم.",
    TextId.ORGANIZE_ERROR_VALIDATE: "تعذر على دروب سورت التحقق من الوجهة. اختر مجلدًا أو اسمًا آخر.",
    TextId.ORGANIZE_ERROR_PREPARE: "تعذر على دروب سورت إعداد معاينة آمنة. حاول مرة أخرى.",
    TextId.ORGANIZE_ERROR_STALE: "تغير المصدر أو الوجهة بعد المعاينة. لم يتم تفويض شيء؛ أعد إعداد المعاينة.",
    TextId.ORGANIZE_ERROR_RECOVERY: "وصلت عملية نظام الملفات إلى حالة قابلة للاسترداد، لكن تعذر إكمالها بأمان. يلزم الاسترداد؛ لا تعدّل أيًا من الملفين.",
    TextId.ORGANIZE_ERROR_EXECUTION: "تعذر على دروب سورت إكمال عملية الملف. لم يتم تحديث مسار الفهرس.",
    TextId.ORGANIZE_ERROR_GENERIC: "تعذر على دروب سورت إكمال العملية. افحص الملف وحاول مرة أخرى.",
    TextId.MEDIA_MISSING_ACTION: "لم يعد ملف الوسائط متاحًا في موقعه المفهرس.",
    TextId.PLAY_FAILED: "تعذر على دروب سورت تشغيل هذا الفيلم. حاول مرة أخرى.",
    TextId.OPEN_FOLDER_FAILED: "تعذر على دروب سورت فتح هذا المجلد. حاول مرة أخرى.",
    TextId.BUSY_CLEAR: "دروب سورت مشغول. أنهِ أو ألغِ الفحص أو الاستيراد أو فحص الملفات أو تحميل الملصقات قبل مسح المكتبة.",
    TextId.CLEAR_UNAVAILABLE: "مسح المكتبة غير متاح.",
    TextId.CLEAR_BLOCKED: "مسح المكتبة محظور حتى تُحل العمليات النشطة أو القابلة للاسترداد.",
    TextId.CLEAR_DATABASE: "تعذر على دروب سورت مسح المكتبة المحلية؛ بقيت بيانات الفهرس الحالية محفوظة.",
    TextId.CLEAR_FAILED: "تعذر على دروب سورت مسح المكتبة المحلية.",
    TextId.DETAILS_LOAD_ERROR: "تعذر على دروب سورت تحميل تفاصيل الفيلم. ارجع إلى المكتبة وحاول مرة أخرى.",
}


class UiLocalizer(QObject):
    language_changed = Signal(object)

    def __init__(self, language: UiLanguage = UiLanguage.ENGLISH, parent=None) -> None:
        super().__init__(parent)
        self._language = language
        self._bindings: weakref.WeakKeyDictionary[QWidget, tuple[TextId, dict[str, object]]] = (
            weakref.WeakKeyDictionary()
        )
        self._apply_direction()

    @property
    def language(self) -> UiLanguage:
        return self._language

    def text(self, key: TextId, **values: object) -> str:
        catalog = _ARABIC if self._language is UiLanguage.ARABIC else _ENGLISH
        template = catalog.get(key, _ENGLISH[key])
        return template.format(**values)

    def bind_text(self, widget: QWidget, key: TextId, **values: object) -> None:
        self._bindings[widget] = (key, values)
        widget.setText(self.text(key, **values))  # type: ignore[attr-defined]

    def refresh_binding(self, widget: QWidget, **values: object) -> None:
        key, previous = self._bindings[widget]
        merged = {**previous, **values}
        self._bindings[widget] = (key, merged)
        widget.setText(self.text(key, **merged))  # type: ignore[attr-defined]

    def set_language(self, language: UiLanguage) -> None:
        if not isinstance(language, UiLanguage):
            raise ValueError("language must be supported")
        self._language = language
        self._apply_direction()
        for widget, (key, values) in tuple(self._bindings.items()):
            widget.setText(self.text(key, **values))  # type: ignore[attr-defined]
        self.language_changed.emit(language)

    def mark_ltr(self, widget: QWidget) -> None:
        widget.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        widget.setProperty("dropsortTechnicalLtr", True)

    def missing_translations(self) -> tuple[TextId, ...]:
        return tuple(key for key in TextId if key not in _ENGLISH or key not in _ARABIC)

    def _apply_direction(self) -> None:
        application = QApplication.instance()
        if application is not None:
            direction = (
                Qt.LayoutDirection.RightToLeft
                if self._language is UiLanguage.ARABIC
                else Qt.LayoutDirection.LeftToRight
            )
            application.setLayoutDirection(direction)
