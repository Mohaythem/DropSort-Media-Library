using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DropSort.Application.External;
using DropSort.Application.Repositories;

namespace DropSort.Infrastructure.Security;

/// <summary>One generic credential in the current Windows user's native credential set.</summary>
public sealed class WindowsTmdbCredentialStore : ITmdbCredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;
    private const int MaxBlob = 2560;
    private readonly string _target;
    private readonly ISettingsRepository? _settings;

    public WindowsTmdbCredentialStore(string? isolatedProfileRoot = null, ISettingsRepository? settings = null)
    {
        _settings = settings;
        if (string.IsNullOrWhiteSpace(isolatedProfileRoot))
        {
            _target = "DropSort/TMDB";
        }
        else
        {
            // QA must never read, replace, or delete the live profile's credential.
            if (!Path.IsPathFullyQualified(isolatedProfileRoot))
                throw new ArgumentException("The credential profile root must be an absolute path.");
            var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolatedProfileRoot.Trim())).ToUpperInvariant();
            _target = "DropSort/TMDB/QA/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
        }
    }

    public string? Read()
    {
        string? nativeToken = null;
        if (CredReadW(_target, Generic, 0, out var pointer))
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
                        nativeToken = new UTF8Encoding(false, true).GetString(bytes);
                    }
                    catch (DecoderFallbackException) { throw StorageError(); }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(bytes);
                        Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
                    }
                }
            }
            finally { CredFree(pointer); }
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            if (err != NotFound) throw StorageError();
        }

        if (!string.IsNullOrWhiteSpace(nativeToken))
        {
            if (_settings is not null)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(_settings.Get("tmdb_credential_dpapi")))
                    {
                        var cipher = EncryptDpapi(nativeToken);
                        if (cipher is not null) _settings.Set("tmdb_credential_dpapi", cipher, DateTimeOffset.UtcNow);
                    }
                }
                catch { }
            }
            return nativeToken;
        }

        if (_settings is not null)
        {
            try
            {
                var cipher = _settings.Get("tmdb_credential_dpapi");
                var recovered = DecryptDpapi(cipher);
                if (!string.IsNullOrWhiteSpace(recovered))
                {
                    try { Write(recovered); } catch { }
                    return recovered;
                }
            }
            catch { }
        }

        return null;
    }

    public void Write(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential) || Encoding.UTF8.GetByteCount(credential) > MaxBlob)
            throw StorageError();
        var bytes = Encoding.UTF8.GetBytes(credential);
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
                UserName = "DropSort",
            };
            if (!CredWriteW(ref native, 0)) throw StorageError();

            if (_settings is not null)
            {
                try
                {
                    var cipher = EncryptDpapi(credential);
                    if (cipher is not null) _settings.Set("tmdb_credential_dpapi", cipher, DateTimeOffset.UtcNow);
                }
                catch { }
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
            try { _settings.Delete("tmdb_credential_dpapi"); } catch { }
        }
        if (!CredDeleteW(_target, Generic, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw StorageError();
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
