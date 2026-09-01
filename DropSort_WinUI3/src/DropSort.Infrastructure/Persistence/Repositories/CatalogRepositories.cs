using System.Collections.Immutable;
using System.Text.Json;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

public sealed class CatalogUnitOfWork : ICatalogUnitOfWork
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private bool _completed;
    private bool _disposed;

    public IMovieRepository Movies { get; }
    public IMediaFileRepository MediaFiles { get; }

    public CatalogUnitOfWork(string connectionString)
    {
        _connection = SqliteConnections.Open(connectionString);
        _transaction = _connection.BeginTransaction();
        Movies = new MovieRepository(_connection, _transaction);
        MediaFiles = new MediaFileRepository(_connection, _transaction);
    }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("Unit of work is already complete.");
        _transaction.Commit();
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!_completed) _transaction.Rollback();
        _transaction.Dispose();
        _connection.Dispose();
        _disposed = true;
    }
}

public sealed class CatalogUnitOfWorkFactory : ICatalogUnitOfWorkFactory
{
    private readonly string _connectionString;
    public CatalogUnitOfWorkFactory(string connectionString) => _connectionString = connectionString;
    public ICatalogUnitOfWork Begin() => new CatalogUnitOfWork(_connectionString);
}

public sealed class MovieRepository : IMovieRepository
{
    private const string Columns = "id, provider, external_id, title, original_title, year, overview, runtime_minutes, rating, poster_path, date_added, created_at, updated_at, genres, metadata_status";
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction? _transaction;

    public MovieRepository(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public Movie Create(MovieCatalogData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var command = Command("""
            INSERT INTO movies (
                provider, external_id, title, original_title, year, overview,
                runtime_minutes, rating, poster_path, date_added, created_at, updated_at,
                genres, metadata_status
            ) VALUES (
                $provider, $external, $title, $original, $year, $overview,
                $runtime, $rating, $poster, $dateAdded, $created, $updated,
                $genres, $status
            );
            SELECT last_insert_rowid();
            """);
        AddMovieParameters(command, data, now);
        var id = checked((int)(long)(command.ExecuteScalar() ?? throw new InvalidOperationException("Movie insert returned no ID.")));
        return GetById(id) ?? throw new InvalidOperationException("Inserted movie could not be read back.");
    }

    public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        using var command = Command("""
            UPDATE movies
               SET provider = $provider, external_id = $external, title = $title,
                   original_title = $original, year = $year, overview = $overview,
                   runtime_minutes = $runtime, rating = $rating, poster_path = $poster,
                   genres = $genres, metadata_status = $status, updated_at = $updated
             WHERE id = $id;
            """);
        AddMovieParameters(command, data, now);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"Movie {id} was not found.");
        return GetById(id)!;
    }

    public Movie? GetById(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        using var command = Command($"SELECT {Columns} FROM movies WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command);
    }

    public Movie? GetByExternalId(string provider, string externalId)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("External ID is required.", nameof(externalId));
        using var command = Command($"SELECT {Columns} FROM movies WHERE provider = $provider AND external_id = $external;");
        command.Parameters.AddWithValue("$provider", provider.Trim());
        command.Parameters.AddWithValue("$external", externalId.Trim());
        return ReadOne(command);
    }

    public IReadOnlyList<Movie> ListAll()
    {
        using var command = Command($"SELECT {Columns} FROM movies ORDER BY datetime(date_added) DESC, id DESC;");
        return ReadMany(command);
    }

    public IReadOnlyList<Movie> ListPage(int afterId, int limit)
    {
        if (afterId < 0) throw new ArgumentOutOfRangeException(nameof(afterId));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        using var command = Command($"SELECT {Columns} FROM movies WHERE id > $after ORDER BY id LIMIT $limit;");
        command.Parameters.AddWithValue("$after", afterId);
        command.Parameters.AddWithValue("$limit", limit);
        return ReadMany(command);
    }

    public int CountAll()
    {
        using var command = Command("SELECT COUNT(*) FROM movies;");
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Delete(int id)
    {
        using var command = Command("DELETE FROM movies WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"Movie {id} was not found.");
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private static void AddMovieParameters(SqliteCommand command, MovieCatalogData data, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$provider", (object?)data.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$external", (object?)data.ExternalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", data.Title);
        command.Parameters.AddWithValue("$original", (object?)data.OriginalTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$year", (object?)data.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$overview", (object?)data.Overview ?? DBNull.Value);
        command.Parameters.AddWithValue("$runtime", (object?)data.RuntimeMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("$rating", (object?)data.Rating ?? DBNull.Value);
        command.Parameters.AddWithValue("$poster", (object?)data.PosterReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$dateAdded", now.ToString("O"));
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.Parameters.AddWithValue("$genres", JsonSerializer.Serialize(data.Genres));
        command.Parameters.AddWithValue("$status", EnumText.MetadataStatus(data.MetadataStatus));
    }

    private static Movie? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<Movie> ReadMany(SqliteCommand command)
    {
        var result = new List<Movie>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    internal static Movie Map(SqliteDataReader reader)
    {
        var genres = JsonSerializer.Deserialize<string[]>(reader.GetString(13)) ?? [];
        var data = new MovieCatalogData(
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            genres.ToImmutableArray(),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            EnumText.ParseMetadataStatus(reader.GetString(14)));
        return new Movie(
            reader.GetInt32(0),
            data,
            ParseTimestamp(reader.GetString(10)),
            ParseTimestamp(reader.GetString(11)),
            ParseTimestamp(reader.GetString(12)));
    }

    internal static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}

public sealed class MediaFileRepository : IMediaFileRepository
{
    private const string Columns = "id, movie_id, current_path, file_size, extension, resolution, codec, source, status, discovered_at, last_seen_at";
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction? _transaction;

    public MediaFileRepository(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null)
    {
        using var command = Command("""
            INSERT INTO media_files (
                movie_id, current_path, path_key, file_size, extension,
                resolution, codec, source, status, discovered_at, last_seen_at
            ) VALUES (
                $movie, $path, $key, $size, $extension,
                $resolution, $codec, $source, 'PRESENT', $observed, $observed
            );
            SELECT last_insert_rowid();
            """);
        command.Parameters.AddWithValue("$movie", (object?)movieId ?? DBNull.Value);
        AddFacts(command, facts);
        var id = checked((int)(long)(command.ExecuteScalar() ?? throw new InvalidOperationException("Media insert returned no ID.")));
        return GetById(id)!;
    }

    public MediaFile? GetById(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        using var command = Command($"SELECT {Columns} FROM media_files WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        return ReadOne(command);
    }

    public MediaFile? GetByPath(string path)
    {
        using var command = Command($"SELECT {Columns} FROM media_files WHERE path_key = $key;");
        command.Parameters.AddWithValue("$key", PathKey(path));
        return ReadOne(command);
    }

    public IReadOnlyList<MediaFile> GetByMovieId(int movieId)
    {
        using var command = Command($"SELECT {Columns} FROM media_files WHERE movie_id = $movie ORDER BY id;");
        command.Parameters.AddWithValue("$movie", movieId);
        return ReadMany(command);
    }

    public IReadOnlyList<MediaFile> ListAll()
    {
        using var command = Command($"SELECT {Columns} FROM media_files ORDER BY id;");
        return ReadMany(command);
    }

    public IReadOnlyList<MediaFile> ListMissing()
    {
        using var command = Command($"SELECT {Columns} FROM media_files WHERE status = 'MISSING' ORDER BY id;");
        return ReadMany(command);
    }

    public MediaFile LinkToMovie(int id, int movieId)
    {
        using var command = Command("UPDATE media_files SET movie_id = $movie WHERE id = $id AND (movie_id IS NULL OR movie_id = $movie);");
        command.Parameters.AddWithValue("$movie", movieId);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Media file is missing or linked to another movie.");
        return GetById(id)!;
    }

    public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts)
    {
        var current = GetById(id) ?? throw new KeyNotFoundException($"Media file {id} was not found.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(PathKey(current.CurrentPath), PathKey(facts.CurrentPath)))
            throw new InvalidOperationException("Verified facts identify a different path.");
        using var command = Command("""
            UPDATE media_files
               SET file_size = $size, extension = $extension, resolution = $resolution,
                   codec = $codec, source = $source, status = 'PRESENT', last_seen_at = $observed
             WHERE id = $id AND path_key = $key;
            """);
        AddFacts(command, facts);
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Media file path changed during refresh.");
        return GetById(id)!;
    }

    public MediaFile MarkMissing(int id, DateTimeOffset observedAt)
    {
        using var command = Command("UPDATE media_files SET status = 'MISSING' WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"Media file {id} was not found.");
        return GetById(id)!;
    }

    public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts)
    {
        using var command = Command("""
            UPDATE media_files
               SET current_path = $path, path_key = $key, file_size = $size,
                   extension = $extension, resolution = $resolution, codec = $codec,
                   source = $source, status = 'PRESENT', last_seen_at = $observed
             WHERE id = $id AND path_key = $expected AND status = 'MISSING';
            """);
        AddFacts(command, facts);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$expected", PathKey(expectedPath));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Media path or status changed before relink.");
        return GetById(id)!;
    }

    public void Delete(int id)
    {
        using var command = Command("DELETE FROM media_files WHERE id = $id;");
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException($"Media file {id} was not found.");
    }

    internal static string PathKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Path must be absolute.", nameof(path));
        return Path.GetFullPath(path).ToUpperInvariant();
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        return command;
    }

    private static void AddFacts(SqliteCommand command, VerifiedMediaFileFacts facts)
    {
        command.Parameters.AddWithValue("$path", facts.CurrentPath);
        command.Parameters.AddWithValue("$key", PathKey(facts.CurrentPath));
        command.Parameters.AddWithValue("$size", facts.FileSize);
        command.Parameters.AddWithValue("$extension", facts.Extension);
        command.Parameters.AddWithValue("$resolution", (object?)facts.Resolution ?? DBNull.Value);
        command.Parameters.AddWithValue("$codec", (object?)facts.Codec ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)facts.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$observed", facts.ObservedAt.ToString("O"));
    }

    private static MediaFile? ReadOne(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<MediaFile> ReadMany(SqliteCommand command)
    {
        var result = new List<MediaFile>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    internal static MediaFile Map(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.IsDBNull(1) ? null : reader.GetInt32(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8) == "PRESENT" ? MediaFileStatus.Present : MediaFileStatus.Missing,
        MovieRepository.ParseTimestamp(reader.GetString(9)),
        MovieRepository.ParseTimestamp(reader.GetString(10)));
}

internal static class EnumText
{
    internal static string MetadataStatus(MetadataStatus value) => value switch
    {
        DropSort.Domain.Library.Movies.MetadataStatus.Pending => "PENDING",
        DropSort.Domain.Library.Movies.MetadataStatus.Ready => "READY",
        DropSort.Domain.Library.Movies.MetadataStatus.Failed => "FAILED",
        DropSort.Domain.Library.Movies.MetadataStatus.NeedsMatch => "NEEDS_MATCH",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    internal static MetadataStatus ParseMetadataStatus(string value) => value switch
    {
        "PENDING" => DropSort.Domain.Library.Movies.MetadataStatus.Pending,
        "READY" => DropSort.Domain.Library.Movies.MetadataStatus.Ready,
        "FAILED" => DropSort.Domain.Library.Movies.MetadataStatus.Failed,
        "NEEDS_MATCH" => DropSort.Domain.Library.Movies.MetadataStatus.NeedsMatch,
        _ => throw new InvalidDataException($"Unknown metadata status '{value}'.")
    };
}
