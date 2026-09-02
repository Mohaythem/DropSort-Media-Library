using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence;

/// <summary>
/// Proves SQLite dependency initializes correctly.
/// In Phase 2 this will become the real migration runner and connection factory.
/// </summary>
public static class DatabaseBootstrap
{
    /// <summary>
    /// Opens an in-memory SQLite connection and verifies the engine responds.
    /// Returns the SQLite version string on success.
    /// </summary>
    public static string VerifySqliteConnection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        var version = command.ExecuteScalar()?.ToString()
            ?? throw new InvalidOperationException("SQLite version query returned null.");

        return version;
    }

    /// <summary>
    /// Returns every pooled connection to the operating system.
    /// <para>
    /// Microsoft.Data.Sqlite pools connections, so closing one does not necessarily close the file
    /// handle behind it. While any handle is open SQLite cannot check-point and remove the write-ahead
    /// log, which leaves a stub database file next to a growing <c>-wal</c>. A host calls this once,
    /// after it has disposed everything it owns, to make the last close a real last close.
    /// </para>
    /// </summary>
    public static void ReleasePooledConnections() => SqliteConnection.ClearAllPools();
}
