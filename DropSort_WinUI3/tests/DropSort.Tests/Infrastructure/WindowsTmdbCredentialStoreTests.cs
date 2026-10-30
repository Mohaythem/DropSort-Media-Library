using System;
using System.Collections.Generic;
using System.IO;
using DropSort.Application.Repositories;
using DropSort.Infrastructure.Security;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class WindowsTmdbCredentialStoreTests
{
    [Fact]
    public void Isolated_profile_round_trip_uses_only_the_scoped_credential_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "DropSort-TmdbCredentialTest-" + Guid.NewGuid().ToString("N"));
        var store = new WindowsTmdbCredentialStore(profile);
        try
        {
            store.Delete();
            Assert.Null(store.Read());
            store.Write("synthetic-native-token");
            Assert.Equal("synthetic-native-token", store.Read());
        }
        finally
        {
            store.Delete();
        }
        Assert.Null(store.Read());
    }

    [Fact]
    public void When_native_credential_deleted_recovers_from_settings_dpapi_backup()
    {
        var profile = Path.Combine(Path.GetTempPath(), "DropSort-TmdbCredentialTest-" + Guid.NewGuid().ToString("N"));
        var settings = new TestSettingsRepo();
        var storeWithSettings = new WindowsTmdbCredentialStore(profile, settings);
        var bareStore = new WindowsTmdbCredentialStore(profile);

        try
        {
            storeWithSettings.Delete();
            Assert.Null(storeWithSettings.Read());

            // Write credential
            storeWithSettings.Write("super-secret-tmdb-token");

            // Verify native credential has it
            Assert.Equal("super-secret-tmdb-token", bareStore.Read());

            // Verify settings has DPAPI encrypted blob, not plaintext
            var cipher = settings.Get("tmdb_credential_dpapi");
            Assert.NotNull(cipher);
            Assert.DoesNotContain("super-secret-tmdb-token", cipher);

            // Simulate Credential Manager deletion / loss
            bareStore.Delete();
            Assert.Null(bareStore.Read());

            // Calling Read() on storeWithSettings should recover from DPAPI and restore to native Credential Manager
            var recovered = storeWithSettings.Read();
            Assert.Equal("super-secret-tmdb-token", recovered);

            // Native Credential Manager is now restored as well
            Assert.Equal("super-secret-tmdb-token", bareStore.Read());

            // Delete via storeWithSettings deletes both native and settings backup
            storeWithSettings.Delete();
            Assert.Null(bareStore.Read());
            Assert.Null(settings.Get("tmdb_credential_dpapi"));
        }
        finally
        {
            bareStore.Delete();
        }
    }

    private sealed class TestSettingsRepo : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value, DateTimeOffset updatedAt) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }
}
