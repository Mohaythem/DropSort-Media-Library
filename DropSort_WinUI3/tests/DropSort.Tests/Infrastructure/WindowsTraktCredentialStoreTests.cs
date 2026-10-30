using System;
using System.Collections.Generic;
using System.IO;
using DropSort.Application.Repositories;
using DropSort.Application.Trakt;
using DropSort.Infrastructure.Security;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class WindowsTraktCredentialStoreTests
{
    [Fact]
    public void Isolated_profile_round_trip_uses_only_the_scoped_credential_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "DropSort-TraktCredentialTest-" + Guid.NewGuid().ToString("N"));
        var store = new WindowsTraktCredentialStore(profile);
        var payload = new TraktCredentialPayload(
            "client-id-123",
            "access-token-abc",
            "refresh-token-xyz",
            "bearer",
            7200,
            "public",
            1700000000,
            "account-uuid-999");

        try
        {
            store.Delete();
            Assert.Null(store.Read());
            store.Write(payload);

            var read = store.Read();
            Assert.NotNull(read);
            Assert.Equal("client-id-123", read.ClientId);
            Assert.Equal("access-token-abc", read.AccessToken);
            Assert.Equal("refresh-token-xyz", read.RefreshToken);
            Assert.Equal("bearer", read.TokenType);
            Assert.Equal(7200, read.ExpiresIn);
            Assert.Equal("public", read.Scope);
            Assert.Equal(1700000000, read.CreatedAtUnixSeconds);
            Assert.Equal("account-uuid-999", read.AccountId);
            Assert.False(read.RequiresReconnect);
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
        var profile = Path.Combine(Path.GetTempPath(), "DropSort-TraktCredentialTest-" + Guid.NewGuid().ToString("N"));
        var settings = new TestSettingsRepo();
        var storeWithSettings = new WindowsTraktCredentialStore(profile, settings);
        var bareStore = new WindowsTraktCredentialStore(profile);

        var payload = new TraktCredentialPayload(
            "client-id-recovery",
            "access-secret-token",
            "refresh-secret-token",
            "bearer",
            3600,
            "public",
            1700001000,
            "acc-123");

        try
        {
            storeWithSettings.Delete();
            Assert.Null(storeWithSettings.Read());

            // Write credential
            storeWithSettings.Write(payload);

            // Verify native credential exists
            var nativeRead = bareStore.Read();
            Assert.NotNull(nativeRead);
            Assert.Equal("access-secret-token", nativeRead.AccessToken);

            // Verify settings has DPAPI encrypted blob, not plaintext
            var cipher = settings.Get("trakt_credential_dpapi");
            Assert.NotNull(cipher);
            Assert.DoesNotContain("access-secret-token", cipher);
            Assert.DoesNotContain("refresh-secret-token", cipher);

            // Delete from native store
            bareStore.Delete();
            Assert.Null(bareStore.Read());

            // Read from storeWithSettings should recover from DPAPI
            var recovered = storeWithSettings.Read();
            Assert.NotNull(recovered);
            Assert.Equal("client-id-recovery", recovered.ClientId);
            Assert.Equal("access-secret-token", recovered.AccessToken);
            Assert.Equal("refresh-secret-token", recovered.RefreshToken);

            // Native Credential Manager is also restored
            var restoredNative = bareStore.Read();
            Assert.NotNull(restoredNative);
            Assert.Equal("access-secret-token", restoredNative.AccessToken);

            // Delete via storeWithSettings removes both native and settings backup
            storeWithSettings.Delete();
            Assert.Null(bareStore.Read());
            Assert.Null(settings.Get("trakt_credential_dpapi"));
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
