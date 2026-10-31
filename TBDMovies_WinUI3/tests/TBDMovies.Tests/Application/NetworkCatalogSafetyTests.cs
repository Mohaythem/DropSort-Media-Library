using TBDMovies.Application.External;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Availability;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class NetworkCatalogSafetyTests : IDisposable
{
    private const string UncPath = @"\\server\share\movie.mkv";
    private const string LocalPath = @"C:\healthy\movie.mkv";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_unc_{Guid.NewGuid():N}.db");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public NetworkCatalogSafetyTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        SeedCatalog();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public void Unavailable_network_root_preserves_catalog_relationships_and_healthy_root_progresses()
    {
        var before = SnapshotStableCatalog();
        var offlineMediaBefore = SnapshotMedia(1);
        var inspector = new FakeInspector { UnavailableRoots = [@"\\server\share\"] };
        new ReconciliationService(Media(), inspector).ReconcileLibraryFiles();
        Assert.Equal(before, SnapshotStableCatalog());
        Assert.Equal(offlineMediaBefore, SnapshotMedia(1));
        Assert.Equal(new[] { LocalPath }, inspector.InspectionOrder);
    }

    [Fact]
    public void Recovery_from_network_outage_refreshes_facts_without_changing_ids_or_links()
    {
        var inspector = new FakeInspector { UnavailableRoots = [@"\\server\share\"] };
        new ReconciliationService(Media(), inspector).ReconcileLibraryFiles();
        Assert.Equal(MediaFileStatus.Present, Media().GetById(1)!.Status);
        inspector.UnavailableRoots.Clear();
        inspector.PresentPaths.Add(UncPath);
        new ReconciliationService(Media(), inspector).ReconcileLibraryFiles();
        var recovered = Media().GetById(1)!;
        Assert.Equal(1, recovered.Id);
        Assert.Equal(1, recovered.MovieId);
        Assert.Equal(MediaFileStatus.Present, recovered.Status);
        Assert.Equal(UncPath, recovered.CurrentPath);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM episode_media_files WHERE media_file_id = 1 AND episode_id = 1"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM custom_list_members WHERE media_kind = 'MOVIE' AND movie_id = 1"));
    }

    [Fact]
    public void Cancellation_after_inspection_prevents_missing_write()
    {
        var before = SnapshotMedia(1);
        var cancelled = false;
        var inspector = new FakeInspector { MissingPaths = [UncPath] };
        inspector.AfterInspect = path => { if (path.Equals(UncPath, StringComparison.OrdinalIgnoreCase)) cancelled = true; };
        Assert.Throws<OperationCanceledException>(() => new ReconciliationService(Media(), inspector)
            .ReconcileLibraryFiles(isCancelled: () => cancelled));
        Assert.Equal(before, SnapshotMedia(1));
    }

    [Fact]
    public void Relink_cancellation_after_candidate_inspection_does_not_write_path()
    {
        Execute("UPDATE media_files SET status = 'MISSING' WHERE id = 1");
        var candidate = @"C:\healthy\replacement.mkv";
        var inspector = new FakeInspector { PresentPaths = [candidate] };
        var service = new ReconciliationService(Media(), inspector);
        var preview = service.PrepareMediaRelink(1, candidate);
        var before = SnapshotMedia(1);
        var cancelled = false;
        inspector.AfterInspect = _ => cancelled = true;

        Assert.Throws<OperationCanceledException>(() => service.ConfirmMediaRelink(preview.PreviewId, () => cancelled));
        Assert.Equal(before, SnapshotMedia(1));
    }

    private IMediaFileRepository Media() => new PerCallMediaFileRepository(_factory);

    private void SeedCatalog()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status, review_match) VALUES (1, 'tmdb', '1', 'Seed Movie', '2026-01-01', '2026-01-01', '2026-01-01', '["Drama"]', 'READY', 0);
            INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at) VALUES (1, 1, $unc, $uncKey, 100, 'PRESENT', '2026-01-01', '2026-01-01');
            INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at) VALUES (2, 1, $local, $localKey, 100, 'PRESENT', '2026-01-01', '2026-01-01');
            INSERT INTO movie_personal_state(movie_id, preference, watchlist_added_at, created_at, updated_at) VALUES (1, 'LIKED', '2026-01-01', '2026-01-01', '2026-01-01');
            INSERT INTO watch_events(id, media_kind, movie_id, watched_at, created_at, title_snapshot) VALUES (1, 'MOVIE', 1, '2026-01-01', '2026-01-01', 'Seed Movie');
            INSERT INTO tv_shows(id, title, sort_title, date_added, created_at, updated_at) VALUES (1, 'Seed Show', 'seed show', '2026-01-01', '2026-01-01', '2026-01-01');
            INSERT INTO tv_seasons(id, show_id, season_number, created_at, updated_at) VALUES (1, 1, 1, '2026-01-01', '2026-01-01');
            INSERT INTO tv_episodes(id, season_id, episode_number, created_at, updated_at) VALUES (1, 1, 1, '2026-01-01', '2026-01-01');
            INSERT INTO episode_media_files(media_file_id, episode_id, linked_at) VALUES (1, 1, '2026-01-01');
            INSERT INTO custom_lists(id, display_name, created_at) VALUES (1, 'Favorites', '2026-01-01');
            INSERT INTO custom_list_members(id, list_id, media_kind, movie_id, added_at) VALUES (1, 1, 'MOVIE', 1, '2026-01-01');
            """;
        command.Parameters.AddWithValue("$unc", UncPath);
        command.Parameters.AddWithValue("$uncKey", UncPath.ToUpperInvariant());
        command.Parameters.AddWithValue("$local", LocalPath);
        command.Parameters.AddWithValue("$localKey", LocalPath.ToUpperInvariant());
        command.ExecuteNonQuery();
    }

    private Dictionary<string, string> SnapshotStableCatalog() => new[] { "movies", "movie_personal_state", "watch_events", "tv_shows", "tv_seasons", "tv_episodes", "episode_media_files", "custom_lists", "custom_list_members" }.ToDictionary(table => table, table => SnapshotTable(table));
    private string SnapshotMedia(int id) => SnapshotTable("media_files", $"WHERE id = {id}");
    private string SnapshotTable(string table, string suffix = "", string columns = "*")
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = $"SELECT {columns} FROM {table} {suffix} ORDER BY 1";
        using var reader = command.ExecuteReader(); var rows = new List<string>();
        while (reader.Read()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(string.Join("|", values.Select(value => value is DBNull ? "<NULL>" : value.ToString()))); }
        return string.Join("\n", rows);
    }
    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private sealed class FakeInspector : IAvailabilityInspector
    {
        public HashSet<string> UnavailableRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PresentPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissingPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> InspectionOrder { get; } = [];
        public Action<string>? AfterInspect { get; set; }
        public bool IsStorageAvailable(string path) => !UnavailableRoots.Any(root =>
            string.Equals(root.TrimEnd('\\'), Root(path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        public AvailabilityInspection Inspect(string path)
        {
            if (IsStorageAvailable(path)) InspectionOrder.Add(path);
            var unavailable = !IsStorageAvailable(path);
            var result = MissingPaths.Contains(path)
                ? new AvailabilityInspection(path, AvailabilityInspectionStatus.Missing)
                : PresentPaths.Contains(path) || !unavailable
                ? new AvailabilityInspection(path, AvailabilityInspectionStatus.Present, new MediaFileIdentity(100, 1, 1, 0, 0))
                : new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable);
            AfterInspect?.Invoke(path); return result;
        }
        private static string Root(string path) => Path.GetPathRoot(Path.GetFullPath(path)) ?? path;
    }
}
