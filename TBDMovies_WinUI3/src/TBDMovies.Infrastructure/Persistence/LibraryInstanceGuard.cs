using Microsoft.Data.Sqlite;

namespace TBDMovies.Infrastructure.Persistence;

/// <summary>
/// Cross-process single-owner guard for one library database file. The first process to call
/// <see cref="TryAcquire" /> takes an exclusive OS lock on a sibling lock file and holds it for the
/// whole session, so only one TBDMovies process migrates, recovers, or otherwise owns a given
/// <c>library.db</c> at a time. A second process cannot acquire the lock and must therefore not run
/// migration or recovery against the same database. The lock is released on <see cref="Dispose" /> and,
/// if the process crashes, by the operating system closing the handle. In-memory databases have no file
/// to lock and are always granted, so tests are unaffected.
/// </summary>
public sealed class LibraryInstanceGuard : IDisposable
{
    private FileStream? _lockStream;

    private LibraryInstanceGuard(FileStream? lockStream) => _lockStream = lockStream;

    /// <summary>
    /// Tries to become the single owner of the database named by <paramref name="connectionString" />.
    /// Returns a guard to hold for the session, or <c>null</c> when another process already owns it.
    /// </summary>
    public static LibraryInstanceGuard? TryAcquire(string connectionString)
    {
        string? dataSource;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch
        {
            dataSource = null;
        }

        if (string.IsNullOrWhiteSpace(dataSource)
            || dataSource == ":memory:"
            || dataSource.StartsWith("file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryInstanceGuard(null);
        }

        var lockPath = Path.GetFullPath(dataSource) + ".tbdmovies-lock";
        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new LibraryInstanceGuard(stream);
        }
        catch (IOException)
        {
            // Another process holds the exclusive lock.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _lockStream, null);
        stream?.Dispose();
    }
}
