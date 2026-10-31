using System;
using System.Collections.Generic;
using System.IO;
using TBDMovies.Application.Repositories;
using TBDMovies.Infrastructure.Security;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

public sealed class WindowsTmdbCredentialStoreTests
{
    [Fact]
    public void Isolated_profile_round_trip_uses_only_the_scoped_credential_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TmdbCredentialTest-" + Guid.NewGuid().ToString("N"));
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
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TmdbCredentialTest-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void When_legacy_target_exists_reads_and_migrates_to_primary_target()
    {
        var profile = Path.Combine(Path.GetTempPath(), "TBDMovies-TmdbMigrateTest-" + Guid.NewGuid().ToString("N"));
        var store = new WindowsTmdbCredentialStore(profile);
        store.Delete();

        var normalizedProfile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.Trim())).ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedProfile)));
        var legacyTarget = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("RHJvcFNvcnQvVE1EQi9RQS8=")) + hash;

        WriteNativeDirect(legacyTarget, "legacy-tmdb-token");

        try
        {
            var read = store.Read();
            Assert.Equal("legacy-tmdb-token", read);

            DeleteNativeDirect(legacyTarget);

            Assert.Equal("legacy-tmdb-token", store.Read());
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
                UserName = "TBDMovies",
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
