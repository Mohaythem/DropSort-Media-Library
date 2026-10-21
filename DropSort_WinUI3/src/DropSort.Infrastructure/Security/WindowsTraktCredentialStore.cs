using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropSort.Application.Trakt;

namespace DropSort.Infrastructure.Security;

/// <summary>
/// Secure Windows Credential Manager store for Trakt OAuth credentials.
/// Implements target isolation for QA profiles and native roundtrip verification.
/// </summary>
public sealed class WindowsTraktCredentialStore : ITraktCredentialStore
{
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;
    private const int MaxBlob = 2560;
    private readonly string _target;

    public WindowsTraktCredentialStore(string? isolatedProfileRoot = null)
    {
        if (string.IsNullOrWhiteSpace(isolatedProfileRoot))
        {
            _target = "DropSort/Trakt";
        }
        else
        {
            if (!Path.IsPathFullyQualified(isolatedProfileRoot))
                throw new ArgumentException("The credential profile root must be an absolute path.");
            var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolatedProfileRoot.Trim())).ToUpperInvariant();
            _target = "DropSort/Trakt/QA/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile)));
        }
    }

    public TraktCredentialPayload? Read()
    {
        if (!CredReadW(_target, Generic, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == NotFound) return null;
            throw StorageError();
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > MaxBlob
                || credential.CredentialBlob == IntPtr.Zero)
            {
                throw StorageError();
            }

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
        finally
        {
            CredFree(pointer);
        }
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
                UserName = "DropSort/Trakt",
            };
            if (!CredWriteW(ref native, 0)) throw StorageError();

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
        if (!CredDeleteW(_target, Generic, 0) && Marshal.GetLastWin32Error() != NotFound)
            throw StorageError();
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
