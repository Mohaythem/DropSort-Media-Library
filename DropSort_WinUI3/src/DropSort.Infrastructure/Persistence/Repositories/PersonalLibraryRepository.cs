using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

public sealed class PersonalLibraryRepository : IPersonalLibraryRepository
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
            INSERT INTO watch_events(movie_id, watched_at, created_at) VALUES ($movie, $watched, $created);
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
                   COUNT(DISTINCT we.id), MAX(we.watched_at)
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

    private static PersonalMovieState ReadState(SqliteConnection connection, int movieId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, """
            SELECT ps.preference, ps.watchlist_added_at, ps.created_at, ps.updated_at,
                   COUNT(we.id), MAX(we.watched_at)
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
            "SELECT id, movie_id, watched_at, created_at FROM watch_events WHERE movie_id = $movie ORDER BY datetime(watched_at), id;");
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
