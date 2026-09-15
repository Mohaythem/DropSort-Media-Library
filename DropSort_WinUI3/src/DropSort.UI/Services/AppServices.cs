using DropSort.Application.Contracts;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Discovery;
using DropSort.FileSystem.Inspection;
using DropSort.FileSystem.Operations;
using DropSort.Infrastructure.Persistence;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using DropSort.Infrastructure.Metadata.Cache;
using DropSort.Infrastructure.Metadata.Tmdb;

namespace DropSort.UI.Services;

/// <summary>
/// The application's composition root.
/// <para>
/// One place builds the real V1 stack - SQLite catalog, personal library, settings, file discovery,
/// availability inspection and the operation journal - and hands the views the narrow UI action
/// contracts from <c>DropSort.Application.Contracts</c>. Views never construct a service or touch a
/// repository, so there is exactly one database location and one migration point.
/// </para>
/// <para>
/// Initialization is lazy and never throws into a view: if the database cannot be opened or migrated
/// the pages fall back to their error state and <see cref="InitializationError" /> carries the reason.
/// </para>
/// </summary>
internal static class AppServices
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>
    /// True once the shell has closed. A task that was still running then must not be able to reopen
    /// the database on its way out - that would recreate the write-ahead log the shutdown just
    /// check-pointed away.
    /// </summary>
    private static bool _closed;

    /// <summary>Why the stack is unavailable, or null when it started normally.</summary>
    public static string? InitializationError { get; private set; }

    public static Microsoft.UI.Dispatching.DispatcherQueue? UiDispatcherQueue { get; set; }

    /// <summary>True when every service below is safe to call.</summary>
    public static bool IsAvailable
    {
        get
        {
            Initialize();
            return InitializationError is null;
        }
    }

    /// <summary>Where the local index, journal and settings live. Shown on the Settings page.</summary>
    public static string DataFolder { get; } = ResolveDataFolder();

    private static string ResolveDataFolder()
    {
#if DEBUG
        // Explicit isolated smoke-test profile; never enabled in Release builds.
        var testRoot = Environment.GetEnvironmentVariable("DROPSORT_TEST_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(testRoot))
        {
            if (!Path.IsPathFullyQualified(testRoot))
                throw new InvalidOperationException("The test data root must be an absolute path.");
            return Path.GetFullPath(testRoot);
        }
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropSort");
    }

    public static ILibraryUiActions Library => Require(_library);

    public static IPersonalLibraryUiActions Personal => Require(_personal);

    public static IImportUiActions Import => Require(_import);

    public static IReconciliationUiActions Reconciliation => Require(_reconciliation);

    /// <summary>The TV catalog: the shows grid, one show's hierarchy, and an episode's files.</summary>
    public static ITvLibraryUiActions Tv => Require(_tv);

    public static IOperationHistoryUiActions History => Require(_history);

    public static ISettingsUiActions Settings => Require(_settings);

    public static IOrganizationUiActions Organization => Require(_organization);

    public static IMetadataProvider Metadata => Require(_metadataProvider);

    public static IPosterService Poster => Require(_posterCache);

    public static IPosterCacheMaintenance PosterMaintenance => Require(_posterCache);

    public static MetadataMatchingService Matching => Require(_matching);

    public static TmdbClient TmdbClient => Require(_tmdbClient);

    /// <summary>
    /// The raw settings table, for the UI's own configuration - the scan folders and the user's
    /// custom lists - which the application contracts do not model. Same database, same lifetime.
    /// </summary>
    public static ISettingsRepository SettingsStore => Require(_settingsRepository);

    /// <summary>
    /// The registered files that were not found on disk. Check Library reports these, and a missing
    /// file deliberately stays registered - it is a reported issue, never a deletion.
    /// </summary>
    public static IReadOnlyList<MediaFile> MissingMediaFiles() => Require(_mediaFiles).ListMissing();

    /// <summary>
    /// The theme and language the user last chose, read back through the settings service so the shell
    /// starts where they left it. An unavailable store answers with the defaults.
    /// </summary>
    public static (AppTheme Theme, UiLanguage Language) RestorePreferences()
    {
        try
        {
            if (!IsAvailable)
            {
                return (AppTheme.Slate, UiLanguage.English);
            }

            var theme = Settings.CurrentUiTheme() switch
            {
                DropSort.Domain.Configuration.UiTheme.Dark => AppTheme.Dark,
                DropSort.Domain.Configuration.UiTheme.Light => AppTheme.Light,
                _ => AppTheme.Slate,
            };

            var language = Settings.CurrentUiLanguage() == DropSort.Domain.Configuration.UiLanguage.Arabic
                ? UiLanguage.Arabic
                : UiLanguage.English;

            return (theme, language);
        }
        catch (Exception)
        {
            return (AppTheme.Slate, UiLanguage.English);
        }
    }

    /// <summary>Stores the chosen theme. A settings failure must not undo the user's visible choice.</summary>
    public static void PersistTheme(AppTheme theme)
    {
        try
        {
            if (IsAvailable)
            {
                Settings.SetUiTheme(theme switch
                {
                    AppTheme.Dark => DropSort.Domain.Configuration.UiTheme.Dark,
                    AppTheme.Light => DropSort.Domain.Configuration.UiTheme.Light,
                    _ => DropSort.Domain.Configuration.UiTheme.Slate,
                });
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Stores the chosen language, same rule as <see cref="PersistTheme" />.</summary>
    public static void PersistLanguage(UiLanguage language)
    {
        try
        {
            if (IsAvailable)
            {
                Settings.SetUiLanguage(language == UiLanguage.Arabic
                    ? DropSort.Domain.Configuration.UiLanguage.Arabic
                    : DropSort.Domain.Configuration.UiLanguage.English);
            }
        }
        catch (Exception)
        {
        }
    }

    private static LibraryService? _library;
    private static LibraryService? _personal;
    private static ImportService? _import;
    private static ReconciliationService? _reconciliation;
    private static OperationHistoryService? _history;
    private static SettingsService? _settings;
    private static OrganizationService? _organization;
    private static TvLibraryService? _tv;
    private static SettingsRepository? _settingsRepository;
    private static IMediaFileRepository? _mediaFiles;
    private static DiskPosterCache? _posterCache;
    private static TmdbClient? _tmdbClient;
    private static IMetadataProvider? _metadataProvider;
    private static MetadataMatchingService? _matching;

    /// <summary>
    /// The two objects that own a resource rather than a connection string: the journal store holds a
    /// SQLite connection for the process lifetime and the coordinator wraps it. They are kept here so
    /// <see cref="Shutdown" /> can close them.
    /// </summary>
    private static FileOperationStore? _operationStore;
    private static FileOperationCoordinator? _coordinator;

    /// <summary>When the last failed attempt happened, so a retry does not run on every property read.</summary>
    private static DateTimeOffset _lastFailure = DateTimeOffset.MinValue;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens and migrates the database, then builds the services. Safe to call repeatedly.
    /// <para>
    /// A failure is not final: nothing is marked initialized, the half-built objects are disposed and
    /// the next call - the Library page's Retry button, or simply navigating again - attempts the whole
    /// thing once more. <see cref="RetryDelay" /> keeps that from turning a locked database into an
    /// attempt on every property read. Services are published only once every one of them is built, so
    /// a failed attempt can never leave a partial stack behind.
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            if (_closed || _initialized || DateTimeOffset.UtcNow - _lastFailure < RetryDelay)
            {
                return;
            }

            FileOperationStore? operations = null;
            FileOperationCoordinator? coordinator = null;

            try
            {
                Directory.CreateDirectory(DataFolder);
                var connectionString = "Data Source=" + Path.Combine(DataFolder, "library.db");
                new DatabaseMigrator(connectionString).Migrate();

                var catalogFactory = new CatalogUnitOfWorkFactory(connectionString);
                IMovieRepository movies = new UnitOfWorkMovieRepository(catalogFactory);
                IMediaFileRepository mediaFiles = new UnitOfWorkMediaFileRepository(catalogFactory);
                var personal = new PersonalLibraryRepository(connectionString);
                var settingsRepository = new SettingsRepository(connectionString);
                var maintenance = new LibraryMaintenanceRepository(connectionString);
                operations = new FileOperationStore(connectionString);
                coordinator = new FileOperationCoordinator(operations);
                RecoverInterruptedOperations(operations, coordinator, settingsRepository);

                var posterFolder = Path.Combine(DataFolder, "Posters");
                var posterCache = new DiskPosterCache(posterFolder);
                var settings = new SettingsService(maintenance, posterCache: posterCache, settings: settingsRepository);
                var tmdbClient = new TmdbClient(() => settings.GetTmdbToken());
                var matching = new MetadataMatchingService(catalogFactory, tmdbClient, posterCache);

                var library = new LibraryService(movies, mediaFiles, personal);
                var import = new ImportService(
                    catalogFactory,
                    tmdbClient,
                    new MediaDiscoveryService());
                var reconciliation = new ReconciliationService(mediaFiles, new AvailabilityInspector(), movies);
                var tv = new TvLibraryService(
                    new UnitOfWorkTvShowRepository(catalogFactory),
                    new UnitOfWorkTvSeasonRepository(catalogFactory),
                    new UnitOfWorkTvEpisodeRepository(catalogFactory));
                var history = new OperationHistoryService(operations, coordinator, mediaFiles, movies);
                var organization = new OrganizationService(mediaFiles, coordinator);

                _mediaFiles = mediaFiles;
                _settingsRepository = settingsRepository;
                _operationStore = operations;
                _coordinator = coordinator;
                _library = library;
                _personal = library;
                _import = import;
                _reconciliation = reconciliation;
                _tv = tv;
                _history = history;
                _settings = settings;
                _organization = organization;
                _posterCache = posterCache;
                _tmdbClient = tmdbClient;
                _metadataProvider = tmdbClient;
                _matching = matching;

                InitializationError = null;
                _initialized = true;
            }
            catch (Exception error)
            {
                InitializationError = error.Message;
                _lastFailure = DateTimeOffset.UtcNow;
                coordinator?.Dispose();
                operations?.Dispose();
                Clear();
            }
        }
    }

    /// <summary>
    /// Closes everything this root owns. Called when the shell window closes: the journal store holds
    /// the only long-lived SQLite connection, and while it is open SQLite cannot truncate the
    /// write-ahead log, so the database file stays a stub with a growing -wal beside it. Clearing the
    /// connection pool afterwards releases the per-call connections too, which is what actually lets
    /// the last close check-point and remove the -wal and -shm files.
    /// </summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _closed = true;
            _coordinator?.Dispose();
            _operationStore?.Dispose();
            Clear();
            _initialized = false;
            InitializationError = "The application is shutting down.";
            DatabaseBootstrap.ReleasePooledConnections();
        }
    }

    /// <summary>Drops every published service. The caller holds <see cref="Gate" />.</summary>
    private static void Clear()
    {
        _library = null;
        _personal = null;
        _import = null;
        _reconciliation = null;
        _tv = null;
        _history = null;
        _settings = null;
        _organization = null;
        _settingsRepository = null;
        _mediaFiles = null;
        _posterCache?.Dispose();
        _posterCache = null;
        _tmdbClient?.Dispose();
        _tmdbClient = null;
        _metadataProvider = null;
        _matching = null;
        _operationStore = null;
        _coordinator = null;
    }

    private static void RecoverInterruptedOperations(
        IFileOperationStore operations,
        IFileOperationCoordinator coordinator,
        ISettingsRepository settings)
    {
        try
        {
            var nonterminal = operations.ListNonterminal();
            if (nonterminal.Count == 0) return;

            var approvedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DataFolder };
            try
            {
                var movieFolder = settings.Get("ui_movie_folder");
                if (!string.IsNullOrEmpty(movieFolder) && Directory.Exists(movieFolder))
                    approvedRoots.Add(movieFolder);
                var showFolder = settings.Get("ui_show_folder");
                if (!string.IsNullOrEmpty(showFolder) && Directory.Exists(showFolder))
                    approvedRoots.Add(showFolder);
            }
            catch { }

            foreach (var op in nonterminal)
            {
                try
                {
                    var opRoots = new HashSet<string>(approvedRoots, StringComparer.OrdinalIgnoreCase);
                    var sourceParent = Path.GetDirectoryName(op.Source);
                    if (!string.IsNullOrEmpty(sourceParent) && Directory.Exists(sourceParent))
                        opRoots.Add(sourceParent);
                    var destParent = Path.GetDirectoryName(op.Destination);
                    if (!string.IsNullOrEmpty(destParent) && Directory.Exists(destParent))
                        opRoots.Add(destParent);

                    coordinator.Recover(op.Id, opRoots.ToArray());
                }
                catch
                {
                    // Interrupted recovery on one operation must not prevent recovering others
                }
            }
        }
        catch
        {
            // Interrupted recovery pass must never crash application startup
        }
    }

    /// <summary>
    /// Detects stale .dropsort-*.tmp files across approved directories without automatic deletion.
    /// </summary>
    public static IReadOnlyList<DropSort.Domain.Core.Operations.StaleTempFileInfo> DetectStaleTempFiles(IEnumerable<string>? extraRoots = null)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DataFolder };
        try
        {
            if (_settingsRepository is not null)
            {
                var movieFolder = _settingsRepository.Get("ui_movie_folder");
                if (!string.IsNullOrEmpty(movieFolder) && Directory.Exists(movieFolder))
                    roots.Add(movieFolder);
                var showFolder = _settingsRepository.Get("ui_show_folder");
                if (!string.IsNullOrEmpty(showFolder) && Directory.Exists(showFolder))
                    roots.Add(showFolder);
            }
        }
        catch { }
        if (extraRoots is not null)
        {
            foreach (var root in extraRoots)
            {
                if (Directory.Exists(root)) roots.Add(root);
            }
        }
        return DropSort.FileSystem.Inspection.CrashTempFileInspector.FindStaleTempFiles(roots);
    }

    private static T Require<T>(T? service)
        where T : class
    {
        Initialize();
        return service ?? throw new InvalidOperationException(
            InitializationError ?? "The application services are unavailable.");
    }
}

/// <summary>
/// The metadata provider used until a TMDB read token is configured. It answers honestly - no
/// candidates, no metadata - instead of inventing results, so "Find Match" and the TMDB search
/// report an empty result rather than fabricating one. Registration and organization never need it.
/// </summary>
internal sealed class UnconfiguredMetadataProvider : IMetadataProvider
{
    public string ProviderName => "unconfigured";

    public bool IsConfigured => false;

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectionTestResult(false, "TMDB read token is not configured.", 401));

    public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];

    public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MovieCandidate>>([]);

    public MovieMetadata? GetMovie(string externalId) => null;

    public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult<MovieMetadata?>(null);

    public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TvCandidate>>([]);

    public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TvShowMetadata?>(null);

    public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult<TvSeasonMetadata?>(null);
}
