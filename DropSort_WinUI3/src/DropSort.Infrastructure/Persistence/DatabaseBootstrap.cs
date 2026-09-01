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
}
