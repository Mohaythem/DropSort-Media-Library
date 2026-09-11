using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Configuration;

namespace DropSort.Application.Services;

public sealed class SettingsService : ISettingsUiActions
{
    private readonly ILibraryMaintenanceRepository _maintenanceRepository;
    private readonly IPosterCacheMaintenance? _posterCache;
    private readonly ISettingsRepository? _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _clearGate = new();
    private string? _tmdbToken;

    public SettingsService(
        ILibraryMaintenanceRepository maintenanceRepository,
        IPosterCacheMaintenance? posterCache = null,
        ISettingsRepository? settings = null,
        string? initialTmdbToken = null,
        Func<DateTimeOffset>? clock = null)
    {
        _maintenanceRepository = maintenanceRepository;
        _posterCache = posterCache;
        _settings = settings;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var token = string.IsNullOrWhiteSpace(initialTmdbToken)
            ? _settings?.Get("tmdb_read_access_token")
            : initialTmdbToken.Trim();
        _tmdbToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    public bool IsTmdbConfigured() => _tmdbToken is not null;

    public bool ApplyTmdbSessionToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        _tmdbToken = token.Trim();
        _settings?.Set("tmdb_read_access_token", _tmdbToken, _clock());
        return true;
    }

    public bool ClearTmdbSessionToken()
    {
        var changed = _tmdbToken is not null;
        _tmdbToken = null;
        _settings?.Delete("tmdb_read_access_token");
        return changed;
    }

    public string? GetTmdbToken() => _tmdbToken;

    public Task<ConnectionTestResult> TestTmdbConnectionAsync(IMetadataProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.TestConnectionAsync(cancellationToken);
    }

    public ClearLibraryDataResult ClearLibraryData()
    {
        if (!Monitor.TryEnter(_clearGate))
            throw new InvalidOperationException("DropSort is busy with another catalog clear.");
        try
        {
            var counts = _maintenanceRepository.ClearCatalog();
            var posterFiles = 0;
            string? warning = null;
            try
            {
                posterFiles = _posterCache?.Clear() ?? 0;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                warning = "POSTER_CACHE_CLEANUP_FAILED";
            }
            return new ClearLibraryDataResult(
                counts.Movies,
                counts.MediaFiles,
                counts.MetadataEntries,
                posterFiles,
                warning,
                counts.Shows,
                counts.Seasons,
                counts.Episodes);
        }
        finally
        {
            Monitor.Exit(_clearGate);
        }
    }

    public UiLanguage CurrentUiLanguage() => (_settings?.Get("ui_language")) switch
    {
        "ar" => UiLanguage.Arabic,
        _ => UiLanguage.English
    };

    public UiLanguage SetUiLanguage(UiLanguage language)
    {
        if (!Enum.IsDefined(language)) throw new ArgumentOutOfRangeException(nameof(language));
        _settings?.Set("ui_language", language == UiLanguage.Arabic ? "ar" : "en", _clock());
        return language;
    }

    public UiTheme CurrentUiTheme() => (_settings?.Get("ui_theme")) switch
    {
        "dark" or "charcoal" => UiTheme.Dark,
        "light" or "light_blue" => UiTheme.Light,
        "slate" or "main" or "deep_ink" => UiTheme.Slate,
        _ => UiTheme.Slate
    };

    public UiTheme SetUiTheme(UiTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        var normalized = theme == UiTheme.Main ? UiTheme.Slate : theme;
        _settings?.Set("ui_theme", normalized.ToString().ToLowerInvariant(), _clock());
        return normalized;
    }
}
