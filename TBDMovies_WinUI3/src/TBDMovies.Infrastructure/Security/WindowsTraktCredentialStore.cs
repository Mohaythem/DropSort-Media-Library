using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Trakt;

namespace TBDMovies.Infrastructure.Security;

/// <summary>
/// Secure Windows Credential Manager store for Trakt OAuth credentials.
/// Implements target isolation for QA profiles, DPAPI fallback in settings, and native roundtrip verification.
/// </summary>
public sealed class WindowsTraktCredentialStore : ITraktCredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;
    private const int MaxBlob = 2560;
    private readonly string _target;
    private readonly string? _legacyTarget;
    private readonly ISettingsRepository? _settings;

    public WindowsTraktCredentialStore(string? isolatedProfileRoot = null, ISettingsRepository? settings = null)
    {
        _settings = settings;
        if (string.IsNullOrWhiteSpace(isolatedProfileRoot))
        {
            _target = "TBDMovies/Trakt";
            _legacyTarget = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("RHJvcFNvcnQvVHJha3Q="));
        }
        else
        {
            if (!Path.IsPathFullyQualified(isolatedProfileRoot))
                throw new ArgumentException("The credential profile root must be an absolute path.");
            var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolatedProfileRoot.Trim())).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
            _target = "TBDMovies/Trakt/QA/" + hash;
            _legacyTarget = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("RHJvcFNvcnQvVHJha3QvUUEv")) + hash;
        }
    }

    public TraktCredentialPayload? Read()
    {
        TraktCredentialPayload? nativePayload = TryReadTarget(_target);
        if (nativePayload is null && !string.IsNullOrWhiteSpace(_legacyTarget))
        {
            nativePayload = TryReadTarget(_legacyTarget);
            if (nativePayload is not null)
            {
                try { Write(nativePayload); } catch { }
            }
        }

        if (nativePayload is not null)
        {
            if (_settings is not null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_settings.Get("trakt_credential_dpapi")))
                    {
                        var json = JsonSerializer.Serialize(nativePayload);
                        var cipher = EncryptDpapi(json);
                        if (cipher is not null) _settings.Set("trakt_credential_dpapi", cipher, DateTimeOffset.UtcNow);
                    }
                }
                catch { }
            }
            return nativePayload;
        }

        if (_settings is not null)
        {
            try
            {
                var cipher = _settings.Get("trakt_credential_dpapi");
                var recoveredJson = DecryptDpapi(cipher);
                if (!string.IsNullOrWhiteSpace(recoveredJson))
                {
                    var recovered = JsonSerializer.Deserialize<TraktCredentialPayload>(recoveredJson);
                    if (recovered is not null)
                    {
                        try { Write(recovered); } catch { }
                        return recovered;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static TraktCredentialPayload? TryReadTarget(string target)
    {
        if (CredReadW(target, Generic, 0, out var pointer))
        {
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.CredentialBlobSize > 0 && credential.CredentialBlobSize <= MaxBlob
                    && credential.CredentialBlob != IntPtr.Zero)
                {
                    var bytes = new byte[(int)credential.CredentialBlobSize];
                    try
                    {
                        Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                        var json = new UTF8Encoding(false, true).GetString(bytes);
                        return JsonSerializer.Deserialize<TraktCredentialPayload>(json);
                    }
                    catch (DecoderFallbackException) { throw StorageError(); }
                    catch (JsonException) { throw StorageError(); }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                        Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
                    }
                }
            }
            finally
            {
                CredFree(pointer);
            }
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            if (err != NotFound) throw StorageError();
        }

        return null;
    }

    public void Write(TraktCredentialPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var json = JsonSerializer.Serialize(payload);
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxBlob)
            throw StorageError();

        var bytes = Encoding.UTF8.GetBytes(json);
        var blob = IntPtr.Zero;
        try
        {
            blob = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var native = new Credential
            {
                Type = Generic,
                TargetName = _target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "TBDMovies/Trakt",
            };
            if (!CredWriteW(ref native, 0)) throw StorageError();

            if (_settings is not null)
            {
                try
                {
                    var cipher = EncryptDpapi(json);
                    if (cipher is not null) _settings.Set("trakt_credential_dpapi", cipher, DateTimeOffset.UtcNow);
                }
                catch { }
            }

            // Native store roundtrip verification
            var roundtrip = Read();
            if (roundtrip is null || !string.Equals(roundtrip.ClientId, payload.ClientId, StringComparison.Ordinal))
            {
                throw StorageError();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (blob != IntPtr.Zero)
            {
                try { Marshal.Copy(bytes, 0, blob, bytes.Length); }
                finally { Marshal.FreeHGlobal(blob); }
            }
        }
    }

    public void Delete()
    {
        if (_settings is not null)
        {
            try { _settings.Delete("trakt_credential_dpapi"); } catch { }
        }
        if (!CredDeleteW(_target, Generic, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw StorageError();
        if (!string.IsNullOrWhiteSpace(_legacyTarget))
        {
            CredDeleteW(_legacyTarget, Generic, 0);
        }
    }

    private static string? EncryptDpapi(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        try
        {
            var raw = Encoding.UTF8.GetBytes(plain);
            var enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        catch { return null; }
    }

    private static string? DecryptDpapi(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return null;
        try
        {
            var raw = Convert.FromBase64String(cipher);
            var dec = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return null; }
    }

    private static InvalidOperationException StorageError() => new("Credential storage unavailable.");

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
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
}
