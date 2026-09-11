using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;

namespace DropSort.UI.Services;

public enum UiLanguage
{
    English,
    Arabic,
}

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        // Shell
        ["WindowTitle"] = "DropSort Media Library",
        ["Home"] = "Home",
        ["Library"] = "Library",
        ["MyLists"] = "My Lists",
        ["AddMedia"] = "Add Media",
        ["CheckLibrary"] = "Check Library",
        ["Settings"] = "Settings",
        ["OperationsLog"] = "Operations Log",

        // Shared vocabulary
        ["Search"] = "Search",
        ["Cancel"] = "Cancel",
        ["Save"] = "Save",
        ["Copy"] = "Copy",
        ["Refresh"] = "Refresh",
        ["Close"] = "Close",
        ["Confirm"] = "Confirm",
        ["Remove"] = "Remove",
        ["More"] = "More",
        ["All"] = "All",
        ["Success"] = "Success",
        ["Failed"] = "Failed",
        ["Warning"] = "Warning",
        ["Retry"] = "Retry",
        ["Export"] = "Export",
        ["Import"] = "Import",
        ["Browse"] = "Browse",
        ["Folder"] = "Folder",
        ["Title"] = "Title",
        ["Year"] = "Year",
        ["Action"] = "Action",
        ["Status"] = "Status",
        ["Movie"] = "Movie",
        ["Movies"] = "movies",
        ["MovieSingular"] = "movie",
        ["Shows"] = "shows",
        ["ShowSingular"] = "show",
        ["Items"] = "items",
        ["ItemSingular"] = "item",
        ["Manage"] = "Manage",
        ["Rename"] = "Rename",
        ["Delete"] = "Delete",
        ["Clear"] = "Clear",
        ["Review"] = "Review",
        ["Ready"] = "Ready",
        ["Local"] = "Local",
        ["OnlineOnly"] = "Online only",
        ["Present"] = "Present",
        ["Filter"] = "Filter",
        ["SortAdded"] = "Sort: Added",
        ["ClearSearch"] = "Clear search",
        ["NoResults"] = "No results",
        ["NoResultsHelp"] = "Try another title, year, or genre.",
        // Library
        ["MoviesTab"] = "Movies",
        ["TvShowsTab"] = "TV Shows",
        ["MediaTypeLabel"] = "Media type",
        ["SearchLibrary"] = "Search your movies",
        ["SearchTvShows"] = "Search your shows",
        ["SeasonAvailableFormat"] = "1 season · {0}/{1} on disk",
        ["SeasonsAvailableFormat"] = "{0} seasons · {1}/{2} on disk",
        ["LoadingLibrary"] = "Loading your library…",
        ["LibraryError"] = "Library could not be loaded",
        ["LibraryErrorHelp"] = "The local library index is unavailable. Try again.",
        ["EmptyLibrary"] = "Your library is empty",
        ["EmptyLibraryHelp"] = "Add media from a local folder to get started.",

        // My Lists
        ["SearchLists"] = "Search this list",
        ["NewList"] = "New List",
        ["ListsCountFormat"] = "{0} lists",
        ["Watchlist"] = "Watchlist",
        ["Favorites"] = "Favorites",
        ["WatchLater"] = "Watch Later",
        ["ReadyToWatch"] = "Ready to Watch",
        ["Liked"] = "Liked",
        ["Blacklisted"] = "Blacklisted",
        ["BestThrillers"] = "Best Thrillers",
        ["ListEmpty"] = "Nothing in this list yet",
        ["ListEmptyHelp"] = "Add a movie from its details page to see it here.",
        ["CustomListEmptyHelp"] = "Your own lists cannot hold items yet: the catalog has no membership table for them.",
        ["RenameList"] = "Rename list",
        ["DeleteList"] = "Delete list",
        ["ListNamePlaceholder"] = "List name",
        // Add Media
        ["TvEpisodesTab"] = "TV Episodes",
        ["AddMoviesFromFolder"] = "Add movies from a folder",
        ["AddMoviesFromFolderHelp"] = "DropSort reads file names only. Nothing is moved until you confirm.",
        ["AddEpisodesFromFiles"] = "Add episodes from files",
        ["AddEpisodesFromFilesHelp"] = "Season and episode numbers are parsed from the file name.",
        ["MediaFolderLabel"] = "Media folder",
        ["FolderPathPlaceholder"] = "Choose a folder to scan",
        ["AddFiles"] = "Add Files",
        ["ScanFolder"] = "Scan Folder",
        ["ScanningFolder"] = "Scanning folder…",
        ["ResetScan"] = "Start Over",
        ["DetectedMovies"] = "Detected movies",
        ["DetectedEpisodes"] = "Detected episodes",
        ["ItemsFoundFormat"] = "{0} items found",
        ["DetectedEpisodesFormat"] = "Season {0} · {1} detected episodes",
        ["AddSelected"] = "Add Selected",
        ["AddEpisodesFormat"] = "Add {0} Episodes",
        ["Quality"] = "Quality",
        ["FindMatch"] = "Find Match",
        ["NeedsReview"] = "Needs review",
        ["SearchTmdbHeading"] = "Search TMDB",
        ["SearchTmdbHelp"] = "Look up a title manually when a file name cannot be matched.",
        ["SearchTmdbPlaceholder"] = "Title and year",
        ["NothingDetected"] = "Nothing detected yet",
        ["NothingDetectedHelp"] = "Pick a folder and run a scan to review detected media.",
        ["ItemsAddedFormat"] = "{0} items added to your library.",
        ["AddAllFormat"] = "Add All ({0})",
        ["EpisodesAddedFormat"] = "{0} episodes added to your library.",
        ["EpisodesSkippedFormat"] = "{0} files were left unresolved:",
        ["EpisodeNoMarkerHelp"] = "no season and episode marker in the name",
        ["EpisodeNoTitleHelp"] = "no show name before the episode marker",
        ["EpisodeRangeHelp"] = "season or episode number out of range",
        ["EpisodeIsMovieFileHelp"] = "already registered as a movie file",
        ["EpisodeOtherOwnerHelp"] = "already registered against another episode",
        ["EmptyShows"] = "No TV shows yet",
        ["EmptyShowsHelp"] = "Add episode files from Add Media to build the hierarchy.",
        ["IssueEpisodeFileMissing"] = "Episode file is missing",
        ["TmdbNotConfiguredHelp"] = "Add a TMDB read access token in Settings to search for titles.",
        // Check Library
        ["CheckDescription"] = "Verify that every registered item still exists on disk.",
        ["LibraryCheck"] = "Library check",
        ["LibraryCheckHelp"] = "Runs locally. No files are changed.",
        ["CheckingLibrary"] = "Checking your library",
        ["CheckingHelp"] = "Manual local verification is in progress.",
        ["CheckedOfFormat"] = "{0} of {1} checked",
        ["CheckComplete"] = "Check complete",
        ["CheckCompleteHelp"] = "Most registered media is healthy.",
        ["CheckHealthyHelp"] = "Every registered item was found on disk.",
        ["CheckCancelledHelp"] = "The check was cancelled. Nothing was changed.",
        ["CheckedCountFormat"] = "{0} checked",
        ["CheckIssuesFormat"] = "{0} of {1} items need attention.",
        ["FilesCheckedFormat"] = "{0} media files checked, {1} missing.",
        ["StartCheck"] = "Start Check",
        ["Passed"] = "Passed",
        ["NeedsAttention"] = "Needs attention",
        ["RunAgain"] = "Run Again",
        ["RunCheck"] = "Run Check",
        ["IssuesHeading"] = "Issues",
        ["NoIssues"] = "No issues found",
        ["NoIssuesHelp"] = "Every registered item was located on disk.",
        ["IssueEpisodeMissing"] = "Episode file is missing",
        ["IssueMovieMissing"] = "Movie file is missing",
        ["IssueMetadataReview"] = "Metadata needs review",
        ["Relink"] = "Relink…",
        ["RelinkTitle"] = "Relink Missing Media",
        ["RelinkConfirmMessage"] = "Are you sure you want to relink this media to the selected file?\n\nOriginal: {0}\nNew: {1}\nSize: {2:N0} bytes",
        ["RelinkSuccess"] = "Media successfully relinked.",

        // Operations Log
        ["EntryFormat"] = "{0} entry",
        ["EntriesFormat"] = "{0} entries",
        ["CopyLog"] = "Copy Log",
        ["OperationMove"] = "File Moved",
        ["OperationRename"] = "File Renamed",
        ["SaveLog"] = "Save Log",
        ["LogSaved"] = "Log saved to your documents folder.",
        ["LogSaveFailed"] = "The log could not be saved: {0}",
        ["NoEntries"] = "No operations recorded",
        ["NoEntriesHelp"] = "Adding or organizing media will appear here.",
        ["LogCopied"] = "Log copied to the clipboard.",
        // Settings
        ["Appearance"] = "Appearance",
        ["Theme"] = "Theme",
        ["ThemeHelpSlate"] = "Slate keeps the dark shell with a cooler surface tint.",
        ["ThemeHelpDark"] = "Dark uses neutral near-black surfaces throughout the app.",
        ["ThemeHelpLight"] = "Light uses bright surfaces with dark text.",
        ["Slate"] = "Slate",
        ["Dark"] = "Dark",
        ["Light"] = "Light",
        ["Language"] = "Language",
        ["LanguageHelp"] = "Arabic switches the whole shell to right-to-left.",
        ["EnglishLanguage"] = "English",
        ["ArabicLanguage"] = "العربية",
        ["LibrarySection"] = "Library",
        ["MovieFolders"] = "Movie folders",
        ["MovieFoldersHelp"] = "Folders scanned when you add movies.",
        ["TvFolders"] = "TV show folders",
        ["TvFoldersHelp"] = "Folders scanned when you add episodes.",
        ["LibraryDataFolder"] = "Library data folder",
        ["LibraryDataFolderHelp"] = "Where the local index and journal are stored.",
        ["OpenFolder"] = "Open Folder",
        ["Tmdb"] = "TMDB",
        ["Connected"] = "Connected",
        ["NotConfigured"] = "Not configured",
        ["TmdbStatusHelp"] = "A read access token enables title lookups and artwork.",
        ["TmdbNoClientHelp"] = "This build has no TMDB client, so a token cannot be verified or used for lookups yet.",
        ["ReadAccessToken"] = "Read access token",
        ["ReadAccessTokenHelp"] = "Stored for this session only unless you save it.",
        ["TmdbTokenPlaceholder"] = "Paste your v4 read access token",
        ["UseForSession"] = "Use for This Session",
        ["TestConnection"] = "Test Connection",
        ["SetupGuide"] = "Setup Guide",
        ["LibraryData"] = "Library data",
        ["ExportData"] = "Export library",
        ["ExportHelp"] = "Writes a portable snapshot of your library index.",
        ["ImportData"] = "Import library",
        ["ImportHelp"] = "Merges a previously exported snapshot.",
        ["SnapshotUnsupportedHelp"] = "Library snapshots are not part of this build. Nothing was written or read.",
        ["WatchEventsRemovedFormat"] = "{0} watch events removed. Preferences and the watchlist were kept.",
        ["LibraryClearedFormat"] = "{0} movies and {1} media files were removed from the index. No media file on disk was deleted.",
        ["HistoryRecovery"] = "History and recovery",
        ["OperationsLogHelp"] = "Review every add, organize and check operation.",
        ["ViewOperationsLog"] = "View Operations Log",
        ["DangerZone"] = "Danger zone",
        ["ClearHistory"] = "Clear watch history",
        ["ClearHistoryHelp"] = "Removes every watch event. Preferences are kept.",
        ["ClearLibraryData"] = "Clear library data",
        ["ClearLibraryDataHelp"] = "Removes the local index. Media files are never deleted.",
        // Movie details
        ["BackToLibrary"] = "Back to Library",
        ["BackToHome"] = "Back to Home",
        ["BackToMyLists"] = "Back to My Lists",
        ["OriginalTitleFormat"] = "Original title: {0}",
        ["Overview"] = "Overview",
        ["PlayMovie"] = "Play Movie",
        ["OrganizeFile"] = "Organize File",
        ["FileDetails"] = "File details",
        ["MediaFile"] = "Media file",
        ["NoLocalFile"] = "No local file",
        ["NoLocalFileHelp"] = "This title is registered without a local media file.",
        ["NoMovieFolderHelp"] = "Set a movies folder in Settings first. DropSort only moves files into an approved root.",
        ["OrganizeConfirmFormat"] = "Move this file into the approved movies folder?\n\nFrom: {0}\n\nTo: {1}",
        ["Organizing"] = "Moving the file…",
        ["OrganizeDone"] = "The file was moved and the library was updated.",
        ["YourActivity"] = "Your Activity",
        ["ActivityHelp"] = "Preferences and watch history stay on this device.",
        ["Preference"] = "Preference",
        ["Like"] = "Like",
        ["Exclude"] = "Exclude",
        ["ClearPreference"] = "Clear",
        ["AddToWatchlist"] = "Add to Watchlist",
        ["InWatchlist"] = "In Watchlist",
        ["Watching"] = "Watching",
        ["MarkWatched"] = "Mark Watched",
        ["WatchedDate"] = "Watched Date",
        ["MarkOnDate"] = "Mark on Date",
        ["PickDate"] = "Pick a date",
        ["WatchHistory"] = "Watch History",
        ["NoWatches"] = "No watches recorded",
        ["NoWatchesHelp"] = "Mark this title watched to build a history.",
        ["RemoveWatchEvent"] = "Remove watch event",
        ["FirstWatch"] = "First watch",
        ["Rewatch"] = "Rewatch",
        ["Watched"] = "Watched",
        ["AddToList"] = "Add to List",

        // TV show details
        ["TvShow"] = "TV Show",
        ["Seasons"] = "Seasons",
        ["SeasonsHelp"] = "Expand a season to review its episodes.",
        ["PlayNextEpisode"] = "Play Next Episode",
        ["MoreOptions"] = "More options",
        ["SeasonFormat"] = "Season {0}",
        ["SeasonMetaFormat"] = "{0} episodes · {1} on disk",
        ["SeasonMetaMissingFormat"] = "{0} episodes · {1} on disk · {2} missing",
        ["EpisodesWatched"] = "episodes watched",
        ["EpisodesOnDisk"] = "episodes on disk",
        ["Episodes"] = "episodes",
        ["PlayEpisode"] = "Play episode",
        ["Missing"] = "Missing",

        // Home
        ["WelcomeBack"] = "Welcome back",
        ["YourLibrary"] = "Your library at a glance.",
        ["StatMovies"] = "Movies",
        ["StatShows"] = "TV shows",
        ["StatEpisodes"] = "Episodes",
        ["StatWatched"] = "Watched",
        ["StatEpisodesOnDisk"] = "On disk",
        ["EpisodesOnDiskFormat"] = "{0} of {1} episodes on disk",
        ["ContinueWatching"] = "Continue Watching",
        ["SeeAll"] = "See all",
        ["RecentlyAdded"] = "Recently Added",
        ["NothingToContinue"] = "Nothing in progress",
        ["NothingToContinueHelp"] = "Start a show and it will show up here.",

        // TMDB matching & metadata
        ["MatchMetadata"] = "Match metadata…",
        ["RefreshMetadata"] = "Refresh metadata",
        ["MatchWithTmdb"] = "Match with TMDB",
        ["SearchTmdb"] = "Search TMDB",
        ["ApplyMatch"] = "Apply Match",
        ["SearchingTmdb"] = "Searching TMDB…",
        ["MatchingProgress"] = "Updating metadata…",
        ["NoMatchesFound"] = "No matches found.",
        ["TmdbNotConfiguredPrompt"] = "TMDB is not configured. Enter your Read Access Token in Settings to enable matching.",
        ["TmdbConnectionSuccess"] = "Successfully connected to TMDB.",
        ["TmdbConnectionFailed"] = "Connection to TMDB failed.",
        ["FixMatch"] = "Fix match…",
    };

    private static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        // Shell
        ["WindowTitle"] = "مكتبة DropSort",
        ["Home"] = "الرئيسية",
        ["Library"] = "المكتبة",
        ["MyLists"] = "قوائمي",
        ["AddMedia"] = "إضافة وسائط",
        ["CheckLibrary"] = "فحص المكتبة",
        ["Settings"] = "الإعدادات",
        ["OperationsLog"] = "سجل العمليات",

        // Shared vocabulary
        ["Search"] = "بحث",
        ["Cancel"] = "إلغاء",
        ["Save"] = "حفظ",
        ["Copy"] = "نسخ",
        ["Refresh"] = "تحديث",
        ["Close"] = "إغلاق",
        ["Confirm"] = "تأكيد",
        ["Remove"] = "إزالة",
        ["More"] = "المزيد",
        ["All"] = "الكل",
        ["Success"] = "ناجحة",
        ["Failed"] = "فاشلة",
        ["Warning"] = "تحذير",
        ["Retry"] = "إعادة المحاولة",
        ["Export"] = "تصدير",
        ["Import"] = "استيراد",
        ["Browse"] = "استعراض",
        ["Folder"] = "مجلد",
        ["Title"] = "العنوان",
        ["Year"] = "السنة",
        ["Action"] = "الإجراء",
        ["Status"] = "الحالة",
        ["Movie"] = "فيلم",
        ["Movies"] = "فيلمًا",
        ["MovieSingular"] = "فيلم",
        ["Shows"] = "مسلسلًا",
        ["ShowSingular"] = "مسلسل",
        ["Items"] = "عنصرًا",
        ["ItemSingular"] = "عنصر واحد",
        ["Manage"] = "إدارة",
        ["Rename"] = "إعادة تسمية",
        ["Delete"] = "حذف",
        ["Clear"] = "مسح",
        ["Review"] = "مراجعة",
        ["Ready"] = "جاهز",
        ["Local"] = "محلي",
        ["OnlineOnly"] = "عبر الإنترنت فقط",
        ["Present"] = "موجود",
        ["Filter"] = "تصفية",
        ["SortAdded"] = "الترتيب: الإضافة",
        ["ClearSearch"] = "مسح البحث",
        ["NoResults"] = "لا توجد نتائج",
        ["NoResultsHelp"] = "جرّب عنوانًا أو سنة أو نوعًا آخر.",
        // Library
        ["MoviesTab"] = "أفلام",
        ["TvShowsTab"] = "مسلسلات",
        ["MediaTypeLabel"] = "نوع الوسائط",
        ["SearchLibrary"] = "ابحث في أفلامك",
        ["SearchTvShows"] = "ابحث في مسلسلاتك",
        ["SeasonAvailableFormat"] = "موسم واحد · {0}/{1} على القرص",
        ["SeasonsAvailableFormat"] = "{0} مواسم · {1}/{2} على القرص",
        ["LoadingLibrary"] = "جارٍ تحميل مكتبتك…",
        ["LibraryError"] = "تعذّر تحميل المكتبة",
        ["LibraryErrorHelp"] = "فهرس المكتبة المحلي غير متاح. أعد المحاولة.",
        ["EmptyLibrary"] = "مكتبتك فارغة",
        ["EmptyLibraryHelp"] = "أضف وسائط من مجلد محلي للبدء.",

        // My Lists
        ["SearchLists"] = "ابحث في هذه القائمة",
        ["NewList"] = "قائمة جديدة",
        ["ListsCountFormat"] = "{0} قوائم",
        ["Watchlist"] = "قائمة المشاهدة",
        ["Favorites"] = "المفضلة",
        ["WatchLater"] = "المشاهدة لاحقًا",
        ["ReadyToWatch"] = "جاهز للمشاهدة",
        ["Liked"] = "أعجبني",
        ["Blacklisted"] = "مستبعد",
        ["BestThrillers"] = "أفضل أفلام الإثارة",
        ["ListEmpty"] = "لا يوجد شيء في هذه القائمة",
        ["ListEmptyHelp"] = "أضف فيلمًا من صفحة تفاصيله ليظهر هنا.",
        ["CustomListEmptyHelp"] = "لا تستوعب قوائمك الخاصة عناصر بعد، فلا يوجد جدول عضوية لها في الفهرس.",
        ["RenameList"] = "إعادة تسمية القائمة",
        ["DeleteList"] = "حذف القائمة",
        ["ListNamePlaceholder"] = "اسم القائمة",

        // Add Media
        ["TvEpisodesTab"] = "حلقات المسلسلات",
        ["AddMoviesFromFolder"] = "أضف أفلامًا من مجلد",
        ["AddMoviesFromFolderHelp"] = "يقرأ DropSort أسماء الملفات فقط، ولا ينقل شيئًا قبل تأكيدك.",
        ["AddEpisodesFromFiles"] = "أضف حلقات من ملفات",
        ["AddEpisodesFromFilesHelp"] = "يُستخرج رقم الموسم والحلقة من اسم الملف.",
        ["MediaFolderLabel"] = "مجلد الوسائط",
        ["FolderPathPlaceholder"] = "اختر مجلدًا للفحص",
        ["AddFiles"] = "إضافة ملفات",
        ["ScanFolder"] = "فحص المجلد",
        ["ScanningFolder"] = "جارٍ فحص المجلد…",
        ["ResetScan"] = "البدء من جديد",
        ["DetectedMovies"] = "الأفلام المكتشفة",
        ["DetectedEpisodes"] = "الحلقات المكتشفة",
        ["ItemsFoundFormat"] = "تم العثور على {0} عنصرًا",
        ["DetectedEpisodesFormat"] = "الموسم {0} · {1} حلقة مكتشفة",
        ["AddSelected"] = "إضافة المحدد",
        ["AddEpisodesFormat"] = "إضافة {0} حلقة",
        ["Quality"] = "الجودة",
        ["FindMatch"] = "إيجاد تطابق",
        ["NeedsReview"] = "يحتاج مراجعة",
        ["SearchTmdbHeading"] = "البحث في TMDB",
        ["SearchTmdbHelp"] = "ابحث عن العنوان يدويًا عند تعذّر مطابقة اسم الملف.",
        ["SearchTmdbPlaceholder"] = "العنوان والسنة",
        ["NothingDetected"] = "لم يُكتشف شيء بعد",
        ["NothingDetectedHelp"] = "اختر مجلدًا وشغّل الفحص لمراجعة الوسائط المكتشفة.",
        ["ItemsAddedFormat"] = "تمت إضافة {0} عنصراً إلى مكتبتك.",
        ["AddAllFormat"] = "إضافة الكل ({0})",
        ["EpisodesAddedFormat"] = "تمت إضافة {0} حلقة إلى مكتبتك.",
        ["EpisodesSkippedFormat"] = "بقي {0} ملفًا بلا تسجيل:",
        ["EpisodeNoMarkerHelp"] = "لا يوجد رقم موسم وحلقة في الاسم",
        ["EpisodeNoTitleHelp"] = "لا يوجد اسم مسلسل قبل رقم الحلقة",
        ["EpisodeRangeHelp"] = "رقم الموسم أو الحلقة خارج النطاق",
        ["EpisodeIsMovieFileHelp"] = "مسجل بالفعل كملف فيلم",
        ["EpisodeOtherOwnerHelp"] = "مسجل بالفعل لحلقة أخرى",
        ["EmptyShows"] = "لا توجد مسلسلات بعد",
        ["EmptyShowsHelp"] = "أضف ملفات حلقات من إضافة وسائط لبناء التسلسل.",
        ["IssueEpisodeFileMissing"] = "ملف الحلقة مفقود",
        ["TmdbNotConfiguredHelp"] = "أضف رمز وصول TMDB من الإعدادات للبحث عن العناوين.",
        // Check Library
        ["CheckDescription"] = "تحقّق من أن كل عنصر مُسجَّل لا يزال موجودًا على القرص.",
        ["LibraryCheck"] = "فحص المكتبة",
        ["LibraryCheckHelp"] = "يعمل محليًا ولا يُغيّر أي ملف.",
        ["CheckingLibrary"] = "جارٍ فحص مكتبتك",
        ["CheckingHelp"] = "التحقق المحلي اليدوي قيد التنفيذ.",
        ["CheckedOfFormat"] = "تم فحص {0} من {1}",
        ["CheckComplete"] = "انتهى الفحص",
        ["CheckCompleteHelp"] = "معظم الوسائط المسجَّلة سليمة.",
        ["CheckHealthyHelp"] = "تم العثور على كل عنصر مسجّل على القرص.",
        ["CheckCancelledHelp"] = "تم إلغاء الفحص، ولم يتغير شيء.",
        ["CheckedCountFormat"] = "تم فحص {0}",
        ["CheckIssuesFormat"] = "{0} من {1} فيلمًا يحتاج انتباهًا.",
        ["FilesCheckedFormat"] = "تم فحص {0} ملفًا، و{1} منها مفقود.",
        ["StartCheck"] = "بدء الفحص",
        ["Passed"] = "ناجح",
        ["NeedsAttention"] = "يحتاج انتباهًا",
        ["RunAgain"] = "إعادة التشغيل",
        ["RunCheck"] = "تشغيل الفحص",
        ["IssuesHeading"] = "المشكلات",
        ["NoIssues"] = "لا توجد مشكلات",
        ["NoIssuesHelp"] = "تم العثور على كل عنصر مسجَّل على القرص.",
        ["IssueEpisodeMissing"] = "ملف الحلقة مفقود",
        ["IssueMovieMissing"] = "ملف الفيلم مفقود",
        ["IssueMetadataReview"] = "البيانات الوصفية تحتاج مراجعة",
        ["Relink"] = "إعادة ربط…",
        ["RelinkTitle"] = "إعادة ربط الوسائط المفقودة",
        ["RelinkConfirmMessage"] = "هل أنت متأكد من رغبتك في إعادة ربط هذا الملف بالملف المحدد؟\n\nالأصلي: {0}\nالجديد: {1}\nالحجم: {2:N0} بايت",
        ["RelinkSuccess"] = "تمت إعادة ربط الوسائط بنجاح.",

        // Operations Log
        ["EntryFormat"] = "سجل واحد",
        ["EntriesFormat"] = "{0} سجلًا",
        ["CopyLog"] = "نسخ السجل",
        ["OperationMove"] = "نُقل ملف",
        ["OperationRename"] = "إعادة تسمية ملف",
        ["SaveLog"] = "حفظ السجل",
        ["LogSaved"] = "تم حفظ السجل في مجلد المستندات.",
        ["LogSaveFailed"] = "تعذر حفظ السجل: {0}",
        ["NoEntries"] = "لا توجد عمليات مسجَّلة",
        ["NoEntriesHelp"] = "ستظهر هنا عمليات إضافة الوسائط وتنظيمها.",
        ["LogCopied"] = "تم نسخ السجل إلى الحافظة.",

        // Settings
        ["Appearance"] = "المظهر",
        ["Theme"] = "النمط",
        ["ThemeHelpSlate"] = "يحافظ نمط Slate على الواجهة الداكنة بلمسة أبرد.",
        ["ThemeHelpDark"] = "يستخدم النمط الداكن أسطحًا سوداء محايدة في التطبيق.",
        ["ThemeHelpLight"] = "يستخدم النمط الفاتح أسطحًا مضيئة مع نص داكن.",
        ["Slate"] = "Slate",
        ["Dark"] = "داكن",
        ["Light"] = "فاتح",
        ["Language"] = "اللغة",
        ["LanguageHelp"] = "تحوّل العربية الواجهة بالكامل إلى اتجاه من اليمين إلى اليسار.",
        ["EnglishLanguage"] = "English",
        ["ArabicLanguage"] = "العربية",
        ["LibrarySection"] = "المكتبة",
        ["MovieFolders"] = "مجلدات الأفلام",
        ["MovieFoldersHelp"] = "المجلدات التي تُفحص عند إضافة الأفلام.",
        ["TvFolders"] = "مجلدات المسلسلات",
        ["TvFoldersHelp"] = "المجلدات التي تُفحص عند إضافة الحلقات.",
        ["LibraryDataFolder"] = "مجلد بيانات المكتبة",
        ["LibraryDataFolderHelp"] = "موضع تخزين الفهرس المحلي وسجل العمليات.",
        ["OpenFolder"] = "فتح المجلد",
        ["Tmdb"] = "TMDB",
        ["Connected"] = "متصل",
        ["NotConfigured"] = "غير مُهيّأ",
        ["TmdbStatusHelp"] = "يتيح رمز القراءة البحث عن العناوين وجلب الصور.",
        ["TmdbNoClientHelp"] = "لا يوجد عميل TMDB في هذه النسخة، لذا لا يمكن التحقق من الرمز أو استخدامه بعد.",
        ["ReadAccessToken"] = "رمز وصول القراءة",
        ["ReadAccessTokenHelp"] = "يُحفظ لهذه الجلسة فقط إن لم تحفظه.",
        ["TmdbTokenPlaceholder"] = "الصق رمز وصول القراءة v4",
        ["UseForSession"] = "استخدام لهذه الجلسة",
        ["TestConnection"] = "اختبار الاتصال",
        ["SetupGuide"] = "دليل الإعداد",
        ["LibraryData"] = "بيانات المكتبة",
        ["ExportData"] = "تصدير المكتبة",
        ["ExportHelp"] = "يكتب نسخة محمولة من فهرس مكتبتك.",
        ["ImportData"] = "استيراد المكتبة",
        ["ImportHelp"] = "يدمج نسخة مُصدَّرة سابقًا.",
        ["SnapshotUnsupportedHelp"] = "نسخ المكتبة غير مُنفّذة في هذه النسخة، ولم يُكتب أو يُقرأ شيء.",
        ["WatchEventsRemovedFormat"] = "تم حذف {0} من أحداث المشاهدة، مع الحفاظ على التفضيلات وقائمة المشاهدة.",
        ["LibraryClearedFormat"] = "تم حذف {0} فيلماً و{1} ملفاً من الفهرس، ولم يُحذف أي ملف من القرص.",
        ["HistoryRecovery"] = "السجل والاستعادة",
        ["OperationsLogHelp"] = "راجع كل عمليات الإضافة والتنظيم والفحص.",
        ["ViewOperationsLog"] = "عرض سجل العمليات",
        ["DangerZone"] = "منطقة الخطر",
        ["ClearHistory"] = "مسح سجل المشاهدة",
        ["ClearHistoryHelp"] = "يحذف كل أحداث المشاهدة مع الحفاظ على التفضيلات.",
        ["ClearLibraryData"] = "مسح بيانات المكتبة",
        ["ClearLibraryDataHelp"] = "يحذف الفهرس المحلي فقط، ولا تُحذف ملفات الوسائط أبدًا.",
        // Movie details
        ["BackToLibrary"] = "العودة إلى المكتبة",
        ["BackToHome"] = "العودة إلى الرئيسية",
        ["BackToMyLists"] = "العودة إلى قوائمي",
        ["OriginalTitleFormat"] = "العنوان الأصلي: {0}",
        ["Overview"] = "نظرة عامة",
        ["PlayMovie"] = "تشغيل الفيلم",
        ["OrganizeFile"] = "تنظيم الملف",
        ["FileDetails"] = "تفاصيل الملف",
        ["MediaFile"] = "ملف الوسائط",
        ["NoLocalFile"] = "لا يوجد ملف محلي",
        ["NoLocalFileHelp"] = "هذا العنوان مُسجَّل بدون ملف وسائط محلي.",
        ["NoMovieFolderHelp"] = "اختر مجلد الأفلام من الإعدادات أولاً، فلا ينقل DropSort الملفات إلا إلى مجلد معتمد.",
        ["OrganizeConfirmFormat"] = "نقل هذا الملف إلى مجلد الأفلام المعتمد؟\n\nمن: {0}\n\nإلى: {1}",
        ["Organizing"] = "جارٍ نقل الملف…",
        ["OrganizeDone"] = "تم نقل الملف وتحديث المكتبة.",
        ["YourActivity"] = "نشاطك",
        ["ActivityHelp"] = "تبقى التفضيلات وسجل المشاهدة على هذا الجهاز.",
        ["Preference"] = "التفضيل",
        ["Like"] = "إعجاب",
        ["Exclude"] = "استبعاد",
        ["ClearPreference"] = "مسح",
        ["AddToWatchlist"] = "إضافة إلى قائمة المشاهدة",
        ["InWatchlist"] = "في قائمة المشاهدة",
        ["Watching"] = "قيد المشاهدة",
        ["MarkWatched"] = "تحديد كمشاهَد",
        ["WatchedDate"] = "تاريخ المشاهدة",
        ["MarkOnDate"] = "تحديد بتاريخ",
        ["PickDate"] = "اختر تاريخًا",
        ["WatchHistory"] = "سجل المشاهدة",
        ["NoWatches"] = "لا توجد مشاهدات مسجَّلة",
        ["NoWatchesHelp"] = "حدّد هذا العنوان كمشاهَد لبناء سجل.",
        ["RemoveWatchEvent"] = "إزالة حدث المشاهدة",
        ["FirstWatch"] = "أول مشاهدة",
        ["Rewatch"] = "إعادة مشاهدة",
        ["Watched"] = "مشاهَد",
        ["AddToList"] = "إضافة إلى قائمة",

        // TV show details
        ["TvShow"] = "مسلسل",
        ["Seasons"] = "المواسم",
        ["SeasonsHelp"] = "وسّع موسمًا لمراجعة حلقاته.",
        ["PlayNextEpisode"] = "تشغيل الحلقة التالية",
        ["MoreOptions"] = "خيارات أخرى",
        ["SeasonFormat"] = "الموسم {0}",
        ["SeasonMetaFormat"] = "{0} حلقة · {1} على القرص",
        ["SeasonMetaMissingFormat"] = "{0} حلقة · {1} على القرص · {2} مفقود",
        ["EpisodesWatched"] = "حلقة مشاهَدة",
        ["EpisodesOnDisk"] = "حلقة على القرص",
        ["Episodes"] = "حلقة",
        ["PlayEpisode"] = "تشغيل الحلقة",
        ["Missing"] = "مفقود",

        // Home
        ["WelcomeBack"] = "مرحبًا بعودتك",
        ["YourLibrary"] = "نظرة سريعة على مكتبتك.",
        ["StatMovies"] = "الأفلام",
        ["StatShows"] = "المسلسلات",
        ["StatEpisodes"] = "الحلقات",
        ["StatWatched"] = "المشاهَد",
        ["StatEpisodesOnDisk"] = "على القرص",
        ["EpisodesOnDiskFormat"] = "{0} من {1} حلقة على القرص",
        ["ContinueWatching"] = "متابعة المشاهدة",
        ["SeeAll"] = "عرض الكل",
        ["RecentlyAdded"] = "أُضيف حديثًا",
        ["NothingToContinue"] = "لا يوجد شيء قيد المشاهدة",
        ["NothingToContinueHelp"] = "ابدأ مسلسلًا وسيظهر هنا.",

        // TMDB matching & metadata
        ["MatchMetadata"] = "مطابقة البيانات…",
        ["RefreshMetadata"] = "تحديث البيانات",
        ["MatchWithTmdb"] = "مطابقة مع TMDB",
        ["SearchTmdb"] = "بحث في TMDB",
        ["ApplyMatch"] = "تطبيق المطابقة",
        ["SearchingTmdb"] = "جاري البحث في TMDB…",
        ["MatchingProgress"] = "جاري تحديث البيانات…",
        ["NoMatchesFound"] = "لم يتم العثور على نتائج مطابقة.",
        ["TmdbNotConfiguredPrompt"] = "لم يتم ضبط TMDB بعد. أدخل رمز القراءة في الإعدادات لتفعيل المطابقة.",
        ["TmdbConnectionSuccess"] = "تم الاتصال بـ TMDB بنجاح.",
        ["TmdbConnectionFailed"] = "فشل الاتصال بـ TMDB.",
        ["FixMatch"] = "تصحيح المطابقة…",
    };

    public static UiLanguage CurrentLanguage { get; private set; } = UiLanguage.English;

    /// <summary>U+202A LEFT-TO-RIGHT EMBEDDING: opens a left-to-right run for a digit group.</summary>
    private const char LeftToRightEmbedding = '\u202A';

    /// <summary>U+202C POP DIRECTIONAL FORMATTING: closes the run opened above.</summary>
    private const char PopDirectionalFormatting = '\u202C';

    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// The culture every date and number in the UI is formatted with.
    /// <para>
    /// This machine's regional format is Arabic, so CultureInfo.CurrentCulture would render an English
    /// UI's dates as "\u0633\u064a\u062a\u0645\u0628\u0631 \u0661, \u0662\u0660\u0662\u0666". Formatting follows the language the user picked in
    /// Settings, not the operating system's region.
    /// </para>
    /// </summary>
    public static CultureInfo Culture => CurrentLanguage == UiLanguage.Arabic
        ? CultureInfo.GetCultureInfo("ar")
        : CultureInfo.GetCultureInfo("en-US");

    public static FlowDirection FlowDirection =>
        CurrentLanguage == UiLanguage.Arabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    /// <summary>
    /// The family the whole shell inherits. Windows' default UI family - Segoe UI Variable Text -
    /// carries no Arabic, so Arabic text was drawn by a fallback face while the line box kept the
    /// Latin metrics of the primary family; that mismatch is what shaved the tops off diacritics.
    /// Arabic therefore runs on Segoe UI, which covers the script itself, and English keeps the
    /// variable ramp with Segoe UI behind it.
    /// </summary>
    public static string FontFamily =>
        CurrentLanguage == UiLanguage.Arabic
            ? "Segoe UI"
            : "Segoe UI Variable Text, Segoe UI";

    public static string Text(string key)
    {
        var source = CurrentLanguage == UiLanguage.Arabic ? Arabic : English;
        return source.TryGetValue(key, out var value)
            ? value
            : English.TryGetValue(key, out value) ? value : key;
    }

    public static string Format(string key, params object[] arguments) =>
        Digits(string.Format(Culture, Text(key), arguments));

    /// <summary>
    /// Every number in the UI is a Western digit in both languages.
    /// <para>
    /// .NET already formats ASCII digits, but text layout does not render them literally: inside a
    /// right-to-left paragraph the digits are contextually substituted with Arabic-Indic digits.
    /// Each digit run is therefore wrapped in an explicit left-to-right run - U+202A ... U+202C -
    /// which is the only marker that suppressed the substitution for every run in a sentence: a
    /// leading U+200E LEFT-TO-RIGHT MARK only reached the first number of the string, and the
    /// U+2066 / U+2069 isolates were ignored outright. Both markers are zero-width and the wrapped
    /// run keeps its position in the sentence, so the Arabic word order is unchanged.
    /// </para>
    /// Every string the UI shows that can contain a number goes through here, so the rule is set
    /// in one place instead of being re-applied per page.
    /// </summary>
    public static string Digits(string? text)
    {
        if (string.IsNullOrEmpty(text) || CurrentLanguage != UiLanguage.Arabic)
        {
            return text ?? string.Empty;
        }

        var builder = new StringBuilder(text.Length + 16);
        var inDigitRun = false;

        foreach (var original in text)
        {
            // A formatter that ran under an Arabic culture can emit Arabic-Indic digits directly;
            // folding them back to ASCII here means one rule covers both that and the contextual
            // substitution the text layout does.
            var character = original switch
            {
                >= '٠' and <= '٩' => (char)('0' + (original - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (original - '۰')),
                _ => original,
            };

            var isDigit = character is >= '0' and <= '9';

            if (isDigit && !inDigitRun)
            {
                builder.Append(LeftToRightEmbedding);
            }
            else if (!isDigit && inDigitRun)
            {
                builder.Append(PopDirectionalFormatting);
            }

            inDigitRun = isDigit;
            builder.Append(character);
        }

        if (inDigitRun)
        {
            builder.Append(PopDirectionalFormatting);
        }

        return builder.ToString();
    }

    /// <summary>Formats a count for display with Western digits in every language.</summary>
    public static string Number(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Applies the stored language at startup without re-writing it to the store.</summary>
    public static void Restore(UiLanguage language)
    {
        CurrentLanguage = language;
    }

    public static void SetLanguage(UiLanguage language)
    {
        if (CurrentLanguage == language)
        {
            return;
        }

        CurrentLanguage = language;
        AppServices.PersistLanguage(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}
