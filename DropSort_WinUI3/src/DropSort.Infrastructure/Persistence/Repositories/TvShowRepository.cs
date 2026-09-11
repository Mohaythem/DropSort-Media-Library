using System.Collections.Immutable;
using System.Text.Json;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

/// <summary>
/// The TV half of the catalog. Same conventions as the movie repositories: the caller supplies the
/// connection (and the transaction when it is part of a unit of work), timestamps are round-trip
/// strings, and identity is the row id - never a position in a list.
/// </summary>
public sealed class TvShowRepository(SqliteConnection connection, SqliteTransaction? transaction = null)
    : ITvShowRepository
{
    private const string Columns =
        "id, provider, external_id, title, sort_title, original_title, year, overview, genres, "
        + "poster_path, metadata_status, date_added, created_at, updated_at, backdrop_path, rating, tagline";

    public TvShow? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM tv_shows WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command);
    }

    public TvShow? GetBySortTitle(string sortTitle)
    {
        var normalized = TvShowCatalogData.NormalizeTitle(sortTitle);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var command = Command($"SELECT {Columns} FROM tv_shows WHERE sort_title = $sortTitle;");
        command.Parameters.AddWithValue("$sortTitle", normalized);
        return ReadOne(command);
    }

    public TvShow? GetByExternalId(string provider, string externalId)
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_shows WHERE provider = $provider AND external_id = $externalId;");
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$externalId", externalId);
        return ReadOne(command);
    }

    public IReadOnlyList<TvShow> ListAll()
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_shows ORDER BY datetime(date_added) DESC, id DESC;");
        return ReadMany(command);
    }

    public int CountAll()
    {
        using var command = Command("SELECT COUNT(*) FROM tv_shows;");
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public TvShow Create(TvShowCatalogData data, DateTimeOffset now)
    {
        using var command = Command($"""
            INSERT INTO tv_shows (
                provider, external_id, title, sort_title, original_title, year, overview, genres,
                poster_path, metadata_status, date_added, created_at, updated_at,
                backdrop_path, rating, tagline
            )
            VALUES (
                $provider, $externalId, $title, $sortTitle, $originalTitle, $year, $overview, $genres,
                $poster, $status, $now, $now, $now,
                $backdrop, $rating, $tagline
            )
            RETURNING {Columns};
            """);
        AddShowParameters(command, data, now);
        return ReadOne(command) ?? throw new InvalidOperationException("Inserting a TV show returned no row.");
    }

    public TvShow UpdateMetadata(int id, TvShowCatalogData data, DateTimeOffset now)
    {
        using var command = Command($"""
            UPDATE tv_shows
               SET provider = $provider,
                   external_id = $externalId,
                   title = $title,
                   sort_title = $sortTitle,
                   original_title = $originalTitle,
                   year = $year,
                   overview = $overview,
                   genres = $genres,
                   poster_path = $poster,
                   metadata_status = $status,
                   updated_at = $now,
                   backdrop_path = $backdrop,
                   rating = $rating,
                   tagline = $tagline
             WHERE id = $id
            RETURNING {Columns};
            """);
        AddShowParameters(command, data, now);
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command) ?? throw new KeyNotFoundException($"TV show {id} was not found.");
    }

    public void Delete(int id)
    {
        using var command = Command("DELETE FROM tv_shows WHERE id = $id;");
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

    private static void AddShowParameters(SqliteCommand command, TvShowCatalogData data, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$provider", data.Provider ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$externalId", data.ExternalId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$title", data.Title);
        command.Parameters.AddWithValue("$sortTitle", data.SortTitle);
        command.Parameters.AddWithValue("$originalTitle", data.OriginalTitle ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$year", data.Year ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$overview", data.Overview ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$genres", JsonSerializer.Serialize(data.Genres));
        command.Parameters.AddWithValue("$poster", data.PosterReference ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", EnumText.MetadataStatus(data.MetadataStatus));
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.Parameters.AddWithValue("$backdrop", data.BackdropReference ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rating", data.Rating ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$tagline", data.Tagline ?? (object)DBNull.Value);
    }

    private static TvShow? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<TvShow> ReadMany(SqliteCommand command)
    {
        var result = new List<TvShow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    internal static TvShow Map(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        new TvShowCatalogData(
            provider: reader.IsDBNull(1) ? null : reader.GetString(1),
            externalId: reader.IsDBNull(2) ? null : reader.GetString(2),
            title: reader.GetString(3),
            originalTitle: reader.IsDBNull(5) ? null : reader.GetString(5),
            year: reader.IsDBNull(6) ? null : reader.GetInt32(6),
            overview: reader.IsDBNull(7) ? null : reader.GetString(7),
            genres: [.. JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? []],
            posterReference: reader.IsDBNull(9) ? null : reader.GetString(9),
            metadataStatus: EnumText.ParseMetadataStatus(reader.GetString(10)),
            rating: reader.IsDBNull(15) ? null : reader.GetDouble(15),
            backdropReference: reader.IsDBNull(14) ? null : reader.GetString(14),
            tagline: reader.IsDBNull(16) ? null : reader.GetString(16)),
        MovieRepository.ParseTimestamp(reader.GetString(11)),
        MovieRepository.ParseTimestamp(reader.GetString(12)),
        MovieRepository.ParseTimestamp(reader.GetString(13)));
}
