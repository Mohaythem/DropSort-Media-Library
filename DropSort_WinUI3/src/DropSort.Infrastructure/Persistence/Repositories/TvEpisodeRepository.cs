using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

/// <summary>
/// Episodes and the files registered against them. (season_id, episode_number) is unique, and a media
/// file can belong to at most one episode - <c>episode_media_files</c> has the file as its primary key -
/// while an episode can carry several files.
/// </summary>
public sealed class TvEpisodeRepository(SqliteConnection connection, SqliteTransaction? transaction = null)
    : ITvEpisodeRepository
{
    private const string Columns =
        "id, season_id, episode_number, title, overview, runtime_minutes, air_date, created_at, updated_at, still_path, rating, external_id";

    private const string PrefixedColumns =
        "e.id, e.season_id, e.episode_number, e.title, e.overview, e.runtime_minutes, e.air_date, e.created_at, e.updated_at, e.still_path, e.rating, e.external_id";

    public TvEpisode? GetById(int id)
    {
        using var command = Command($"SELECT {Columns} FROM tv_episodes WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command);
    }

    public TvEpisode? GetByNumber(int seasonId, int episodeNumber)
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_episodes WHERE season_id = $seasonId AND episode_number = $number;");
        command.Parameters.AddWithValue("$seasonId", seasonId);
        command.Parameters.AddWithValue("$number", episodeNumber);
        return ReadOne(command);
    }

    public IReadOnlyList<TvEpisode> ListBySeason(int seasonId)
    {
        using var command = Command(
            $"SELECT {Columns} FROM tv_episodes WHERE season_id = $seasonId ORDER BY episode_number;");
        command.Parameters.AddWithValue("$seasonId", seasonId);
        return ReadMany(command);
    }

    public IReadOnlyList<TvEpisode> ListByShow(int showId)
    {
        using var command = Command($"""
            SELECT {PrefixedColumns}
              FROM tv_episodes e
              JOIN tv_seasons s ON s.id = e.season_id
             WHERE s.show_id = $showId
             ORDER BY s.season_number, e.episode_number;
            """);
        command.Parameters.AddWithValue("$showId", showId);
        return ReadMany(command);
    }

    public TvEpisode Create(
        int seasonId,
        int episodeNumber,
        string? title,
        string? overview,
        int? runtimeMinutes,
        DateTimeOffset? airDate,
        DateTimeOffset now)
    {
        TvEpisode.ValidateEpisodeNumber(episodeNumber);

        using var command = Command($"""
            INSERT INTO tv_episodes (
                season_id, episode_number, title, overview, runtime_minutes, air_date, created_at, updated_at
            )
            VALUES ($seasonId, $number, $title, $overview, $runtime, $airDate, $now, $now)
            RETURNING {Columns};
            """);
        command.Parameters.AddWithValue("$seasonId", seasonId);
        command.Parameters.AddWithValue("$number", episodeNumber);
        command.Parameters.AddWithValue("$title", title ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$overview", overview ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$runtime", runtimeMinutes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$airDate", airDate?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadOne(command) ?? throw new InvalidOperationException("Inserting an episode returned no row.");
    }

    public TvEpisode UpdateMetadata(
        int id,
        string? title,
        string? overview,
        int? runtimeMinutes,
        string? airDate,
        double? rating,
        string? stillPath,
        string? externalId,
        DateTimeOffset now)
    {
        using var command = Command($"""
            UPDATE tv_episodes
               SET title = $title,
                   overview = $overview,
                   runtime_minutes = $runtime,
                   air_date = $airDate,
                   still_path = $stillPath,
                   rating = $rating,
                   external_id = $externalId,
                   updated_at = $now
             WHERE id = $id
            RETURNING {Columns};
            """);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$title", title ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$overview", overview ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$runtime", runtimeMinutes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$airDate", airDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$stillPath", stillPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$rating", rating ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$externalId", externalId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadOne(command) ?? throw new KeyNotFoundException($"TV episode {id} was not found.");
    }

    public void Delete(int id)
    {
        using var command = Command("DELETE FROM tv_episodes WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void LinkMediaFile(int episodeId, int mediaFileId, DateTimeOffset now)
    {
        var owner = GetEpisodeIdForMediaFile(mediaFileId);

        if (owner == episodeId)
        {
            return;
        }

        if (owner is not null)
        {
            throw new InvalidOperationException(
                $"Media file {mediaFileId} is already registered against episode {owner}.");
        }

        using var command = Command("""
            INSERT INTO episode_media_files (media_file_id, episode_id, linked_at)
            VALUES ($mediaFileId, $episodeId, $now);
            """);
        command.Parameters.AddWithValue("$mediaFileId", mediaFileId);
        command.Parameters.AddWithValue("$episodeId", episodeId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UnlinkMediaFile(int mediaFileId)
    {
        using var command = Command("DELETE FROM episode_media_files WHERE media_file_id = $mediaFileId;");
        command.Parameters.AddWithValue("$mediaFileId", mediaFileId);
        command.ExecuteNonQuery();
    }

    public int? GetEpisodeIdForMediaFile(int mediaFileId)
    {
        using var command = Command(
            "SELECT episode_id FROM episode_media_files WHERE media_file_id = $mediaFileId;");
        command.Parameters.AddWithValue("$mediaFileId", mediaFileId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    public IReadOnlyList<MediaFile> ListMediaFiles(int episodeId)
    {
        using var command = Command($"""
            SELECT {MediaFileColumns}
              FROM media_files f
              JOIN episode_media_files l ON l.media_file_id = f.id
             WHERE l.episode_id = $episodeId
             ORDER BY f.id;
            """);
        command.Parameters.AddWithValue("$episodeId", episodeId);

        var result = new List<MediaFile>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(MediaFileRepository.Map(reader));
        }

        return result;
    }

    public IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> ListMediaFilesByShow(int showId)
    {
        using var command = Command($"""
            SELECT l.episode_id, {MediaFileColumns}
              FROM media_files f
              JOIN episode_media_files l ON l.media_file_id = f.id
              JOIN tv_episodes e ON e.id = l.episode_id
              JOIN tv_seasons s ON s.id = e.season_id
             WHERE s.show_id = $showId
             ORDER BY l.episode_id, f.id;
            """);
        command.Parameters.AddWithValue("$showId", showId);

        var result = new Dictionary<int, List<MediaFile>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var episodeId = reader.GetInt32(0);

            if (!result.TryGetValue(episodeId, out var files))
            {
                files = [];
                result[episodeId] = files;
            }

            files.Add(MediaFileRepository.MapOffset(reader, 1));
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<MediaFile>)pair.Value);
    }

    /// <summary>The media-file columns in the order <see cref="MediaFileRepository.Map" /> expects.</summary>
    private const string MediaFileColumns =
        "f.id, f.movie_id, f.current_path, f.file_size, f.extension, f.resolution, f.codec, f.source, "
        + "f.status, f.discovered_at, f.last_seen_at";

    private SqliteCommand Command(string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static TvEpisode? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<TvEpisode> ReadMany(SqliteCommand command)
    {
        var result = new List<TvEpisode>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    private static DateTimeOffset? ParseEpisodeAirDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) return parsed;
        if (DateTimeOffset.TryParse(value, out var parsedFallback)) return parsedFallback;
        return null;
    }

    internal static TvEpisode Map(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : ParseEpisodeAirDate(reader.GetString(6)),
        MovieRepository.ParseTimestamp(reader.GetString(7)),
        MovieRepository.ParseTimestamp(reader.GetString(8)),
        stillReference: reader.IsDBNull(9) ? null : reader.GetString(9),
        rating: reader.IsDBNull(10) ? null : reader.GetDouble(10),
        externalId: reader.IsDBNull(11) ? null : reader.GetString(11));
}
