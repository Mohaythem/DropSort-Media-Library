using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

/// <summary>Seasons of a show. (show_id, season_number) is unique, which is what makes a rescan idempotent.</summary>
public sealed class TvSeasonRepository(SqliteConnection connection, SqliteTransaction? transaction = null)
    : ITvSeasonRepository
{
    private const string Columns = "id, show_id, season_number, title, overview, created_at, updated_at, poster_path, air_date, external_id";

    public TvSeason? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM tv_seasons WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command);
    }

    public TvSeason? GetByNumber(int showId, int seasonNumber)
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_seasons WHERE show_id = $showId AND season_number = $number;");
        command.Parameters.AddWithValue("$showId", showId);
        command.Parameters.AddWithValue("$number", seasonNumber);
        return ReadOne(command);
    }

    public IReadOnlyList<TvSeason> ListByShow(int showId)
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_seasons WHERE show_id = $showId ORDER BY season_number;");
        command.Parameters.AddWithValue("$showId", showId);
        return ReadMany(command);
    }

    public TvSeason Create(int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now)
    {
        TvSeason.ValidateSeasonNumber(seasonNumber);

        using var command = Command($"""
            INSERT INTO tv_seasons (show_id, season_number, title, overview, created_at, updated_at)
            VALUES ($showId, $number, $title, $overview, $now, $now)
            RETURNING {Columns};
            """);
        command.Parameters.AddWithValue("$showId", showId);
        command.Parameters.AddWithValue("$number", seasonNumber);
        command.Parameters.AddWithValue("$title", title ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$overview", overview ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadOne(command) ?? throw new InvalidOperationException("Inserting a season returned no row.");
    }

    public TvSeason UpdateMetadata(
        int id,
        string? title,
        string? overview,
        string? posterPath,
        string? airDate,
        string? externalId,
        DateTimeOffset now)
    {
        using var command = Command($"""
            UPDATE tv_seasons
               SET title = $title,
                   overview = $overview,
                   poster_path = $posterPath,
                   air_date = $airDate,
                   external_id = $externalId,
                   updated_at = $now
             WHERE id = $id
            RETURNING {Columns};
            """);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$overview", overview ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$posterPath", posterPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$airDate", airDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$externalId", externalId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadOne(command) ?? throw new KeyNotFoundException($"TV season {id} was not found.");
    }

    public void Delete(int id)
    {
        using var command = Command("DELETE FROM tv_seasons WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static TvSeason? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<TvSeason> ReadMany(SqliteCommand command)
    {
        var result = new List<TvSeason>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    internal static TvSeason Map(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        MovieRepository.ParseTimestamp(reader.GetString(5)),
        MovieRepository.ParseTimestamp(reader.GetString(6)),
        posterReference: reader.IsDBNull(7) ? null : reader.GetString(7),
        airDate: reader.IsDBNull(8) ? null : reader.GetString(8),
        externalId: reader.IsDBNull(9) ? null : reader.GetString(9));
}
