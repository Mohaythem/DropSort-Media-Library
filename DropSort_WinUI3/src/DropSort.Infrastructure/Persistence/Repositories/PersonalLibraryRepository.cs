using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

public sealed class PersonalLibraryRepository : IPersonalLibraryRepository, IWatchHistoryRepository
{
    private readonly string _connectionString;

    public PersonalLibraryRepository(string connectionString) => _connectionString = connectionString;

    public PersonalMovieState GetState(int movieId)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        RequireMovie(connection, movieId, null);
        return ReadState(connection, movieId, null);
    }

    public PersonalMovieState SetPreference(int movieId, PersonalPreference preference, DateTimeOffset now)
    {
        ValidateId(movieId, nameof(movieId));
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        if (preference == PersonalPreference.NoOpinion) return ClearPreference(movieId, now);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        using var command = Command(connection, transaction, """
            INSERT INTO movie_personal_state(movie_id, preference, watchlist_added_at, created_at, updated_at)
            VALUES ($movie, $preference, NULL, $now, $now)
            ON CONFLICT(movie_id) DO UPDATE SET preference = excluded.preference, updated_at = excluded.updated_at;
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$preference", preference == PersonalPreference.Liked ? "LIKED" : "BLACKLISTED");
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        var state = ReadState(connection, movieId, transaction);
        transaction.Commit();
        return state;
    }

    public PersonalMovieState ClearPreference(int movieId, DateTimeOffset now)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        using var command = Command(connection, transaction, """
            DELETE FROM movie_personal_state
             WHERE movie_id = $movie AND watchlist_added_at IS NULL;
            UPDATE movie_personal_state
               SET preference = 'NO_OPINION', updated_at = $now
             WHERE movie_id = $movie;
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        var state = ReadState(connection, movieId, transaction);
        transaction.Commit();
        return state;
    }

    public PersonalMovieState AddToWatchlist(int movieId, DateTimeOffset now)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        using var command = Command(connection, transaction, """
            INSERT INTO movie_personal_state(movie_id, preference, watchlist_added_at, created_at, updated_at)
            VALUES ($movie, 'NO_OPINION', $now, $now, $now)
            ON CONFLICT(movie_id) DO UPDATE SET
                watchlist_added_at = COALESCE(movie_personal_state.watchlist_added_at, excluded.watchlist_added_at),
                updated_at = CASE WHEN movie_personal_state.watchlist_added_at IS NULL
                                  THEN excluded.updated_at ELSE movie_personal_state.updated_at END;
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        var state = ReadState(connection, movieId, transaction);
        transaction.Commit();
        return state;
    }

    public PersonalMovieState RemoveFromWatchlist(int movieId, DateTimeOffset now)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        using var command = Command(connection, transaction, """
            DELETE FROM movie_personal_state
             WHERE movie_id = $movie AND preference = 'NO_OPINION';
            UPDATE movie_personal_state
               SET watchlist_added_at = NULL, updated_at = $now
             WHERE movie_id = $movie;
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        var state = ReadState(connection, movieId, transaction);
        transaction.Commit();
        return state;
    }

    public WatchEvent AddWatchEvent(int movieId, DateTimeOffset watchedAt, DateTimeOffset createdAt)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        using var command = Command(connection, transaction, """
            INSERT INTO watch_events(
                media_kind, movie_id, watched_at, created_at, title_snapshot, year_snapshot,
                artwork_reference, poster_reference)
            SELECT 'MOVIE', id, $watched, $created, title, year,
                   COALESCE(backdrop_path, poster_path), poster_path
              FROM movies WHERE id = $movie;
            SELECT last_insert_rowid();
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$watched", watchedAt.ToString("O"));
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        var id = checked((int)(long)(command.ExecuteScalar() ?? throw new InvalidOperationException("Watch insert returned no ID.")));
        var events = ReadEvents(connection, movieId, transaction);
        transaction.Commit();
        return events.Single(item => item.Id == id);
    }

    public WatchEvent DeleteWatchEvent(int eventId)
    {
        ValidateId(eventId, nameof(eventId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var find = Command(connection, transaction, "SELECT movie_id FROM watch_events WHERE id = $id;");
        find.Parameters.AddWithValue("$id", eventId);
        var movieValue = find.ExecuteScalar();
        if (movieValue is null) throw new KeyNotFoundException($"Watch event {eventId} was not found.");
        var movieId = Convert.ToInt32(movieValue);
        var removed = ReadEvents(connection, movieId, transaction).Single(item => item.Id == eventId);
        using var delete = Command(connection, transaction, "DELETE FROM watch_events WHERE id = $id;");
        delete.Parameters.AddWithValue("$id", eventId);
        delete.ExecuteNonQuery();
        transaction.Commit();
        return removed;
    }

    public IReadOnlyList<WatchEvent> GetWatchEvents(int movieId)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        RequireMovie(connection, movieId, null);
        return ReadEvents(connection, movieId, null);
    }

    public IReadOnlyList<PersonalMovieSummary> ListMovies(PersonalLibrarySection section, int limit = 100, int offset = 0)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var condition = section switch
        {
            PersonalLibrarySection.Watchlist => "ps.watchlist_added_at IS NOT NULL",
            PersonalLibrarySection.ReadyToWatch => "ps.watchlist_added_at IS NOT NULL AND EXISTS (SELECT 1 FROM media_files r WHERE r.movie_id = m.id AND r.status = 'PRESENT') AND NOT EXISTS (SELECT 1 FROM watch_events w WHERE w.movie_id = m.id)",
            PersonalLibrarySection.Liked => "ps.preference = 'LIKED'",
            PersonalLibrarySection.Blacklisted => "ps.preference = 'BLACKLISTED'",
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.id, m.provider, m.external_id, m.title, m.original_title, m.year, m.overview,
                   m.runtime_minutes, m.rating, m.poster_path, m.date_added, m.created_at, m.updated_at,
                   m.genres, m.metadata_status,
                   COUNT(DISTINCT mf.id),
                   COALESCE(SUM(CASE WHEN mf.status = 'MISSING' THEN 1 ELSE 0 END), 0),
                   ps.preference,
                   CASE WHEN ps.watchlist_added_at IS NULL THEN 0 ELSE 1 END,
                   COUNT(DISTINCT we.id),
                   (SELECT recent.watched_at FROM watch_events recent
                     WHERE recent.movie_id = m.id
                     ORDER BY julianday(recent.watched_at) DESC, recent.id DESC LIMIT 1)
              FROM movies m
              JOIN movie_personal_state ps ON ps.movie_id = m.id
              LEFT JOIN media_files mf ON mf.movie_id = m.id
              LEFT JOIN watch_events we ON we.movie_id = m.id
             WHERE {condition}
             GROUP BY m.id, ps.preference, ps.watchlist_added_at
             ORDER BY datetime(m.date_added) DESC, m.id DESC
             LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var result = new List<PersonalMovieSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new PersonalMovieSummary(
                MovieRepository.Map(reader),
                reader.GetInt32(15),
                reader.GetInt32(16),
                ParsePreference(reader.GetString(17)),
                reader.GetInt32(18) != 0,
                reader.GetInt32(19),
                reader.IsDBNull(20) ? null : MovieRepository.ParseTimestamp(reader.GetString(20))));
        }
        return result;
    }

    public WatchHistoryEntry AddEpisodeWatchEvent(int episodeId, DateTimeOffset watchedAt, DateTimeOffset createdAt)
    {
        ValidateId(episodeId, nameof(episodeId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = Command(connection, transaction, """
            INSERT INTO watch_events(
                media_kind, episode_id, watched_at, created_at, title_snapshot, show_title_snapshot,
                season_number_snapshot, episode_number_snapshot, year_snapshot,
                artwork_reference, backdrop_reference, poster_reference)
            SELECT 'EPISODE', e.id, $watched, $created, COALESCE(NULLIF(TRIM(e.title), ''), CAST(e.episode_number AS TEXT)),
                   s.title, se.season_number, e.episode_number, s.year,
                   e.still_path, s.backdrop_path, s.poster_path
              FROM tv_episodes e
              JOIN tv_seasons se ON se.id = e.season_id
              JOIN tv_shows s ON s.id = se.show_id
             WHERE e.id = $episode;
            """);
        command.Parameters.AddWithValue("$episode", episodeId);
        command.Parameters.AddWithValue("$watched", watchedAt.ToString("O"));
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        if (command.ExecuteNonQuery() != 1)
            throw new KeyNotFoundException($"Episode {episodeId} was not found.");
        using var idCommand = Command(connection, transaction, "SELECT last_insert_rowid();");
        var id = checked(Convert.ToInt32((long)(idCommand.ExecuteScalar() ?? throw new InvalidOperationException("Watch insert returned no ID."))));
        var entry = ReadHistory(connection, transaction, id).Single();
        transaction.Commit();
        return entry;
    }

    public IReadOnlyList<WatchHistoryEntry> ListRecent(int limit = 50, int offset = 0)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null, """
            SELECT id FROM watch_events
             ORDER BY julianday(watched_at) DESC, id DESC
             LIMIT $limit OFFSET $offset;
            """);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var ids = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids.SelectMany(id => ReadHistory(connection, null, id)).ToList();
    }

    public IReadOnlySet<int> GetWatchedEpisodeIds(int showId)
    {
        ValidateId(showId, nameof(showId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null, """
            SELECT DISTINCT w.episode_id
              FROM watch_events w
              JOIN tv_episodes e ON e.id = w.episode_id
              JOIN tv_seasons se ON se.id = e.season_id
             WHERE w.media_kind = 'EPISODE' AND se.show_id = $show AND w.episode_id IS NOT NULL;
            """);
        command.Parameters.AddWithValue("$show", showId);
        var ids = new HashSet<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    private static IReadOnlyList<WatchHistoryEntry> ReadHistory(SqliteConnection connection, SqliteTransaction? transaction, int? eventId = null)
    {
        using var command = Command(connection, transaction, """
            SELECT w.id, w.media_kind, w.movie_id, w.episode_id, w.title_snapshot, w.year_snapshot,
                   w.show_title_snapshot, w.season_number_snapshot, w.episode_number_snapshot,
                   w.artwork_reference, w.backdrop_reference, w.poster_reference,
                   w.watched_at, w.created_at,
                   CASE WHEN w.media_kind = 'MOVIE' THEN
                        CASE WHEN m.id IS NOT NULL AND EXISTS (SELECT 1 FROM media_files mf WHERE mf.movie_id = m.id AND mf.status = 'PRESENT') THEN 1 ELSE 0 END
                        ELSE CASE WHEN e.id IS NOT NULL AND EXISTS (
                            SELECT 1 FROM episode_media_files emf JOIN media_files mf ON mf.id = emf.media_file_id
                             WHERE emf.episode_id = e.id AND mf.status = 'PRESENT') THEN 1 ELSE 0 END END AS available,
                   COALESCE(m.title, w.title_snapshot), COALESCE(m.year, w.year_snapshot),
                   se.show_id,
                   COALESCE(s.title, w.show_title_snapshot), COALESCE(e.episode_number, w.episode_number_snapshot),
                   COALESCE(w.artwork_reference, e.still_path, m.backdrop_path),
                   COALESCE(w.backdrop_reference, s.backdrop_path),
                   COALESCE(w.poster_reference, m.poster_path, s.poster_path)
              FROM watch_events w
              LEFT JOIN movies m ON m.id = w.movie_id
              LEFT JOIN tv_episodes e ON e.id = w.episode_id
              LEFT JOIN tv_seasons se ON se.id = e.season_id
              LEFT JOIN tv_shows s ON s.id = se.show_id
             WHERE ($event IS NULL OR w.id = $event)
             ORDER BY julianday(w.watched_at) DESC, w.id DESC;
            """);
        command.Parameters.AddWithValue("$event", eventId is null ? DBNull.Value : eventId.Value);
        var result = new List<WatchHistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new WatchHistoryEntry(
                reader.GetInt32(0),
                reader.GetString(1) == "MOVIE" ? WatchEventMediaKind.Movie : WatchEventMediaKind.Episode,
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(19) ? null : reader.GetInt32(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                MovieRepository.ParseTimestamp(reader.GetString(12)),
                MovieRepository.ParseTimestamp(reader.GetString(13)),
                reader.GetInt32(14) != 0));
        }
        return result;
    }

    private static PersonalMovieState ReadState(SqliteConnection connection, int movieId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, """
            SELECT ps.preference, ps.watchlist_added_at, ps.created_at, ps.updated_at,
                   COUNT(we.id),
                   (SELECT recent.watched_at FROM watch_events recent
                     WHERE recent.movie_id = m.id
                     ORDER BY julianday(recent.watched_at) DESC, recent.id DESC LIMIT 1)
              FROM movies m
              LEFT JOIN movie_personal_state ps ON ps.movie_id = m.id
              LEFT JOIN watch_events we ON we.movie_id = m.id
             WHERE m.id = $movie
             GROUP BY m.id, ps.preference, ps.watchlist_added_at, ps.created_at, ps.updated_at;
            """);
        command.Parameters.AddWithValue("$movie", movieId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"Movie {movieId} was not found.");
        return new PersonalMovieState(
            movieId,
            reader.IsDBNull(0) ? PersonalPreference.NoOpinion : ParsePreference(reader.GetString(0)),
            reader.IsDBNull(1) ? null : MovieRepository.ParseTimestamp(reader.GetString(1)),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : MovieRepository.ParseTimestamp(reader.GetString(5)),
            reader.IsDBNull(2) ? null : MovieRepository.ParseTimestamp(reader.GetString(2)),
            reader.IsDBNull(3) ? null : MovieRepository.ParseTimestamp(reader.GetString(3)));
    }

    private static IReadOnlyList<WatchEvent> ReadEvents(SqliteConnection connection, int movieId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction,
            "SELECT id, movie_id, watched_at, created_at FROM watch_events WHERE movie_id = $movie ORDER BY julianday(watched_at), id;");
        command.Parameters.AddWithValue("$movie", movieId);
        var result = new List<WatchEvent>();
        using var reader = command.ExecuteReader();
        var first = true;
        while (reader.Read())
        {
            result.Add(new WatchEvent(
                reader.GetInt32(0), reader.GetInt32(1), MovieRepository.ParseTimestamp(reader.GetString(2)),
                !first, MovieRepository.ParseTimestamp(reader.GetString(3))));
            first = false;
        }
        return result;
    }

    private static void RequireMovie(SqliteConnection connection, int movieId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM movies WHERE id = $movie;");
        command.Parameters.AddWithValue("$movie", movieId);
        if (command.ExecuteScalar() is null) throw new KeyNotFoundException($"Movie {movieId} was not found.");
    }

    private static PersonalPreference ParsePreference(string value) => value switch
    {
        "NO_OPINION" => PersonalPreference.NoOpinion,
        "LIKED" => PersonalPreference.Liked,
        "BLACKLISTED" => PersonalPreference.Blacklisted,
        _ => throw new InvalidDataException($"Unknown personal preference '{value}'.")
    };

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
