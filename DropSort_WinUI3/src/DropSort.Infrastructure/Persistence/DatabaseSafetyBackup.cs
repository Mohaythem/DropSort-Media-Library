using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence;

/// <summary>Raised when a required safety backup cannot be created, verified, or restored.</summary>
public sealed class DatabaseBackupException : Exception
{
    public DatabaseBackupException(string message) : base(message) { }
    public DatabaseBackupException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Automatic pre-operation safety backups for the local library database. Before a risky database
/// mutation (a forward schema migration of an existing database, a destructive catalog clear) DropSort
/// creates a consistent SQLite snapshot, verifies it, and only then lets the mutation proceed. If the
/// backup cannot be created or verified, the caller must abort and leave the database untouched
/// (fail closed). This is not the future portable-backup feature: backups are internal, local, and
/// bounded.
/// </summary>
public static class DatabaseSafetyBackup
{
    /// <summary>Known-good automatic backups kept per database. Older ones are pruned.</summary>
    public const int RetentionCount = 5;

    /// <summary>The dedicated backup directory beside the database file.</summary>
    public static string BackupDirectoryFor(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
            throw new DatabaseBackupException("The database connection has no file to back up.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource))
            ?? throw new DatabaseBackupException("Cannot resolve the database directory.");
        return Path.Combine(directory, "backups");
    }

    /// <summary>
    /// Creates and verifies a snapshot of the database that <paramref name="source" /> is connected to.
    /// Returns the backup path. Throws <see cref="DatabaseBackupException" /> on any failure and removes
    /// a partial file, so the source is left untouched.
    /// </summary>
    public static string CreateVerifiedBackup(
        SqliteConnection source, string connectionString, int sourceVersion, string label)
    {
        var backupDirectory = BackupDirectoryFor(connectionString);
        var backupPath = string.Empty;
        try
        {
            Directory.CreateDirectory(backupDirectory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
            // A random token keeps two same-millisecond, same-label backups from colliding, so a rapid
            // or concurrent protected operation is never turned into a fail-closed abort by a name clash.
            var token = Guid.NewGuid().ToString("N")[..8];
            backupPath = Path.Combine(backupDirectory, $"library.{stamp}.v{sourceVersion}.{Sanitize(label)}.{token}.db");
            if (File.Exists(backupPath))
                throw new DatabaseBackupException($"A backup already exists at '{backupPath}'.");

            // The SQLite online backup API copies a consistent snapshot, including committed WAL pages,
            // into a fresh standalone database file - never a raw byte copy that can miss WAL state.
            // Pooling is disabled so the destination handle is released immediately and the file can be
            // verified, pruned, or deleted without a lingering pooled handle holding it open.
            using (var destination = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }
        }
        catch (DatabaseBackupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(backupPath);
            throw new DatabaseBackupException("Failed to create the safety backup.", exception);
        }

        if (!VerifyBackup(backupPath, out var verifiedVersion) || verifiedVersion != sourceVersion)
        {
            TryDelete(backupPath);
            throw new DatabaseBackupException($"The safety backup at '{backupPath}' failed verification.");
        }

        // Prune only after the new backup is verified, and never touch it, so retention can never delete
        // the only valid copy or a just-created replacement.
        PruneOldBackups(backupDirectory, keep: backupPath);
        return backupPath;
    }

    /// <summary>
    /// Opens the backup read-only and proves it is structurally usable: an integrity check passes and
    /// the schema version can be read. Does not touch the source database.
    /// </summary>
    public static bool VerifyBackup(string backupPath, out int userVersion)
    {
        userVersion = -1;
        if (!File.Exists(backupPath)) return false;
        try
        {
            using var connection = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                if (integrity.ExecuteScalar() as string != "ok") return false;
            }
            using var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            userVersion = Convert.ToInt32(version.ExecuteScalar());
            return true;
        }
        catch
        {
            return false;
        }
    }

    // PLACEHOLDER_RESTORE_AND_HELPERS

    /// <summary>
    /// Backend restore primitive. Replaces the database at <paramref name="connectionString" /> with a
    /// verified, compatible backup. The caller must ensure DropSort is not using the database (hold the
    /// single-instance guard). The current database is preserved with a WAL-safe snapshot, the
    /// replacement is staged and swapped in with an atomic same-volume replace, and the result is
    /// validated; on failure the original is rolled back. A fully crash-safe, journaled restore with
    /// startup reconciliation, and the user-facing restore workflow, are a documented pre-1.0 release
    /// gate (see FUTURE_PLAN.md); this primitive is not yet wired into any user or startup path.
    /// </summary>
    public static void RestoreFromBackup(string backupPath, string connectionString)
    {
        // Accept only a structurally sound DropSort database within the supported schema range, so a
        // restore can never overwrite the library with an unrelated or unsupported-version file.
        if (!IsRestorableDatabase(backupPath, out _))
            throw new DatabaseBackupException($"The backup at '{backupPath}' is not a restorable DropSort database.");

        var target = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(target))
            throw new DatabaseBackupException("The database connection has no file to restore into.");
        target = Path.GetFullPath(target);

        SqliteConnection.ClearAllPools();

        // Preserve the current database with a WAL-safe snapshot, not a raw main-file copy, so committed
        // pages still in the WAL are captured in the preserved copy.
        var preRestore = target + ".pre-restore";
        TryDelete(preRestore);
        if (File.Exists(target))
        {
            try
            {
                using var live = new SqliteConnection($"Data Source={target};Mode=ReadOnly;Pooling=False");
                live.Open();
                using var snapshot = new SqliteConnection($"Data Source={preRestore};Pooling=False");
                snapshot.Open();
                live.BackupDatabase(snapshot);
            }
            catch
            {
                // The current database is not a usable SQLite file; keep a raw copy as best-effort evidence.
                TryDelete(preRestore);
                File.Copy(target, preRestore, overwrite: true);
            }
        }

        // Stage the replacement, then swap it in atomically on the same volume. Removing a stale WAL/SHM
        // sidecar is fatal before any swap, so the swapped-in file is authoritative.
        var incoming = target + ".restore-incoming";
        TryDelete(incoming);
        try
        {
            File.Copy(backupPath, incoming, overwrite: true);
            DeleteOrThrow(target + "-wal");
            DeleteOrThrow(target + "-shm");
            if (File.Exists(target))
                File.Replace(incoming, target, destinationBackupFileName: null);
            else
                File.Move(incoming, target);

            if (!IsRestorableDatabase(target, out _))
                throw new DatabaseBackupException("The restored database failed verification.");
        }
        catch
        {
            TryDelete(incoming);
            if (File.Exists(preRestore))
                File.Copy(preRestore, target, overwrite: true); // roll back the current data
            throw;
        }
    }

    /// <summary>
    /// A backup is restorable only if it is structurally sound, carries a supported DropSort schema
    /// version, and has the core catalog table. This blocks overwriting the library with an unrelated or
    /// unsupported-version database.
    /// </summary>
    public static bool IsRestorableDatabase(string path, out int userVersion)
    {
        userVersion = -1;
        if (!VerifyBackup(path, out var version)) return false;
        if (version < 1 || version > Migrations.DatabaseMigrator.LatestVersion) return false;
        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='movies';";
            if (Convert.ToInt32(command.ExecuteScalar()) != 1) return false;
        }
        catch
        {
            return false;
        }

        userVersion = version;
        return true;
    }

    private static void DeleteOrThrow(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            throw new DatabaseBackupException($"Could not remove sidecar '{path}' before restore.", exception);
        }
    }

    private static void PruneOldBackups(string backupDirectory, string keep)
    {
        try
        {
            var backups = Directory.GetFiles(backupDirectory, "library.*.db")
                .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path, StringComparer.Ordinal) // timestamped names sort chronologically
                .ToList();

            // Keep the newest (RetentionCount - 1) alongside the just-created backup.
            foreach (var stale in backups.Skip(RetentionCount - 1))
                TryDelete(stale);
        }
        catch
        {
            // Retention is best effort. A pruning failure must never invalidate the new, verified backup.
        }
    }

    private static string Sanitize(string label)
    {
        var cleaned = new string((label ?? string.Empty)
            .Select(character => char.IsLetterOrDigit(character) || character is '-' ? character : '-')
            .ToArray());
        return string.IsNullOrEmpty(cleaned) ? "backup" : cleaned;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
