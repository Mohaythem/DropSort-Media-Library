using System;
using System.Collections.Generic;
using System.IO;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Trakt;
using TBDMovies.Infrastructure.Security;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

public sealed class WindowsTraktCredentialStoreTests
{
    [Fact]
    public void Isolated_profile_round_trip_uses_only_the_scoped_credential_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TraktCredentialTest-" + Guid.NewGuid().ToString("N"));
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
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TraktCredentialTest-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void When_legacy_target_exists_reads_and_migrates_to_primary_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TraktMigrateTest-" + Guid.NewGuid().ToString("N"));
        var store = new WindowsTraktCredentialStore(profile);
        store.Delete();

        var normalizedProfile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.Trim())).ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedProfile)));
        var legacyTarget = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("RHJvcFNvcnQvVHJha3QvUUEv")) + hash;

        var payload = new TraktCredentialPayload(
            "client-id-migrated",
            "access-migrated",
            "refresh-migrated",
            "bearer",
            3600,
            "public",
            1700000000,
            "acc-migrated");

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        WriteNativeDirect(legacyTarget, json);

        try
        {
            var read = store.Read();
            Assert.NotNull(read);
            Assert.Equal("client-id-migrated", read.ClientId);
            Assert.Equal("access-migrated", read.AccessToken);

            DeleteNativeDirect(legacyTarget);

            var readAgain = store.Read();
            Assert.NotNull(readAgain);
            Assert.Equal("client-id-migrated", readAgain.ClientId);
            Assert.Equal("access-migrated", readAgain.AccessToken);
        }
        finally
        {
            store.Delete();
            DeleteNativeDirect(legacyTarget);
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    private static void WriteNativeDirect(string target, string secret)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var blob = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new NativeCredential
            {
                Type = 1,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = 2,
                UserName = "TBDMovies/Trakt",
            };
            CredWriteW(ref cred, 0);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(blob);
        }
    }

    private static void DeleteNativeDirect(string target)
    {
        CredDeleteW(target, 1, 0);
    }

    private sealed class TestSettingsRepo : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value, DateTimeOffset updatedAt) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }
}
