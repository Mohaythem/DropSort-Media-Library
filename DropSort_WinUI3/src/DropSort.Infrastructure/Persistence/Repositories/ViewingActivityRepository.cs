using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

/// <summary>Active viewings are separate from the append-only completed-watch event stream.</summary>
public sealed class ViewingActivityRepository(string connectionString) : IViewingActivityRepository
{
    public IReadOnlyList<ActiveViewingItem> ListActive(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        using var connection = SqliteConnections.Open(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = ActiveQuery + " ORDER BY julianday(a.started_at) DESC, a.id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        return ReadActive(command);
    }

    public ActiveViewingItem RecordSuccessfulPlay(string path, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Path must be absolute.", nameof(path));

        using var gate = DatabaseWriteGate.Enter(connectionString);
        using var connection = SqliteConnections.Open(connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        int mediaId;
        int? movieId;
        using (var find = Command(connection, transaction,
            "SELECT id, movie_id FROM media_files WHERE path_key = $key AND status = 'PRESENT';"))
        {
            find.Parameters.AddWithValue("$key", MediaFileRepository.PathKey(path));
            using var reader = find.ExecuteReader();
            if (!reader.Read()) throw new KeyNotFoundException("The registered present media file was not found.");
            mediaId = reader.GetInt32(0);
            movieId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        }

        int? episodeId;
        using (var episode = Command(connection, transaction,
            "SELECT episode_id FROM episode_media_files WHERE media_file_id = $media;"))
        {
            episode.Parameters.AddWithValue("$media", mediaId);
            episodeId = episode.ExecuteScalar() is long value ? checked((int)value) : null;
        }
        if (movieId is null && episodeId is null)
            throw new InvalidOperationException("The file has no movie or episode association.");
        if (movieId is not null && episodeId is not null)
            throw new InvalidDataException("The file has conflicting movie and episode associations.");

        using (var upsert = Command(connection, transaction, movieId is not null ? MovieUpsert : EpisodeUpsert))
        {
            upsert.Parameters.AddWithValue("$id", (object?)movieId ?? episodeId!.Value);
            upsert.Parameters.AddWithValue("$media", mediaId);
            upsert.Parameters.AddWithValue("$path", path);
            upsert.Parameters.AddWithValue("$started", startedAt.ToUniversalTime().ToString("O"));
            upsert.ExecuteNonQuery();
        }
        using var result = Command(connection, transaction,
            ActiveQuery + (movieId is not null ? " AND a.movie_id = $id;" : " AND a.episode_id = $id;"));
        result.Parameters.AddWithValue("$id", (object?)movieId ?? episodeId!.Value);
        var item = ReadActive(result).Single();
        transaction.Commit();
        return item;
    }

    public IReadOnlyList<WatchHistoryEntry> ListRecentCompleted(int limit, int offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        using var connection = SqliteConnections.Open(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = RecentQuery;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var result = new List<WatchHistoryEntry>();
        while (reader.Read())
        {
            result.Add(new WatchHistoryEntry(
                reader.GetInt32(0), reader.GetString(1) == "MOVIE" ? WatchEventMediaKind.Movie : WatchEventMediaKind.Episode,
                Int(reader, 2), Int(reader, 3), Int(reader, 4), reader.GetString(5), Int(reader, 6),
                Text(reader, 7), Int(reader, 8), Int(reader, 9), Text(reader, 10), Text(reader, 11), Text(reader, 12),
                MovieRepository.ParseTimestamp(reader.GetString(13)),
                MovieRepository.ParseTimestamp(reader.GetString(14)), reader.GetInt32(15) != 0));
        }
        return result;
    }

    private static IReadOnlyList<ActiveViewingItem> ReadActive(SqliteCommand command)
    {
        var result = new List<ActiveViewingItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ActiveViewingItem(Int(reader, 0), Int(reader, 1), Int(reader, 2), reader.GetString(3),
                Int(reader, 4), Int(reader, 5), Int(reader, 6), Text(reader, 7), Text(reader, 8),
                MovieRepository.ParseTimestamp(reader.GetString(9)), Text(reader, 10), Text(reader, 11), Text(reader, 12)));
        }
        return result;
    }

    private static int? Int(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    // Resolve current catalog locations, so a supported rename/move cannot leave Home playing a stale path.
    private const string ActiveQuery = """
        SELECT a.movie_id, se.show_id, a.episode_id, COALESCE(m.title, s.title, a.title_snapshot),
               COALESCE(m.year, s.year, a.year_snapshot), COALESCE(se.season_number, a.season_number_snapshot),
               COALESCE(e.episode_number, a.episode_number_snapshot), COALESCE(e.title, a.episode_title_snapshot),
               COALESCE(CASE WHEN played.status = 'PRESENT' THEN played.current_path END,
               (SELECT mf.current_path FROM media_files mf
                 WHERE (a.media_kind = 'MOVIE' AND mf.movie_id = a.movie_id)
                    OR (a.media_kind = 'EPISODE' AND EXISTS (
                        SELECT 1 FROM episode_media_files ef WHERE ef.episode_id = a.episode_id AND ef.media_file_id = mf.id))
                 ORDER BY CASE WHEN mf.status = 'PRESENT' THEN 0 ELSE 1 END,
                          mf.id LIMIT 1)),
               a.started_at, a.artwork_reference, a.backdrop_reference, a.poster_reference
          FROM active_viewings a
          LEFT JOIN movies m ON m.id = a.movie_id
          LEFT JOIN tv_episodes e ON e.id = a.episode_id
          LEFT JOIN tv_seasons se ON se.id = e.season_id
          LEFT JOIN tv_shows s ON s.id = se.show_id
          LEFT JOIN media_files played ON played.id = a.media_file_id
            AND (played.movie_id = a.movie_id OR EXISTS (
                SELECT 1 FROM episode_media_files ef WHERE ef.episode_id = a.episode_id AND ef.media_file_id = played.id))
         WHERE 1 = 1
        """;

    private const string MovieUpsert = """
        INSERT INTO active_viewings(media_kind, movie_id, media_file_id, title_snapshot, year_snapshot,
                                    path, started_at, artwork_reference, poster_reference)
        SELECT 'MOVIE', id, $media, title, year, $path, $started, COALESCE(backdrop_path, poster_path), poster_path
          FROM movies WHERE id = $id
        ON CONFLICT(movie_id) WHERE movie_id IS NOT NULL DO UPDATE SET
            media_file_id = excluded.media_file_id, path = excluded.path, started_at = excluded.started_at,
            title_snapshot = excluded.title_snapshot, year_snapshot = excluded.year_snapshot,
            artwork_reference = excluded.artwork_reference, poster_reference = excluded.poster_reference;
        """;

    private const string EpisodeUpsert = """
        INSERT INTO active_viewings(media_kind, episode_id, media_file_id, title_snapshot, year_snapshot,
                                    season_number_snapshot, episode_number_snapshot, episode_title_snapshot,
                                    path, started_at, artwork_reference, backdrop_reference, poster_reference)
        SELECT 'EPISODE', e.id, $media, s.title, s.year, se.season_number, e.episode_number,
               COALESCE(e.title, CAST(e.episode_number AS TEXT)), $path, $started, e.still_path, s.backdrop_path, s.poster_path
          FROM tv_episodes e JOIN tv_seasons se ON se.id = e.season_id JOIN tv_shows s ON s.id = se.show_id WHERE e.id = $id
        ON CONFLICT(episode_id) WHERE episode_id IS NOT NULL DO UPDATE SET
            media_file_id = excluded.media_file_id, path = excluded.path, started_at = excluded.started_at,
            title_snapshot = excluded.title_snapshot, year_snapshot = excluded.year_snapshot,
            season_number_snapshot = excluded.season_number_snapshot, episode_number_snapshot = excluded.episode_number_snapshot,
            episode_title_snapshot = excluded.episode_title_snapshot, artwork_reference = excluded.artwork_reference,
            backdrop_reference = excluded.backdrop_reference, poster_reference = excluded.poster_reference;
        """;

    private const string RecentQuery = """
        WITH ranked AS (
            SELECT w.*, ROW_NUMBER() OVER (
                PARTITION BY CASE WHEN w.media_kind = 'MOVIE' AND w.movie_id IS NOT NULL THEN 'M:' || w.movie_id
                                  WHEN w.media_kind = 'EPISODE' AND w.episode_id IS NOT NULL THEN 'E:' || w.episode_id
                                  ELSE 'X:' || w.id END
                ORDER BY julianday(w.watched_at) DESC, w.id DESC) AS viewing_rank
              FROM watch_events w)
        SELECT w.id, w.media_kind, w.movie_id, w.episode_id, se.show_id,
               COALESCE(m.title, w.title_snapshot), COALESCE(m.year, w.year_snapshot),
               COALESCE(s.title, w.show_title_snapshot), COALESCE(se.season_number, w.season_number_snapshot),
               COALESCE(e.episode_number, w.episode_number_snapshot),
               COALESCE(w.artwork_reference, e.still_path, m.backdrop_path),
               COALESCE(w.backdrop_reference, s.backdrop_path), COALESCE(w.poster_reference, m.poster_path, s.poster_path),
               w.watched_at, w.created_at,
               CASE WHEN w.media_kind = 'MOVIE' THEN
                   EXISTS(SELECT 1 FROM media_files f WHERE f.movie_id = w.movie_id AND f.status = 'PRESENT')
               ELSE EXISTS(SELECT 1 FROM episode_media_files ef JOIN media_files f ON f.id = ef.media_file_id
                            WHERE ef.episode_id = w.episode_id AND f.status = 'PRESENT') END
          FROM ranked w
          LEFT JOIN movies m ON m.id = w.movie_id
          LEFT JOIN tv_episodes e ON e.id = w.episode_id
          LEFT JOIN tv_seasons se ON se.id = e.season_id
          LEFT JOIN tv_shows s ON s.id = se.show_id
         WHERE w.viewing_rank = 1
         ORDER BY julianday(w.watched_at) DESC, w.id DESC LIMIT $limit OFFSET $offset;
        """;
}
