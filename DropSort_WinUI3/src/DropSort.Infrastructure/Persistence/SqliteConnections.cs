using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence;

internal static class SqliteConnections
{
    internal static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();
        return connection;
    }
}
