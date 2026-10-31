using System;
using System.Collections.Generic;
using TBDMovies.Application.Dto;
using TBDMovies.Application.Repositories;
using TBDMovies.Domain.Library.Personal;
using Microsoft.Data.Sqlite;

namespace TBDMovies.Infrastructure.Persistence.Repositories;

public sealed class ShowPersonalLibraryRepository : IShowPersonalRepository
{
    private readonly string _connectionString;

    public ShowPersonalLibraryRepository(string connectionString) => _connectionString = connectionString;

    public ShowPersonalSnapshot GetState(int showId)
    {
        ValidateId(showId, nameof(showId));
        using var connection = SqliteConnections.Open(_connectionString);
        RequireShow(connection, showId, null);
        return ReadState(connection, showId, null);
    }

    public ShowPersonalSnapshot SetFavorite(int showId, bool value, DateTimeOffset now) =>
        SetFlag(showId, value, now, favorite: true);

    public ShowPersonalSnapshot SetWatchLater(int showId, bool value, DateTimeOffset now) =>
        SetFlag(showId, value, now, favorite: false);

    public IReadOnlySet<int> ListShowIds(PersonalLibrarySection section)
    {
        var condition = section switch
        {
            PersonalLibrarySection.Watchlist => "watch_later_added_at IS NOT NULL",
            PersonalLibrarySection.Liked => "is_favorite = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };

        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT show_id FROM tv_show_personal_state WHERE {condition};";
        var ids = new HashSet<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    private ShowPersonalSnapshot SetFlag(int showId, bool value, DateTimeOffset now, bool favorite)
    {
        ValidateId(showId, nameof(showId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireShow(connection, showId, transaction);

        var column = favorite ? "is_favorite" : "watch_later_added_at";
        var assignment = favorite ? "$value" : "CASE WHEN $value = 1 THEN $now ELSE NULL END";
        using (var command = Command(connection, transaction, $"""
            INSERT INTO tv_show_personal_state(show_id, is_favorite, watch_later_added_at, created_at, updated_at)
            VALUES ($show, {(favorite ? "$value" : "0")}, {(favorite ? "NULL" : "CASE WHEN $value = 1 THEN $now ELSE NULL END")}, $now, $now)
            ON CONFLICT(show_id) DO UPDATE SET
                {column} = {assignment},
                updated_at = $now;
            """))
        {
            command.Parameters.AddWithValue("$show", showId);
            command.Parameters.AddWithValue("$value", value ? 1 : 0);
            command.Parameters.AddWithValue("$now", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        using (var cleanup = Command(connection, transaction, "DELETE FROM tv_show_personal_state WHERE show_id = $show AND is_favorite = 0 AND watch_later_added_at IS NULL;"))
        {
            cleanup.Parameters.AddWithValue("$show", showId);
            cleanup.ExecuteNonQuery();
        }

        var state = ReadState(connection, showId, transaction);
        transaction.Commit();
        return state;
    }

    private static ShowPersonalSnapshot ReadState(SqliteConnection connection, int showId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT COALESCE(is_favorite, 0), watch_later_added_at IS NOT NULL FROM tv_shows s LEFT JOIN tv_show_personal_state ps ON ps.show_id = s.id WHERE s.id = $show;");
        command.Parameters.AddWithValue("$show", showId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"TV show {showId} was not found.");
        return new ShowPersonalSnapshot(showId, reader.GetInt32(0) != 0, reader.GetInt32(1) != 0);
    }

    private static void RequireShow(SqliteConnection connection, int showId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM tv_shows WHERE id = $show;");
        command.Parameters.AddWithValue("$show", showId);
        if (command.ExecuteScalar() is null) throw new KeyNotFoundException($"TV show {showId} was not found.");
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void ValidateId(int value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
