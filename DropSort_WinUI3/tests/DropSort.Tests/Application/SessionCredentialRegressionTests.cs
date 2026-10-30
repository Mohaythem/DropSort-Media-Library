using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Metadata.Contracts;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class SessionCredentialRegressionTests
{
    [Fact]
    public async Task Save_read_and_restart_use_secure_store_without_writing_plaintext_settings()
    {
        var store = new FakeCredentialStore();
        var settings = new MemorySettings();
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        var result = await service.SaveTmdbCredentialAsync("  synthetic-token  ", new Validator());
        Assert.True(result.Success);
        Assert.Equal("synthetic-token", store.Value);
        Assert.Equal("synthetic-token", service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.Configured, service.GetTmdbCredentialState());
        Assert.DoesNotContain("synthetic-token", settings.Values.Values);
        var restarted = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.Equal("synthetic-token", restarted.GetTmdbToken());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_whitespace_credentials_are_rejected_without_store_access(string candidate)
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        var result = await service.SaveTmdbCredentialAsync(candidate, new Validator());
        Assert.False(result.Success);
        Assert.Equal(TmdbCredentialFailure.Empty, result.Failure);
        Assert.Equal("old-token", store.Value);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Failed_validation_preserves_old_credential()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        var validator = new Validator { Result = new(false, "rejected", 401, MetadataFailureKind.Authentication) };
        var result = await service.SaveTmdbCredentialAsync("new-token", validator);
        Assert.False(result.Success);
        Assert.Equal(TmdbCredentialFailure.Authentication, result.Failure);
        Assert.Equal("old-token", service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.Configured, service.GetTmdbCredentialState());
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Read_failure_during_save_preserves_old_state_and_rolls_back_candidate()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        store.FailNextReads = 1;
        var result = await service.SaveTmdbCredentialAsync("new-token", new Validator());
        Assert.False(result.Success);
        Assert.Equal(TmdbCredentialFailure.Storage, result.Failure);
        Assert.Equal("old-token", store.Value);
        Assert.Equal("old-token", service.GetTmdbToken());
    }

    [Fact]
    public async Task Rollback_verification_failure_clears_uncertain_active_state()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        store.FailReadsAfterWrite = 2;
        var result = await service.SaveTmdbCredentialAsync("new-token", new Validator());
        Assert.False(result.Success);
        Assert.Null(service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.StorageUnavailable, service.GetTmdbCredentialState());
    }

    [Fact]
    public async Task Network_and_service_validation_failures_preserve_existing_credential()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        foreach (var failure in new[] { MetadataFailureKind.Network, MetadataFailureKind.Api })
        {
            var result = await service.SaveTmdbCredentialAsync("new-token", new Validator
            {
                Result = new(false, "failed", null, failure)
            });
            Assert.False(result.Success);
            Assert.Equal("old-token", service.GetTmdbToken());
        }
    }

    [Fact]
    public void Startup_secure_read_failure_is_reported_without_throwing_or_exposing_a_token()
    {
        var store = new FakeCredentialStore { Value = "secret", FailNextReads = 1 };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        Assert.Null(service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.StorageUnavailable, service.GetTmdbCredentialState());
    }

    [Fact]
    public void Failed_legacy_migration_keeps_legacy_value_and_does_not_activate_candidate()
    {
        var store = new FakeCredentialStore { FailNextReads = 1 };
        var settings = new MemorySettings();
        settings.Values["tmdb_read_access_token"] = "legacy-token";
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.Null(service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.StorageUnavailable, service.GetTmdbCredentialState());
        Assert.Equal("legacy-token", settings.Get("tmdb_read_access_token"));
    }

    [Fact]
    public async Task Cancellation_during_validation_does_not_replace_existing_credential()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveTmdbCredentialAsync("new-token", new Validator(), cancellation.Token));
        Assert.Equal("old-token", service.GetTmdbToken());
    }

    [Fact]
    public async Task Remove_deletes_only_secure_credential_and_is_idempotent()
    {
        var store = new FakeCredentialStore { Value = "old-token" };
        var settings = new MemorySettings();
        settings.Values["ui_language"] = "ar";
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.True(await service.RemoveTmdbCredentialAsync());
        Assert.Null(store.Value);
        Assert.Equal(TmdbCredentialState.NotConfigured, service.GetTmdbCredentialState());
        Assert.Equal("ar", settings.Values["ui_language"]);
        Assert.True(await service.RemoveTmdbCredentialAsync());
    }

    [Fact]
    public async Task Remove_delete_failure_preserves_existing_protected_credential()
    {
        var store = new FakeCredentialStore { Value = "old-token", FailDelete = true };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        Assert.False(await service.RemoveTmdbCredentialAsync());
        Assert.Equal("old-token", store.Value);
        Assert.Equal("old-token", service.GetTmdbToken());
    }

    [Fact]
    public async Task Remove_readback_failure_reports_storage_unavailable_after_delete()
    {
        var store = new FakeCredentialStore { Value = "old-token", FailReadAfterDelete = true };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        Assert.False(await service.RemoveTmdbCredentialAsync());
        Assert.Null(store.Value);
        Assert.Null(service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.StorageUnavailable, service.GetTmdbCredentialState());
    }

    [Fact]
    public void Legacy_plaintext_migrates_only_after_secure_write_and_read_verification()
    {
        var store = new FakeCredentialStore();
        var settings = new MemorySettings();
        settings.Values["tmdb_read_access_token"] = "legacy-token";
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.Equal("legacy-token", store.Value);
        Assert.Equal("legacy-token", service.GetTmdbToken());
        Assert.Null(settings.Get("tmdb_read_access_token"));
    }

    [Fact]
    public void Existing_protected_credential_survives_legacy_cleanup_failure()
    {
        var store = new FakeCredentialStore { Value = "protected-token" };
        var settings = new MemorySettings { FailDelete = true };
        settings.Values["tmdb_read_access_token"] = "legacy-token";
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.Equal("protected-token", service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.Configured, service.GetTmdbCredentialState());
        Assert.Equal("legacy-token", settings.Get("tmdb_read_access_token"));
    }

    [Fact]
    public void Newly_migrated_credential_survives_legacy_cleanup_failure()
    {
        var store = new FakeCredentialStore();
        var settings = new MemorySettings { FailDelete = true };
        settings.Values["tmdb_read_access_token"] = "legacy-token";
        var service = new SettingsService(new Maintenance(), settings: settings, credentialStore: store);
        Assert.Equal("legacy-token", service.GetTmdbToken());
        Assert.Equal(TmdbCredentialState.Configured, service.GetTmdbCredentialState());
        Assert.Equal("legacy-token", store.Value);
    }

    [Fact]
    public void Late_authentication_result_for_old_token_is_ignored()
    {
        var store = new FakeCredentialStore { Value = "current-token" };
        var service = new SettingsService(new Maintenance(), credentialStore: store);
        service.RecordTmdbAuthenticationResult("old-token", false);
        Assert.Equal(TmdbCredentialState.Configured, service.GetTmdbCredentialState());
        Assert.Equal("current-token", service.GetTmdbToken());
    }

    private sealed class FakeCredentialStore : ITmdbCredentialStore
    {
        public string? Value { get; set; }
        public int WriteCount { get; private set; }
        public int FailNextReads { get; set; }
        public int FailReadsAfterWrite { get; set; }
        public bool FailDelete { get; set; }
        public bool FailReadAfterDelete { get; set; }
        public string? Read()
        {
            if (FailNextReads > 0) { FailNextReads--; throw new InvalidOperationException("synthetic read failure"); }
            if (WriteCount > 0 && FailReadsAfterWrite > 0) { FailReadsAfterWrite--; throw new InvalidOperationException("synthetic read failure"); }
            if (FailReadAfterDelete && Value is null) throw new InvalidOperationException("synthetic readback failure");
            return Value;
        }
        public void Write(string credential) { WriteCount++; Value = credential; }
        public void Delete() { if (FailDelete) throw new InvalidOperationException("synthetic delete failure"); Value = null; }
    }

    private sealed class Validator : ITmdbCredentialValidator
    {
        public ConnectionTestResult Result { get; set; } = new(true, "ok", 200);
        public Task<ConnectionTestResult> ValidateCredentialAsync(string credential, CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }

    private sealed class MemorySettings : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool FailDelete { get; set; }
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public void Set(string key, string value, DateTimeOffset updatedAt) => Values[key] = value;
        public void Delete(string key) { if (FailDelete) throw new InvalidOperationException("synthetic cleanup failure"); Values.Remove(key); }
    }

    private sealed class Maintenance : ILibraryMaintenanceRepository
    {
        public (int Movies, int MediaFiles, int MetadataEntries, int Shows, int Seasons, int Episodes) ClearCatalog() => default;
    }
}
