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
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;

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

    /// <summary>Why the stack is unavailable, or null when it started normally.</summary>
    public static string? InitializationError { get; private set; }

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
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DropSort");

    public static ILibraryUiActions Library => Require(_library);

    public static IPersonalLibraryUiActions Personal => Require(_personal);

    public static IImportUiActions Import => Require(_import);

    public static IReconciliationUiActions Reconciliation => Require(_reconciliation);

    public static IOperationHistoryUiActions History => Require(_history);

    public static ISettingsUiActions Settings => Require(_settings);

    public static IOrganizationUiActions Organization => Require(_organization);

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
    private static SettingsRepository? _settingsRepository;
    private static IMediaFileRepository? _mediaFiles;

    /// <summary>Opens and migrates the database, then builds the services. Safe to call repeatedly.</summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            try
            {
                Directory.CreateDirectory(DataFolder);
                var connectionString = "Data Source=" + Path.Combine(DataFolder, "library.db");
                new DatabaseMigrator(connectionString).Migrate();

                var catalogFactory = new CatalogUnitOfWorkFactory(connectionString);
                IMovieRepository movies = new UnitOfWorkMovieRepository(catalogFactory);
                IMediaFileRepository mediaFiles = new UnitOfWorkMediaFileRepository(catalogFactory);
                _mediaFiles = mediaFiles;
                var personal = new PersonalLibraryRepository(connectionString);
                var settingsRepository = new SettingsRepository(connectionString);
                _settingsRepository = settingsRepository;
                var maintenance = new LibraryMaintenanceRepository(connectionString);
                var operations = new FileOperationStore(connectionString);
                var coordinator = new FileOperationCoordinator(operations);

                var library = new LibraryService(movies, mediaFiles, personal);
                _library = library;
                _personal = library;
                _import = new ImportService(
                    catalogFactory,
                    new UnconfiguredMetadataProvider(),
                    new MediaDiscoveryService());
                _reconciliation = new ReconciliationService(mediaFiles, new AvailabilityInspector(), movies);
                _history = new OperationHistoryService(operations, coordinator, mediaFiles, movies);
                _settings = new SettingsService(maintenance, posterCache: null, settings: settingsRepository);
                _organization = new OrganizationService(mediaFiles, coordinator);
            }
            catch (Exception error)
            {
                InitializationError = error.Message;
            }
        }
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

    public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];

    public MovieMetadata? GetMovie(string externalId) => null;
}
