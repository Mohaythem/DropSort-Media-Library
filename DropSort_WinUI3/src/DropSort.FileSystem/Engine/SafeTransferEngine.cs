using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;
using DropSort.FileSystem.Safety;
using Microsoft.Win32.SafeHandles;

namespace DropSort.FileSystem.Engine;

/// <summary>
/// Performs only the filesystem half of a journaled operation. A locked source handle and
/// guarded destination directory are retained between Prepare and FinalizeSourceRemoval.
/// </summary>
internal sealed class SafeTransferEngine : IDisposable
{
    private const int BufferSize = 4 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, TransferSession> _sessions = new(StringComparer.Ordinal);
    private readonly bool _forceCopy;

    internal SafeTransferEngine(bool forceCopy = false) => _forceCopy = forceCopy;

    public PreparedTransfer Prepare(
        string source,
        string destination,
        SourceIdentity expected,
        string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("Operation ID is required.", nameof(operationId));
        if (_sessions.ContainsKey(operationId)) throw new InvalidOperationException("Operation already has an active transfer session.");

        PathPolicy.AssertNoReparseComponents(source);
        PathPolicy.AssertNoReparseComponents(Path.GetDirectoryName(destination)!);
        var sourceHandle = WindowsNative.OpenLockedSource(source);
        SafeFileHandle? directoryGuard = null;
        string? tempPath = null;
        try
        {
            if (WindowsNative.Identity(sourceHandle) != expected)
                throw new SourceChangedException("Source identity changed before execution.");
            directoryGuard = WindowsNative.OpenDirectoryGuard(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new DestinationExistsException($"Destination already exists: {destination}");

            PreparedTransfer prepared;
            if (!_forceCopy && WindowsNative.CreateHardLinkW(destination, source, IntPtr.Zero))
            {
                var destinationIdentity = WindowsNative.Identity(destination);
                if (destinationIdentity != expected)
                    throw new IOException("Hard-link destination does not identify the locked source.");
                prepared = FromIdentity("hardlink-unlink", destinationIdentity, null);
            }
            else
            {
                var error = _forceCopy ? 17 : Marshal.GetLastWin32Error();
                if (!CanFallbackToCopy(error)) throw new Win32Exception(error, "Hard-link creation failed.");
                tempPath = Path.Combine(
                    Path.GetDirectoryName(destination)!,
                    $".{Path.GetFileName(destination)}.dropsort-{operationId}.tmp");
                prepared = PrepareCopy(source, destination, tempPath, expected, sourceHandle);
                tempPath = null;
            }

            var session = new TransferSession(source, destination, expected, prepared, sourceHandle, directoryGuard);
            if (!_sessions.TryAdd(operationId, session))
                throw new InvalidOperationException("Operation already has an active transfer session.");
            sourceHandle = null!;
            directoryGuard = null;
            return prepared;
        }
        finally
        {
            if (tempPath is not null && File.Exists(tempPath)) File.Delete(tempPath);
            sourceHandle?.Dispose();
            directoryGuard?.Dispose();
        }
    }

    public void FinalizeSourceRemoval(
        string source,
        string destination,
        SourceIdentity expected,
        PreparedTransfer prepared,
        string operationId)
    {
        if (!_sessions.TryRemove(operationId, out var session))
            throw new InvalidOperationException("No active transfer session exists for this operation.");
        using (session)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(session.Source, source) ||
                !StringComparer.OrdinalIgnoreCase.Equals(session.Destination, destination) ||
                session.Expected != expected || session.Prepared != prepared)
                throw new SourceChangedException("Transfer finalization does not match the prepared operation.");
            if (WindowsNative.Identity(session.SourceHandle) != expected)
                throw new SourceChangedException("Source identity changed during execution.");

            var destinationIdentity = WindowsNative.Identity(destination);
            var verified = new SourceIdentity(
                prepared.DestinationSize,
                prepared.DestinationMTimeNs,
                prepared.DestinationDev,
                prepared.DestinationIno);
            if (destinationIdentity != verified)
                throw new IOException("Destination changed after durable verification.");
            if (prepared.DestinationSha256 is not null &&
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(prepared.DestinationSha256),
                    Convert.FromHexString(ComputeSha256(destination))))
                throw new IOException("Destination SHA-256 verification failed before source removal.");

            WindowsNative.MarkDeleteOnClose(session.SourceHandle);
        }
        if (File.Exists(source)) throw new IOException("Source removal did not complete.");
    }

    public void Abandon(string operationId)
    {
        if (_sessions.TryRemove(operationId, out var session)) session.Dispose();
    }

    public void Dispose()
    {
        foreach (var operationId in _sessions.Keys) Abandon(operationId);
    }

    private static PreparedTransfer PrepareCopy(
        string source,
        string destination,
        string tempPath,
        SourceIdentity expected,
        SafeFileHandle sourceHandle)
    {
        string sourceHash;
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
        using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                output.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
            }
            output.Flush(flushToDisk: true);
            sourceHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        if (WindowsNative.Identity(sourceHandle) != expected)
            throw new SourceChangedException("Source identity changed during cross-volume copy.");
        File.Move(tempPath, destination, overwrite: false);
        var destinationHash = ComputeSha256(destination);
        if (!StringComparer.Ordinal.Equals(sourceHash, destinationHash))
            throw new IOException("Destination SHA-256 verification failed.");
        return FromIdentity("copy-sha256-flush-finalize-unlink", WindowsNative.Identity(destination), destinationHash);
    }

    private static PreparedTransfer FromIdentity(string strategy, SourceIdentity identity, string? sha256) =>
        new(strategy, identity.Size, identity.MTimeNs, identity.Dev, identity.Ino, sha256);

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool CanFallbackToCopy(int win32Error) => win32Error is 1 or 17 or 50;

    private sealed class TransferSession : IDisposable
    {
        public string Source { get; }
        public string Destination { get; }
        public SourceIdentity Expected { get; }
        public PreparedTransfer Prepared { get; }
        public SafeFileHandle SourceHandle { get; }
        private SafeFileHandle DirectoryGuard { get; }

        public TransferSession(
            string source,
            string destination,
            SourceIdentity expected,
            PreparedTransfer prepared,
            SafeFileHandle sourceHandle,
            SafeFileHandle directoryGuard)
        {
            Source = source;
            Destination = destination;
            Expected = expected;
            Prepared = prepared;
            SourceHandle = sourceHandle;
            DirectoryGuard = directoryGuard;
        }

        public void Dispose()
        {
            SourceHandle.Dispose();
            DirectoryGuard.Dispose();
        }
    }
}
