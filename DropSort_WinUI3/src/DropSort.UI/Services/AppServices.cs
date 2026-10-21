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
using DropSort.Infrastructure.Security;
using DropSort.Application.Trakt;
using DropSort.Infrastructure.Trakt;
using System.Text.Json;

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
        // Opt-in data-root override for QA, scale testing and support of the installed Release build.
        // It is inert unless DROPSORT_TEST_DATA_ROOT is set to an absolute path, and it never changes the
        // default per-user location. It lets an installed build run against an isolated, disposable data
        // profile without touching the user's real %LOCALAPPDATA%\DropSort library.
        var testRoot = Environment.GetEnvironmentVariable("DROPSORT_TEST_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(testRoot))
        {
            if (!Path.IsPathFullyQualified(testRoot))
                throw new InvalidOperationException("The test data root must be an absolute path.");
            return Path.GetFullPath(testRoot);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropSort");
    }

    public static ILibraryUiActions Library => Require(_library);

    public static long CatalogRevision => _catalogFactory?.Revision ?? 0;

    private static CatalogUnitOfWorkFactory? _catalogFactory;

    public static IPersonalLibraryUiActions Personal => Require(_personal);

    public static IWatchHistoryUiActions WatchHistory => Require(_watchHistory);

    public static IImportUiActions Import => Require(_import);

    public static IReconciliationUiActions Reconciliation => Require(_reconciliation);

    /// <summary>The TV catalog: the shows grid, one show's hierarchy, and an episode's files.</summary>
    public static ITvLibraryUiActions Tv => Require(_tv);

    /// <summary>Manual catalog repair: move a movie into a series, or remove an item (file kept on disk).</summary>
    public static ILibraryRepairUiActions Repair => Require(_repair);

    public static MediaFileActions MediaFileActions => new(Require(_availabilityInspector));

    public static ViewingActivityService ViewingActivity => Require(_viewingActivity);
    public static ShowPersonalLibraryService ShowPersonal => Require(_showPersonal);

    public static Task<FileActionResult> PlayMediaFileAsync(string path, CancellationToken token = default) =>
        new TrackedPlaybackActions(MediaFileActions, playedPath => ViewingActivity.RecordSuccessfulPlay(playedPath)).OpenFileAsync(path, token);

    public static IOperationHistoryUiActions History => Require(_history);

    public static ISettingsUiActions Settings => Require(_settings);

    public static IOrganizationUiActions Organization => Require(_organization);


    public static IMetadataProvider Metadata => Require(_metadataProvider);

    public static IPosterService Poster => Require(_posterCache);

    public static IPosterCacheMaintenance PosterMaintenance => Require(_posterCache);

    public static MetadataMatchingService Matching => Require(_matching);

    public static TmdbClient TmdbClient => Require(_tmdbClient);

    #region Trakt Composition Accessors

    public static ITraktUiActions? Trakt => _trakt;
    private static ITraktUiActions? _trakt;

    public static (string ClientId, string RedirectUri) TraktConfig
    {
        get
        {
            var store = _settingsRepository;
            if (store is null) return (string.Empty, string.Empty);
            try
            {
                var json = store.Get("trakt_api_config");
                if (string.IsNullOrWhiteSpace(json)) return (string.Empty, string.Empty);
                var dto = JsonSerializer.Deserialize<TraktConfigDto>(json, TraktJsonOptions);
                if (dto is not null && !string.IsNullOrWhiteSpace(dto.ClientId) && !string.IsNullOrWhiteSpace(dto.RedirectUri))
                {
                    return (dto.ClientId.Trim(), dto.RedirectUri.Trim());
                }
            }
            catch
            {
            }
            return (string.Empty, string.Empty);
        }
    }

    public static ITraktLocalSnapshotBuilder? TraktLocalSnapshot => _traktLocalSnapshot;
    private static ITraktLocalSnapshotBuilder? _traktLocalSnapshot;

    public static ITraktProjectionService? TraktProjection => _traktProjection;
    private static ITraktProjectionService? _traktProjection;

    private static TraktAuthClient? _traktAuthClient;
    private static TraktDataClient? _traktDataClient;
    private static TraktSyncService? _traktSyncService;
    private static readonly SemaphoreSlim TraktReconfigGate = new(1, 1);
    private static readonly JsonSerializerOptions TraktJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record TraktConfigDto(string ClientId, string RedirectUri);

    #endregion

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
    /// Whether the storage root that holds <paramref name="path" /> is currently reachable. A registered
    /// file keeps its persisted PRESENT status while its drive is disconnected (the availability contract
    /// never persists a false MISSING); the UI calls this off the UI thread to avoid presenting such a
    /// file as affirmatively available. Returns true when the store is unavailable, so nothing is
    /// downgraded before the library is ready.
    /// </summary>
    public static (int Season, int Episode)? InferSeasonEpisode(string? fileNameOrPath) =>
        MediaDiscoveryService.InferSeasonEpisode(fileNameOrPath);

    public static bool IsStorageAvailable(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var inspector = _availabilityInspector;
        var available = inspector is null || inspector.IsStorageAvailable(path);
        token.ThrowIfCancellationRequested();
        return available;
    }

    /// <summary>
    /// The subset of <paramref name="paths" /> whose storage root is currently unreachable. Availability
    /// is probed once per distinct root, so many media files on one drive cost one probe. Safe to call
    /// off the UI thread. Returns an empty set when the library is not ready, so nothing is downgraded
    /// before startup completes. This is the shared availability-truth primitive for list surfaces.
    /// </summary>
    public static IReadOnlySet<string> UnavailableStoragePaths(IEnumerable<string> paths, CancellationToken token = default)
    {
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        var inspector = _availabilityInspector;
        if (inspector is null) return unavailable;

        var rootAvailable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) continue;
            var root = StorageRootKey(path);
            if (!rootAvailable.TryGetValue(root, out var available))
            {
                available = inspector.IsStorageAvailable(path);
                token.ThrowIfCancellationRequested();
                rootAvailable[root] = available;
            }

            if (!available) unavailable.Add(path);
        }

        return unavailable;
    }

    private static string StorageRootKey(string path)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? path : root;
        }
        catch
        {
            return path;
        }
    }

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
    private static WatchHistoryService? _watchHistory;
    private static ViewingActivityService? _viewingActivity;
    private static ShowPersonalLibraryService? _showPersonal;
    private static ImportService? _import;
    private static ReconciliationService? _reconciliation;
    private static OperationHistoryService? _history;
    private static SettingsService? _settings;
    private static OrganizationService? _organization;
    private static TvLibraryService? _tv;
    private static SettingsRepository? _settingsRepository;
    private static IMovieRepository? _movies;
    private static IMediaFileRepository? _mediaFiles;
    private static ITvShowRepository? _tvShows;
    private static ITvSeasonRepository? _tvSeasons;
    private static ITvEpisodeRepository? _tvEpisodes;
    private static string? _connectionString;
    private static IAvailabilityInspector? _availabilityInspector;
    private static DiskPosterCache? _posterCache;
    private static TmdbClient? _tmdbClient;
    private static IMetadataProvider? _metadataProvider;
    private static MetadataMatchingService? _matching;
    private static ILibraryRepairUiActions? _repair;

    /// <summary>
    /// The two objects that own a resource rather than a connection string: the journal store holds a
    /// SQLite connection for the process lifetime and the coordinator wraps it. They are kept here so
    /// <see cref="Shutdown" /> can close them.
    /// </summary>
    private static FileOperationStore? _operationStore;
    private static FileOperationCoordinator? _coordinator;

    /// <summary>
    /// The process-wide owner of this library database. It is taken before migration or recovery can
    /// touch the file, so a second DropSort process cannot run the destructive startup path against the
    /// same database. Held for the session and released in <see cref="Shutdown" />.
    /// </summary>
    private static LibraryInstanceGuard? _instanceGuard;

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
    private static string LibraryConnectionString() => "Data Source=" + Path.Combine(DataFolder, "library.db");

    /// <summary>
    /// Claims process-wide ownership of the library before any window is shown. Returns false when
    /// another DropSort process already owns it, so the caller can refuse to open a second, empty
    /// window instead of building one that can never load data. Acquires the same guard
    /// <see cref="Initialize" /> would, so a claimed instance initializes normally afterwards.
    /// </summary>
    public static bool TryClaimSingleInstance()
    {
        lock (Gate)
        {
            if (_closed) return false;
            if (_instanceGuard is not null) return true;
            Directory.CreateDirectory(DataFolder);
            _instanceGuard = LibraryInstanceGuard.TryAcquire(LibraryConnectionString());
            return _instanceGuard is not null;
        }
    }

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
                var connectionString = LibraryConnectionString();
                _connectionString = connectionString;

                // Become the single owner of this library before anything touches it. If another DropSort
                // process already owns it, do not migrate or recover: a second recovery pass over the same
                // journal can mark an in-flight file operation Failed while the first process is still
                // copying. This is a throttled, non-final refusal; the next attempt succeeds once the
                // other process releases the library.
                _instanceGuard ??= LibraryInstanceGuard.TryAcquire(connectionString);
                if (_instanceGuard is null)
                {
                    InitializationError = LocalizationService.Text("LibraryInUse");
                    _lastFailure = DateTimeOffset.UtcNow;
                    return;
                }

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
                var credentialStore = new WindowsTmdbCredentialStore(
                    isolatedProfileRoot: Environment.GetEnvironmentVariable("DROPSORT_TEST_DATA_ROOT"));
                var settings = new SettingsService(maintenance, posterCache: posterCache,
                    settings: settingsRepository, credentialStore: credentialStore);
                var tmdbClient = new TmdbClient(() => settings.GetTmdbToken(),
                    authenticationResult: settings.RecordTmdbAuthenticationResult);
                var matching = new MetadataMatchingService(catalogFactory, tmdbClient, posterCache);

                var library = new LibraryService(movies, mediaFiles, personal);
                var import = new ImportService(
                    catalogFactory,
                    tmdbClient,
                    new MediaDiscoveryService(),
                    matching: matching,
                    fileValidator: new ImportFileValidator());
                var availabilityInspector = new AvailabilityInspector();
                var reconciliation = new ReconciliationService(mediaFiles, availabilityInspector, movies);
                var tvShows = new UnitOfWorkTvShowRepository(catalogFactory);
                var tvSeasons = new UnitOfWorkTvSeasonRepository(catalogFactory);
                var tvEpisodes = new UnitOfWorkTvEpisodeRepository(catalogFactory);
                var tv = new TvLibraryService(
                    tvShows,
                    tvSeasons,
                    tvEpisodes,
                    personal,
                    tmdbClient);
                var watchHistory = new WatchHistoryService(personal);
                var history = new OperationHistoryService(operations, coordinator, mediaFiles, movies);
                var organization = new OrganizationService(mediaFiles, coordinator);
                var repair = new LibraryRepairService(catalogFactory, fileOperations: operations);
                _movies = movies;
                _mediaFiles = mediaFiles;
                _tvShows = tvShows;
                _tvSeasons = tvSeasons;
                _tvEpisodes = tvEpisodes;
                _availabilityInspector = availabilityInspector;
                _settingsRepository = settingsRepository;
                _operationStore = operations;
                _coordinator = coordinator;
                _library = library;
                _catalogFactory = catalogFactory;
                _personal = library;
                _watchHistory = watchHistory;
                _viewingActivity = new ViewingActivityService(new ViewingActivityRepository(connectionString));
                _showPersonal = new ShowPersonalLibraryService(new ShowPersonalLibraryRepository(connectionString), tv);
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
                _repair = repair;

                TryRestoreTraktOnStartup(settingsRepository, connectionString, movies, mediaFiles, tvShows, tvSeasons, tvEpisodes, availabilityInspector);

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
            _traktSyncService?.Dispose();
            _traktAuthClient?.Dispose();
            _traktDataClient?.Dispose();
            _coordinator?.Dispose();
            _operationStore?.Dispose();
            Clear();
            _initialized = false;
            InitializationError = "The application is shutting down.";
            DatabaseBootstrap.ReleasePooledConnections();

            // Release library ownership last, after every database handle this process holds is closed
            // and the connection pool is cleared. A second process must not be able to acquire the guard
            // and start migration or recovery while this process still has open handles to library.db.
            _instanceGuard?.Dispose();
            _instanceGuard = null;
        }
    }

    /// <summary>Drops every published service. The caller holds <see cref="Gate" />.</summary>
    private static void Clear()
    {
        _traktSyncService?.Dispose();
        _traktSyncService = null;
        _trakt = null;
        _traktAuthClient?.Dispose();
        _traktAuthClient = null;
        _traktDataClient?.Dispose();
        _traktDataClient = null;
        _traktLocalSnapshot = null;
        _traktProjection = null;
        _movies = null;
        _tvShows = null;
        _tvSeasons = null;
        _tvEpisodes = null;
        _connectionString = null;
        _library = null;
        _catalogFactory = null;
        _personal = null;
        _watchHistory = null;
        _viewingActivity = null;
        _showPersonal = null;
        _import = null;
        _reconciliation = null;
        _tv = null;
        _history = null;
        _settings = null;
        _organization = null;
        _settingsRepository = null;
        _mediaFiles = null;
        _availabilityInspector = null;
        _posterCache?.Dispose();
        _posterCache = null;
        _tmdbClient?.Dispose();
        _tmdbClient = null;
        _metadataProvider = null;
        _matching = null;
        _repair = null;
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
                if (!string.IsNullOrEmpty(movieFolder) && !NetworkReadGuard.IsNetworkPath(movieFolder) && Directory.Exists(movieFolder))
                    approvedRoots.Add(movieFolder);
                var showFolder = settings.Get("ui_show_folder");
                if (!string.IsNullOrEmpty(showFolder) && !NetworkReadGuard.IsNetworkPath(showFolder) && Directory.Exists(showFolder))
                    approvedRoots.Add(showFolder);
            }
            catch { }

            foreach (var op in nonterminal)
            {
                // Startup runs on the UI thread. Keep network journals pending for explicit recovery;
                // never start a potentially blocked network mutation while initializing the shell.
                if (NetworkReadGuard.IsNetworkPath(op.Source) || NetworkReadGuard.IsNetworkPath(op.Destination))
                    continue;
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
    /// Persists nonsecret Trakt configuration and instantiates the single Trakt service graph.
    /// If an active coordinator is already running under previous configuration, it is disconnected
    /// before instantiating the new graph.
    /// </summary>
    public static async Task ConfigureTraktAsync(string clientId, string redirectUri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        var candidateConfig = new TraktAuthConfig(clientId.Trim(), redirectUri.Trim());
        if (!IsAvailable) throw new InvalidOperationException("The local library is unavailable.");

        await TraktReconfigGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        TraktAuthClient? candidateAuth = null;
        TraktDataClient? candidateData = null;
        TraktSyncService? candidateSync = null;
        var published = false;
        try
        {
            ITraktUiActions? oldCoordinator;
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(_closed, typeof(AppServices));
                oldCoordinator = _trakt;
            }
            if (oldCoordinator is not null)
                await oldCoordinator.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var connectionString = _connectionString ?? throw new InvalidOperationException("The local library is unavailable.");
            var cache = new TraktCacheRepository(connectionString);
            var credentials = new WindowsTraktCredentialStore(Environment.GetEnvironmentVariable("DROPSORT_TEST_DATA_ROOT"));
            candidateAuth = new TraktAuthClient();
            var auth = new TraktAuthService(candidateAuth, credentials, candidateConfig);
            candidateData = new TraktDataClient();
            candidateSync = new TraktSyncService(auth, candidateData, cache, candidateConfig, Require(_settingsRepository));
            var snapshot = new TraktLocalSnapshotBuilder(Require(_movies), Require(_mediaFiles),
                Require(_tvShows), Require(_tvSeasons), Require(_tvEpisodes), Require(_availabilityInspector));
            var projection = new TraktProjectionService();
            var json = JsonSerializer.Serialize(new TraktConfigDto(candidateConfig.ClientId, candidateConfig.RedirectUri));

            TraktSyncService? oldSync;
            TraktAuthClient? oldAuth;
            TraktDataClient? oldData;
            lock (Gate)
            {
                ObjectDisposedException.ThrowIf(_closed, typeof(AppServices));
                cancellationToken.ThrowIfCancellationRequested();
                Require(_settingsRepository).Set("trakt_api_config", json, DateTimeOffset.UtcNow);
                oldSync = _traktSyncService;
                oldAuth = _traktAuthClient;
                oldData = _traktDataClient;
                _traktSyncService = candidateSync;
                _trakt = candidateSync;
                _traktAuthClient = candidateAuth;
                _traktDataClient = candidateData;
                _traktLocalSnapshot = snapshot;
                _traktProjection = projection;
                published = true;
            }
            oldSync?.Dispose();
            oldAuth?.Dispose();
            oldData?.Dispose();
        }
        finally
        {
            if (!published)
            {
                candidateSync?.Dispose();
                candidateAuth?.Dispose();
                candidateData?.Dispose();
            }
            TraktReconfigGate.Release();
        }
    }
    private static void TryRestoreTraktOnStartup(
        ISettingsRepository settingsRepository,
        string connectionString,
        IMovieRepository movies,
        IMediaFileRepository mediaFiles,
        ITvShowRepository tvShows,
        ITvSeasonRepository tvSeasons,
        ITvEpisodeRepository tvEpisodes,
        IAvailabilityInspector availabilityInspector)
    {
        TraktAuthClient? startupAuth = null;
        TraktDataClient? startupData = null;
        TraktSyncService? startupSync = null;
        try
        {
            var configJson = settingsRepository.Get("trakt_api_config");
            if (string.IsNullOrWhiteSpace(configJson)) return;

            var dto = JsonSerializer.Deserialize<TraktConfigDto>(configJson, TraktJsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.ClientId) || string.IsNullOrWhiteSpace(dto.RedirectUri))
                return;

            var trimmedClientId = dto.ClientId.Trim();
            var trimmedRedirectUri = dto.RedirectUri.Trim();

            if (!Uri.TryCreate(trimmedRedirectUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return;

            var authConfig = new TraktAuthConfig(trimmedClientId, trimmedRedirectUri);
            var cacheRepo = new TraktCacheRepository(connectionString);
            var credentialStore = new WindowsTraktCredentialStore(
                isolatedProfileRoot: Environment.GetEnvironmentVariable("DROPSORT_TEST_DATA_ROOT"));
            var authClient = startupAuth = new TraktAuthClient();
            var authService = new TraktAuthService(authClient, credentialStore, authConfig);
            var dataClient = startupData = new TraktDataClient();
            var syncService = startupSync = new TraktSyncService(authService, dataClient, cacheRepo, authConfig, settingsRepository);

            _traktAuthClient = authClient;
            _traktDataClient = dataClient;
            _traktSyncService = syncService;
            _trakt = syncService;

            _traktLocalSnapshot = new TraktLocalSnapshotBuilder(
                movies,
                mediaFiles,
                tvShows,
                tvSeasons,
                tvEpisodes,
                availabilityInspector);
            _traktProjection = new TraktProjectionService();
        }
        catch
        {
            // Dispose locally allocated clients even if construction failed before publication.
            startupSync?.Dispose();
            startupAuth?.Dispose();
            startupData?.Dispose();
            // Trakt initialization failure must not make local Library startup fail
            _traktSyncService?.Dispose();
            _traktSyncService = null;
            _trakt = null;
            _traktAuthClient?.Dispose();
            _traktAuthClient = null;
            _traktDataClient?.Dispose();
            _traktDataClient = null;
            _traktLocalSnapshot = null;
            _traktProjection = null;
        }
    }

    /// <summary>
    /// Detects stale .dropsort-*.tmp files across approved directories without automatic deletion.
    /// </summary>
    public static IReadOnlyList<DropSort.Domain.Core.Operations.StaleTempFileInfo> DetectStaleTempFiles(IEnumerable<string>? extraRoots = null, CancellationToken token = default)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DataFolder };
        try
        {
            if (_settingsRepository is not null)
            {
                var configured = _settingsRepository.Get("ui_media_folders");
                if (configured is not null)
                    foreach (var configuredRoot in configured.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        roots.Add(configuredRoot);
                var movieFolder = _settingsRepository.Get("ui_movie_folder");
                if (!string.IsNullOrEmpty(movieFolder))
                    roots.Add(movieFolder);
                var showFolder = _settingsRepository.Get("ui_show_folder");
                if (!string.IsNullOrEmpty(showFolder))
                    roots.Add(showFolder);
            }
        }
        catch { }
        if (extraRoots is not null)
        {
            foreach (var root in extraRoots)
            {
                if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
            }
        }
        return DropSort.FileSystem.Inspection.CrashTempFileInspector.FindStaleTempFiles(roots, () => token.IsCancellationRequested);
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
