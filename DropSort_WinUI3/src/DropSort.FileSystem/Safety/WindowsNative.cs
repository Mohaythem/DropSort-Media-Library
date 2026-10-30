using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using DropSort.Domain.Core.Safety;

namespace DropSort.FileSystem.Safety;

internal static class WindowsNative
{
    private const uint GenericRead = 0x80000000;
    private const uint Delete = 0x00010000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagSequentialScan = 0x08000000;
    private const int FileDispositionInfo = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FileDispositionInformation
    {
        public byte DeleteFile;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int fileInformationClass,
        ref FileDispositionInformation information,
        uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWInternal(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes);

    internal static bool CreateHardLinkW(
        string newFileName,
        string existingFileName,
        IntPtr securityAttributes) =>
        CreateHardLinkWInternal(EnsureExtendedPath(newFileName), EnsureExtendedPath(existingFileName), securityAttributes);

    internal static string EnsureExtendedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    internal static SafeFileHandle OpenLockedSource(string path)
    {
        var handle = CreateFileW(
            EnsureExtendedPath(path),
            GenericRead | Delete,
            FileShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagSequentialScan,
            IntPtr.Zero);
        ThrowIfInvalid(handle, path);
        return handle;
    }

    internal static SafeFileHandle OpenDirectoryGuard(string path)
    {
        var handle = CreateFileW(
            EnsureExtendedPath(path),
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);
        ThrowIfInvalid(handle, path);
        return handle;
    }

    internal static SourceIdentity Identity(string path)
    {
        using var handle = OpenReadIdentity(path);
        return Identity(handle);
    }

    internal static SourceIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var size = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        var fileTime = ((long)info.LastWriteTime.dwHighDateTime << 32) |
                       (uint)info.LastWriteTime.dwLowDateTime;
        var unixEpochFileTime = DateTimeOffset.UnixEpoch.ToFileTime();
        var mtimeNs = checked((fileTime - unixEpochFileTime) * 100L);
        var index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return new SourceIdentity(size, mtimeNs, info.VolumeSerialNumber, index);
    }

    internal static void MarkDeleteOnClose(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = 1 };
        if (!SetFileInformationByHandle(
                handle,
                FileDispositionInfo,
                ref disposition,
                (uint)Marshal.SizeOf<FileDispositionInformation>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static SafeFileHandle OpenReadIdentity(string path)
    {
        var handle = CreateFileW(
            EnsureExtendedPath(path),
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        ThrowIfInvalid(handle, path);
        return handle;
    }

    private static void ThrowIfInvalid(SafeFileHandle handle, string path)
    {
        if (!handle.IsInvalid) return;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error, $"Could not open '{path}' safely.");
    }
}
