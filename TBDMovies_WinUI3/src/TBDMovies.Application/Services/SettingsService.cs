using TBDMovies.Application.Contracts;
using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Repositories;
using TBDMovies.Domain.Configuration;

namespace TBDMovies.Application.Services;

public sealed class SettingsService : ISettingsUiActions
{
    private readonly ILibraryMaintenanceRepository _maintenanceRepository;
    private readonly IPosterCacheMaintenance? _posterCache;
    private readonly ISettingsRepository? _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ITmdbCredentialStore? _credentialStore;
    private readonly object _clearGate = new();
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    private readonly object _credentialStateGate = new();
    private string? _tmdbToken;
    private TmdbCredentialState _tmdbState = TmdbCredentialState.NotConfigured;

    public SettingsService(ILibraryMaintenanceRepository maintenanceRepository, IPosterCacheMaintenance? posterCache = null,
        ISettingsRepository? settings = null, Func<DateTimeOffset>? clock = null, ITmdbCredentialStore? credentialStore = null)
    {
        _maintenanceRepository = maintenanceRepository;
        _posterCache = posterCache;
        _settings = settings;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _credentialStore = credentialStore;
        try
        {
            var token = credentialStore?.Read()?.Trim();
            lock (_credentialStateGate)
            {
                _tmdbToken = string.IsNullOrWhiteSpace(token) ? null : token;
                _tmdbState = _tmdbToken is null ? TmdbCredentialState.NotConfigured : TmdbCredentialState.Configured;
            }
        }
        catch
        {
            lock (_credentialStateGate) { _tmdbToken = null; _tmdbState = TmdbCredentialState.StorageUnavailable; }
            return;
        }
        try { MigrateLegacyCredential(); }
        catch
        {
            // Legacy cleanup cannot invalidate a credential already verified in protected storage.
            lock (_credentialStateGate)
            {
                if (_tmdbToken is null) _tmdbState = TmdbCredentialState.StorageUnavailable;
            }
        }
    }

    public bool IsTmdbConfigured() { lock (_credentialStateGate) return _tmdbToken is not null; }
    public TmdbCredentialState GetTmdbCredentialState() { lock (_credentialStateGate) return _tmdbState; }
    public string? GetTmdbToken() { lock (_credentialStateGate) return _tmdbToken; }

    public void RecordTmdbAuthenticationResult(string token, bool success)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_credentialStateGate)
        {
            if (!string.Equals(_tmdbToken, token, StringComparison.Ordinal)) return;
            _tmdbState = success ? TmdbCredentialState.Configured : TmdbCredentialState.AuthenticationFailed;
        }
    }

    public async Task<TmdbCredentialResult> SaveTmdbCredentialAsync(string credential, ITmdbCredentialValidator validator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var candidate = credential?.Trim();
        if (string.IsNullOrWhiteSpace(candidate)) return new(false, TmdbCredentialFailure.Empty);

        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ConnectionTestResult validation;
            try
            {
                validation = await validator.ValidateCredentialAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                return new(false, TmdbCredentialFailure.Timeout);
            }
            catch (System.Net.Http.HttpRequestException) { return new(false, TmdbCredentialFailure.Network); }
            catch (Exception) { return new(false, TmdbCredentialFailure.Service); }
            if (!validation.Success)
            {
                return new(false, validation.FailureKind switch
                {
                    MetadataFailureKind.Authentication => TmdbCredentialFailure.Authentication,
                    MetadataFailureKind.Network => TmdbCredentialFailure.Network,
                    MetadataFailureKind.Timeout => TmdbCredentialFailure.Timeout,
                    _ => TmdbCredentialFailure.Service
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_credentialStore is null) return new(false, TmdbCredentialFailure.Storage);
            string? previous;
            try { previous = _credentialStore.Read(); }
            catch { return new(false, TmdbCredentialFailure.Storage); }
            TmdbCredentialState previousState;
            lock (_credentialStateGate) previousState = _tmdbState;

            try
            {
                _credentialStore.Write(candidate);
                if (!string.Equals(_credentialStore.Read(), candidate, StringComparison.Ordinal)) throw new InvalidOperationException();
                _settings?.Delete("tmdb_read_access_token");
                lock (_credentialStateGate) { _tmdbToken = candidate; _tmdbState = TmdbCredentialState.Configured; }
                return new(true, TmdbCredentialFailure.None);
            }
            catch
            {
                var rollbackVerified = false;
                try
                {
                    if (previous is null) _credentialStore.Delete(); else _credentialStore.Write(previous);
                    rollbackVerified = string.Equals(_credentialStore.Read(), previous, StringComparison.Ordinal);
                }
                catch { /* The protected state is uncertain. Do not keep using a stale token. */ }
                lock (_credentialStateGate)
                {
                    _tmdbToken = rollbackVerified ? previous : null;
                    _tmdbState = rollbackVerified ? previousState : TmdbCredentialState.StorageUnavailable;
                }
                return new(false, TmdbCredentialFailure.Storage);
            }
        }
        finally { _credentialGate.Release(); }
    }

    public async Task<bool> RemoveTmdbCredentialAsync(CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_credentialStore is null) return false;
            var deleted = false;
            try
            {
                var legacy = _settings?.Get("tmdb_read_access_token");
                // Preserve a recoverable protected copy before deleting the only legacy copy.
                // Remove the legacy row first so a later restart cannot restore a removed secret.
                if (!string.IsNullOrWhiteSpace(legacy) && _credentialStore.Read() is null)
                {
                    _credentialStore.Write(legacy.Trim());
                    if (!string.Equals(_credentialStore.Read(), legacy.Trim(), StringComparison.Ordinal)) return false;
                }
                _settings?.Delete("tmdb_read_access_token");
                cancellationToken.ThrowIfCancellationRequested();
                _credentialStore.Delete();
                deleted = true;
                lock (_credentialStateGate) { _tmdbToken = null; _tmdbState = TmdbCredentialState.StorageUnavailable; }
                if (_credentialStore.Read() is not null) return false;
                lock (_credentialStateGate) { _tmdbToken = null; _tmdbState = TmdbCredentialState.NotConfigured; }
                return true;
            }
            catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
            catch (Exception)
            {
                if (deleted)
                    lock (_credentialStateGate) { _tmdbToken = null; _tmdbState = TmdbCredentialState.StorageUnavailable; }
                return false;
            }
        }
        finally { _credentialGate.Release(); }
    }

    public Task<ConnectionTestResult> TestTmdbConnectionAsync(IMetadataProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.TestConnectionAsync(cancellationToken);
    }

    private void MigrateLegacyCredential()
    {
        var legacy = _settings?.Get("tmdb_read_access_token")?.Trim();
        if (legacy is null || _credentialStore is null) return;
        // A successful startup read already verified the existing protected value. It wins
        // over an obsolete settings value, including after an interrupted migration.
        if (GetTmdbToken() is not null || legacy.Length == 0)
        {
            _settings?.Delete("tmdb_read_access_token");
            return;
        }
        _credentialStore.Write(legacy);
        if (!string.Equals(_credentialStore.Read(), legacy, StringComparison.Ordinal)) throw new InvalidOperationException();
        lock (_credentialStateGate) { _tmdbToken = legacy; _tmdbState = TmdbCredentialState.Configured; }
        _settings?.Delete("tmdb_read_access_token");
    }

    public ClearLibraryDataResult ClearLibraryData()
    {
        if (!Monitor.TryEnter(_clearGate))
            throw new InvalidOperationException("TBDMovies is busy with another catalog clear.");
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
