using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using TBDMovies.Application.Trakt;
using Microsoft.Data.Sqlite;

namespace TBDMovies.Infrastructure.Persistence.Repositories;

/// <summary>
/// Isolated repository for Trakt offline resource snapshot persistence.
/// Manages table trakt_resource_cache with atomic single-statement upserts under DatabaseWriteGate.
/// Never mutates local catalog, media files, or watched status.
/// </summary>
public sealed class TraktCacheRepository : ITraktCacheRepository
{
    private readonly string _connectionString;

    public TraktCacheRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must be non-empty.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public TraktCacheEntry? Get(string accountId, string resourceKey)
    {
        ValidateKey(accountId, nameof(accountId));
        ValidateKey(resourceKey, nameof(resourceKey));

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, resource_key, payload_json, succeeded_at, attempted_at, error_code
            FROM trakt_resource_cache
            WHERE account_id = $account_id AND resource_key = $resource_key;
            """;
        command.Parameters.AddWithValue("$account_id", accountId.Trim());
        command.Parameters.AddWithValue("$resource_key", resourceKey.Trim());

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return ReadEntry(reader);
    }

    public IReadOnlyList<TraktCacheSummary> List(string accountId)
    {
        ValidateKey(accountId, nameof(accountId));

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_id, resource_key, succeeded_at, attempted_at, error_code
            FROM trakt_resource_cache
            WHERE account_id = $account_id
            ORDER BY resource_key ASC;
            """;
        command.Parameters.AddWithValue("$account_id", accountId.Trim());

        using var reader = command.ExecuteReader();
        var entries = new List<TraktCacheSummary>();
        while (reader.Read())
        {
            entries.Add(new TraktCacheSummary(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return entries;
    }

    public void SaveComplete(
        string accountId,
        string resourceKey,
        string payloadJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(accountId, nameof(accountId));
        ValidateKey(resourceKey, nameof(resourceKey));
        ValidateJson(payloadJson);

        using var gate = DatabaseWriteGate.Enter(_connectionString);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trakt_resource_cache (
                account_id, resource_key, payload_json, succeeded_at, attempted_at, error_code
            ) VALUES (
                $account_id, $resource_key, $payload_json, $succeeded_at, $attempted_at, NULL
            )
            ON CONFLICT(account_id, resource_key) DO UPDATE SET
                payload_json = excluded.payload_json,
                succeeded_at = excluded.succeeded_at,
                attempted_at = excluded.attempted_at,
                error_code = NULL;
            """;
        command.Parameters.AddWithValue("$account_id", accountId.Trim());
        command.Parameters.AddWithValue("$resource_key", resourceKey.Trim());
        command.Parameters.AddWithValue("$payload_json", payloadJson);
        command.Parameters.AddWithValue("$succeeded_at", now.ToString("O"));
        command.Parameters.AddWithValue("$attempted_at", now.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void RecordFailure(
        string accountId,
        string resourceKey,
        TraktCacheErrorCode errorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(accountId, nameof(accountId));
        ValidateKey(resourceKey, nameof(resourceKey));
        if (!Enum.IsDefined(errorCode) || errorCode == TraktCacheErrorCode.None)
            throw new ArgumentException("A defined non-None failure error code is required.", nameof(errorCode));

        using var gate = DatabaseWriteGate.Enter(_connectionString);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trakt_resource_cache (
                account_id, resource_key, payload_json, succeeded_at, attempted_at, error_code
            ) VALUES (
                $account_id, $resource_key, NULL, NULL, $attempted_at, $error_code
            )
            ON CONFLICT(account_id, resource_key) DO UPDATE SET
                attempted_at = excluded.attempted_at,
                error_code = excluded.error_code;
            """;
        command.Parameters.AddWithValue("$account_id", accountId.Trim());
        command.Parameters.AddWithValue("$resource_key", resourceKey.Trim());
        command.Parameters.AddWithValue("$attempted_at", now.ToString("O"));
        command.Parameters.AddWithValue("$error_code", errorCode.ToString());

        command.ExecuteNonQuery();
    }

    public void DeleteAccount(string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(accountId, nameof(accountId));

        using var gate = DatabaseWriteGate.Enter(_connectionString);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM trakt_resource_cache WHERE account_id = $account_id;";
        command.Parameters.AddWithValue("$account_id", accountId.Trim());

        command.ExecuteNonQuery();
    }

    private static void ValidateKey(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty and not whitespace only.", name);
    }

    private static void ValidateJson(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("Payload JSON must be non-empty.", nameof(payloadJson));

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Payload is not valid JSON.", nameof(payloadJson), ex);
        }
    }

    private static TraktCacheEntry ReadEntry(SqliteDataReader reader)
    {
        var accountId = reader.GetString(0);
        var resourceKey = reader.GetString(1);
        var payloadJson = reader.IsDBNull(2) ? null : reader.GetString(2);
        DateTimeOffset? succeededAt = reader.IsDBNull(3)
            ? null
            : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var attemptedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var errorCode = reader.IsDBNull(5) ? null : reader.GetString(5);

        return new TraktCacheEntry(accountId, resourceKey, payloadJson, succeededAt, attemptedAt, errorCode);
    }
}
