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
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using (var active = Command(connection, transaction, "DELETE FROM active_viewings WHERE movie_id = $movie;"))
        {
            active.Parameters.AddWithValue("$movie", movieId);
            active.ExecuteNonQuery();
        }
        var events = ReadEvents(connection, movieId, transaction);
        transaction.Commit();
        return events.Single(item => item.Id == id);
    }

    public WatchEvent DeleteWatchEvent(int eventId)
    {
        ValidateId(eventId, nameof(eventId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var find = Command(connection, transaction, "SELECT movie_id FROM watch_events WHERE id = $id;");
        find.Parameters.AddWithValue("$id", eventId);
        var movieValue = find.ExecuteScalar();
        // No row => the event does not exist. A row whose movie_id is NULL is an episode or orphaned
        // event; DeleteWatchEvent is the movie-scoped contract (it returns a movie WatchEvent), so such
        // an event is "not a movie watch event" rather than a Convert.ToInt32(DBNull) crash.
        if (movieValue is null or DBNull)
            throw new KeyNotFoundException($"Movie watch event {eventId} was not found.");
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
                   m.genres, m.metadata_status, m.backdrop_path, m.tagline, m.review_match, m.last_opened_at,
                   m.custom_display_title,
                   COUNT(DISTINCT mf.id),
                   COUNT(DISTINCT CASE WHEN mf.status = 'MISSING' THEN mf.id END),
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
                 reader.GetInt32(20),
                 reader.GetInt32(21),
                 ParsePreference(reader.GetString(22)),
                 reader.GetInt32(23) != 0,
                 reader.GetInt32(24),
                 reader.IsDBNull(25) ? null : MovieRepository.ParseTimestamp(reader.GetString(25))));
        }
        return result;
    }

    public IReadOnlyList<CustomListRecord> ListCustomLists()
    {
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null, """
            SELECT cl.id, cl.display_name, COUNT(m.id)
              FROM custom_lists cl
              LEFT JOIN custom_list_members m ON m.list_id = cl.id
             GROUP BY cl.id, cl.display_name, cl.created_at
             ORDER BY cl.created_at, cl.id;
            """);
        var result = new List<CustomListRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new CustomListRecord(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        return result;
    }

    public int CreateCustomList(string name, DateTimeOffset now)
    {
        var trimmed = ValidateListName(name);
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireUniqueName(connection, transaction, trimmed, null);
        using var command = Command(connection, transaction, """
            INSERT INTO custom_lists (display_name, created_at) VALUES ($name, $now);
            SELECT last_insert_rowid();
            """);
        command.Parameters.AddWithValue("$name", trimmed);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        var id = checked((int)(long)(command.ExecuteScalar() ?? throw new InvalidOperationException("List insert returned no ID.")));
        transaction.Commit();
        return id;
    }

    public void RenameCustomList(int listId, string name, DateTimeOffset now)
    {
        ValidateId(listId, nameof(listId));
        var trimmed = ValidateListName(name);
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireCustomList(connection, listId, transaction);
        RequireUniqueName(connection, transaction, trimmed, listId);
        using var command = Command(connection, transaction,
            "UPDATE custom_lists SET display_name = $name WHERE id = $id;");
        command.Parameters.AddWithValue("$name", trimmed);
        command.Parameters.AddWithValue("$id", listId);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteCustomList(int listId)
    {
        ValidateId(listId, nameof(listId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        // Membership rows cascade with the list (custom_list_members.list_id ... ON DELETE CASCADE).
        using var command = Command(connection, null, "DELETE FROM custom_lists WHERE id = $id;");
        command.Parameters.AddWithValue("$id", listId);
        command.ExecuteNonQuery();
    }

    public void AddMovieToCustomList(int listId, int movieId, DateTimeOffset now)
    {
        ValidateId(listId, nameof(listId));
        ValidateId(movieId, nameof(movieId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireMovie(connection, movieId, transaction);
        RequireCustomList(connection, listId, transaction);
        // OR IGNORE makes the add idempotent against the per-list partial unique index on movie_id.
        using var command = Command(connection, transaction, """
            INSERT OR IGNORE INTO custom_list_members (list_id, media_kind, movie_id, added_at)
            VALUES ($list, 'MOVIE', $movie, $now);
            """);
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void RemoveMovieFromCustomList(int listId, int movieId)
    {
        ValidateId(listId, nameof(listId));
        ValidateId(movieId, nameof(movieId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null,
            "DELETE FROM custom_list_members WHERE list_id = $list AND movie_id = $movie;");
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$movie", movieId);
        command.ExecuteNonQuery();
    }

    public IReadOnlySet<int> GetCustomListIdsForMovie(int movieId)
    {
        ValidateId(movieId, nameof(movieId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null,
            "SELECT list_id FROM custom_list_members WHERE movie_id = $movie;");
        command.Parameters.AddWithValue("$movie", movieId);
        var ids = new HashSet<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    public void AddShowToCustomList(int listId, int showId, DateTimeOffset now)
    {
        ValidateId(listId, nameof(listId));
        ValidateId(showId, nameof(showId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        RequireShow(connection, showId, transaction);
        RequireCustomList(connection, listId, transaction);
        using var command = Command(connection, transaction, """
            INSERT OR IGNORE INTO custom_list_members (list_id, media_kind, show_id, added_at)
            VALUES ($list, 'SHOW', $show, $now);
            """);
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$show", showId);
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void RemoveShowFromCustomList(int listId, int showId)
    {
        ValidateId(listId, nameof(listId));
        ValidateId(showId, nameof(showId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null,
            "DELETE FROM custom_list_members WHERE list_id = $list AND show_id = $show;");
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$show", showId);
        command.ExecuteNonQuery();
    }

    public IReadOnlySet<int> GetCustomListIdsForShow(int showId)
    {
        ValidateId(showId, nameof(showId));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = Command(connection, null,
            "SELECT list_id FROM custom_list_members WHERE show_id = $show;");
        command.Parameters.AddWithValue("$show", showId);
        var ids = new HashSet<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    public IReadOnlyList<CustomListMember> ListCustomListItems(int listId, int limit = 1000, int offset = 0)
    {
        ValidateId(listId, nameof(listId));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var connection = SqliteConnections.Open(_connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT kind, id, title, year, poster, is_missing, custom_title, canonical_title FROM (
                SELECT 'MOVIE' AS kind, m.id AS id, COALESCE(NULLIF(m.custom_display_title, ''), m.title) AS title, m.year AS year, m.poster_path AS poster,
                       CASE WHEN EXISTS (SELECT 1 FROM media_files mf WHERE mf.movie_id = m.id AND mf.status = 'PRESENT')
                            THEN 0 ELSE 1 END AS is_missing,
                       m.custom_display_title AS custom_title,
                       m.title AS canonical_title,
                       clm.added_at AS added_at
                  FROM custom_list_members clm
                  JOIN movies m ON m.id = clm.movie_id
                 WHERE clm.list_id = $list AND clm.media_kind = 'MOVIE'
                UNION ALL
                SELECT 'SHOW', s.id, COALESCE(NULLIF(s.custom_display_title, ''), s.title) AS title, s.year, s.poster_path,
                       CASE WHEN EXISTS (
                           SELECT 1 FROM episode_media_files emf
                             JOIN media_files mf ON mf.id = emf.media_file_id
                             JOIN tv_episodes e ON e.id = emf.episode_id
                             JOIN tv_seasons se ON se.id = e.season_id
                            WHERE se.show_id = s.id AND mf.status = 'PRESENT')
                            THEN 0 ELSE 1 END,
                       s.custom_display_title AS custom_title,
                       s.title AS canonical_title,
                       clm.added_at
                  FROM custom_list_members clm
                  JOIN tv_shows s ON s.id = clm.show_id
                 WHERE clm.list_id = $list AND clm.media_kind = 'SHOW'
            )
             ORDER BY datetime(added_at) DESC, id DESC
             LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var result = new List<CustomListMember>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CustomListMember(
                reader.GetString(0) == "SHOW" ? CustomListMediaKind.Show : CustomListMediaKind.Movie,
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) != 0,
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return result;
    }

    private static string ValidateListName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A custom list name is required.", nameof(name));
        return name.Trim();
    }

    /// <summary>
    /// Rejects a display name that another custom list already uses. Identity stays the stable id; this
    /// only keeps display names unambiguous. <paramref name="exceptListId" /> is the list being renamed
    /// (excluded) or null when creating.
    /// <para>
    /// The comparison is done in .NET with <see cref="StringComparison.OrdinalIgnoreCase" /> after trimming,
    /// not in SQL: SQLite's built-in lower()/upper() fold ASCII only, so two accented Latin names differing
    /// only in case (for example "Épica" and "épica") would slip past a SQL comparison. OrdinalIgnoreCase is
    /// culture-independent (no Turkish-I surprises) and folds the Unicode BMP case pairs this needs.
    /// </para>
    /// </summary>
    private static void RequireUniqueName(SqliteConnection connection, SqliteTransaction? transaction, string trimmedName, int? exceptListId)
    {
        using var command = Command(connection, transaction, "SELECT id, display_name FROM custom_lists;");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (exceptListId is int except && reader.GetInt32(0) == except) continue;
            if (string.Equals(reader.GetString(1).Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
                throw new DropSort.Application.DuplicateCustomListNameException(trimmedName);
        }
    }

    private static void RequireShow(SqliteConnection connection, int showId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM tv_shows WHERE id = $show;");
        command.Parameters.AddWithValue("$show", showId);
        if (command.ExecuteScalar() is null) throw new KeyNotFoundException($"TV show {showId} was not found.");
    }

    private static void RequireCustomList(SqliteConnection connection, int listId, SqliteTransaction? transaction)
    {
        using var command = Command(connection, transaction, "SELECT 1 FROM custom_lists WHERE id = $id;");
        command.Parameters.AddWithValue("$id", listId);
        if (command.ExecuteScalar() is null) throw new KeyNotFoundException($"Custom list {listId} was not found.");
    }

    public WatchHistoryEntry AddEpisodeWatchEvent(int episodeId, DateTimeOffset watchedAt, DateTimeOffset createdAt)
    {
        ValidateId(episodeId, nameof(episodeId));
        using var gate = DatabaseWriteGate.Enter(_connectionString);
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
        using (var active = Command(connection, transaction, "DELETE FROM active_viewings WHERE episode_id = $episode;"))
        {
            active.Parameters.AddWithValue("$episode", episodeId);
            active.ExecuteNonQuery();
        }
        transaction.Commit();
        return entry;
    }

    public int ClearWatchHistory()
    {
        using var gate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = SqliteConnections.Open(_connectionString);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var active = Command(connection, transaction, "DELETE FROM active_viewings;");
        active.ExecuteNonQuery();
        using var command = Command(connection, transaction, "DELETE FROM watch_events;");
        var removed = command.ExecuteNonQuery();
        transaction.Commit();
        return removed;
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

    public IReadOnlyList<WatchHistoryEntry> ListRecentlyWatchedMedia(int limit = 50, int offset = 0)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var connection = SqliteConnections.Open(_connectionString);
        // Collapse the raw event stream to one row per logical medium for the Home "Recently watched"
        // read: a movie by movie_id, a TV *show* by its parent show_id (an episode's identity resolves
        // up through its season to the series - watching any episode of a show shares one card), keeping
        // the newest event of each group and ordering by that event. Episodes orphaned by a deleted show
        // (episode_id / show_id nulled) have no shared identity, so each falls into its own group keyed by
        // event id and stays distinct. Grouping happens before the limit, so N asks for N distinct media.
        using var command = Command(connection, null, """
            SELECT id FROM (
                SELECT w.id, w.watched_at,
                       ROW_NUMBER() OVER (
                           PARTITION BY
                               CASE
                                   WHEN w.media_kind = 'MOVIE' AND w.movie_id IS NOT NULL THEN 'M:' || w.movie_id
                                   WHEN w.media_kind = 'EPISODE' AND se.show_id IS NOT NULL THEN 'S:' || se.show_id
                                   ELSE 'X:' || w.id
                               END
                           ORDER BY julianday(w.watched_at) DESC, w.id DESC
                       ) AS group_rank
                  FROM watch_events w
                  LEFT JOIN tv_episodes e ON e.id = w.episode_id
                  LEFT JOIN tv_seasons se ON se.id = e.season_id
            ) ranked
             WHERE group_rank = 1
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
                   COALESCE(NULLIF(m.custom_display_title, ''), m.title, w.title_snapshot), COALESCE(m.year, w.year_snapshot),
                   se.show_id,
                   COALESCE(NULLIF(s.custom_display_title, ''), s.title, w.show_title_snapshot), COALESCE(e.episode_number, w.episode_number_snapshot),
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
