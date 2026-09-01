using DropSort.Application.Repositories;

namespace DropSort.Infrastructure.Persistence.Repositories;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly string _connectionString;
    public SettingsRepository(string connectionString) => _connectionString = connectionString;

    public string? Get(string key)
    {
        Validate(key, nameof(key));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void Set(string key, string value, DateTimeOffset updatedAt)
    {
        Validate(key, nameof(key));
        Validate(value, nameof(value));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value, updated_at) VALUES ($key, $value, $updated)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updated", updatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Delete(string key)
    {
        Validate(key, nameof(key));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static void Validate(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value must be non-empty.", name);
    }
}
