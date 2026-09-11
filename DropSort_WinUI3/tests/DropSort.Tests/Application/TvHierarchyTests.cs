using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Discovery;
using DropSort.FileSystem.Inspection;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// The TV hierarchy over a real SQLite catalog and real files: the schema, registration and its
/// refusals, idempotent rescans, the availability the UI reads, and the rule that a missing file keeps
/// its registration. Movies and TV share one catalog, so coexistence is asserted here too.
/// </summary>
public sealed class TvHierarchyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly string _workspace;
    private readonly CatalogUnitOfWorkFactory _factory;

    public TvHierarchyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dropsort_tv_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);

        _workspace = Path.Combine(Path.GetTempPath(), $"dropsort_tv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void The_migration_creates_the_hierarchy_with_its_keys_and_indexes()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        foreach (var table in new[] { "tv_shows", "tv_seasons", "tv_episodes", "episode_media_files" })
        {
            Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));
        }

        foreach (var index in new[]
        {
            "idx_tv_shows_sort_title", "idx_tv_seasons_show_id",
            "idx_tv_episodes_season_id", "idx_episode_media_files_episode_id",
        })
        {
            Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{index}';"));
        }

        Assert.Equal((long)DatabaseMigrator.LatestVersion, Scalar(connection, "PRAGMA user_version;"));
    }

    [Fact]
    public void Migrating_an_existing_movie_library_adds_the_tv_tables_and_keeps_the_movies()
    {
        var path = Path.Combine(_workspace, $"legacy_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";

        try
        {
            // Stop at the schema that shipped before this phase, seed it, then migrate the rest of the way.
            new DatabaseMigrator(connectionString).Migrate(5);

            using (var seeded = new SqliteConnection(connectionString))
            {
                seeded.Open();
                using var command = seeded.CreateCommand();
                command.CommandText = """
                    INSERT INTO movies(id, title, date_added, created_at, updated_at)
                    VALUES (4, 'Legacy', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                    INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, discovered_at, last_seen_at)
                    VALUES (6, 4, 'C:\\media\\Legacy.mkv', 'C:\\MEDIA\\LEGACY.MKV', 12, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                    """;
                command.ExecuteNonQuery();
            }

            new DatabaseMigrator(connectionString).Migrate();

            using var upgraded = new SqliteConnection(connectionString);
            upgraded.Open();
            Assert.Equal(1L, Scalar(upgraded, "SELECT COUNT(*) FROM movies WHERE id = 4;"));
            Assert.Equal(1L, Scalar(upgraded, "SELECT COUNT(*) FROM media_files WHERE id = 6;"));
            Assert.Equal(0L, Scalar(upgraded, "SELECT COUNT(*) FROM tv_shows;"));
            Assert.Equal((long)DatabaseMigrator.LatestVersion, Scalar(upgraded, "PRAGMA user_version;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void A_show_season_and_episode_round_trip_with_stable_ids()
    {
        var now = DateTimeOffset.UtcNow;
        int showId, seasonId, episodeId;

        using (var work = _factory.Begin())
        {
            var show = work.TvShows.Create(new TvShowCatalogData(null, null, "Breaking Bad"), now);
            var season = work.TvSeasons.Create(show.Id, 1, null, null, now);
            var episode = work.TvEpisodes.Create(season.Id, 1, "Pilot", null, 58, null, now);
            showId = show.Id;
            seasonId = season.Id;
            episodeId = episode.Id;
            work.Commit();
        }

        using var read = _factory.Begin();
        var storedShow = read.TvShows.GetById(showId);
        var storedSeason = read.TvSeasons.GetByNumber(showId, 1);
        var storedEpisode = read.TvEpisodes.GetByNumber(seasonId, 1);

        Assert.Equal("Breaking Bad", storedShow!.Title);
        Assert.Equal("breakingbad", storedShow.SortTitle);
        Assert.Equal(seasonId, storedSeason!.Id);
        Assert.Equal(showId, storedSeason.ShowId);
        Assert.Equal(episodeId, storedEpisode!.Id);
        Assert.Equal(seasonId, storedEpisode.SeasonId);
        Assert.Equal("Pilot", storedEpisode.Title);
        Assert.Equal(58, storedEpisode.RuntimeMinutes);
    }

    [Fact]
    public void The_hierarchy_enforces_its_uniqueness_and_cascades_a_delete()
    {
        var now = DateTimeOffset.UtcNow;

        using (var work = _factory.Begin())
        {
            var show = work.TvShows.Create(new TvShowCatalogData(null, null, "Severance"), now);
            var season = work.TvSeasons.Create(show.Id, 1, null, null, now);
            work.TvEpisodes.Create(season.Id, 1, null, null, null, null, now);

            Assert.Throws<SqliteException>(() => work.TvShows.Create(new TvShowCatalogData(null, null, "severance"), now));
            Assert.Throws<SqliteException>(() => work.TvSeasons.Create(show.Id, 1, null, null, now));
            Assert.Throws<SqliteException>(() => work.TvEpisodes.Create(season.Id, 1, null, null, null, null, now));
            work.Commit();
        }

        int showId;
        using (var read = _factory.Begin())
        {
            showId = read.TvShows.ListAll().Single().Id;
        }

        using (var delete = _factory.Begin())
        {
            delete.TvShows.Delete(showId);
            delete.Commit();
        }

        using var after = _factory.Begin();
        Assert.Empty(after.TvShows.ListAll());
        Assert.Empty(after.TvSeasons.ListByShow(showId));
        Assert.Empty(after.TvEpisodes.ListByShow(showId));
    }

    [Fact]
    public void Registering_an_episode_builds_the_hierarchy_and_is_idempotent()
    {
        var import = NewImportService();
        var path = WriteMedia("Breaking.Bad.S01E01.1080p.BluRay.x265.mkv");

        var first = import.RegisterEpisodeImport(Command(path));
        var second = import.RegisterEpisodeImport(Command(path));

        Assert.False(first.AlreadyRegistered);
        Assert.True(second.AlreadyRegistered);
        Assert.Equal(first.Show.Id, second.Show.Id);
        Assert.Equal(first.Season.Id, second.Season.Id);
        Assert.Equal(first.Episode.Id, second.Episode.Id);
        Assert.Equal(first.MediaFile.Id, second.MediaFile.Id);

        Assert.Equal("Breaking Bad", first.Show.Title);
        Assert.Equal(1, first.Season.Number);
        Assert.Equal(1, first.Episode.Number);

        using var read = _factory.Begin();
        Assert.Single(read.TvShows.ListAll());
        Assert.Single(read.TvSeasons.ListByShow(first.Show.Id));
        Assert.Single(read.TvEpisodes.ListByShow(first.Show.Id));
        Assert.Single(read.MediaFiles.ListAll());
    }

    [Fact]
    public void A_second_scan_of_the_same_folder_adds_nothing()
    {
        var import = NewImportService();
        var discovery = new MediaDiscoveryService();
        WriteMedia("Severance.S01E01.1080p.mkv");
        WriteMedia("Severance.S01E02.1080p.mkv");
        WriteMedia("Severance.1x03.1080p.mkv");

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var item in discovery.Discover(_workspace, recursive: false)
                .Where(item => item.Classification == DropSort.Domain.Media.Discovery.DiscoveryClassification.TvEpisodeCandidate))
            {
                import.RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
                    item.Path, item.FileSize ?? 0, item.ParsedMedia!, DateTimeOffset.UtcNow));
            }
        }

        using var read = _factory.Begin();
        var show = read.TvShows.ListAll().Single();
        Assert.Equal("Severance", show.Title);
        Assert.Single(read.TvSeasons.ListByShow(show.Id));
        Assert.Equal(3, read.TvEpisodes.ListByShow(show.Id).Count);
        Assert.Equal(3, read.MediaFiles.ListAll().Count);
    }

    [Fact]
    public void An_episode_can_carry_a_second_file_and_a_file_cannot_change_owner()
    {
        var import = NewImportService();
        var first = WriteMedia("Severance.S01E01.1080p.mkv");
        var second = WriteMedia("Severance.S01E01.2160p.mkv");

        var registered = import.RegisterEpisodeImport(Command(first));
        var alternate = import.RegisterEpisodeImport(Command(second));

        Assert.Equal(registered.Episode.Id, alternate.Episode.Id);
        Assert.NotEqual(registered.MediaFile.Id, alternate.MediaFile.Id);

        using (var read = _factory.Begin())
        {
            Assert.Equal(2, read.TvEpisodes.ListMediaFiles(registered.Episode.Id).Count);
        }

        // The same path claimed by a different episode is refused, not re-pointed.
        var parsed = Parse(second, seasonNumber: 2, episodeNumber: 5, title: "Severance");
        var refusal = Assert.Throws<EpisodeRegistrationException>(() =>
            import.RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
                second, 2048, parsed, DateTimeOffset.UtcNow)));

        Assert.Equal(EpisodeRegistrationRefusal.OwnedByAnotherEpisode, refusal.Refusal);
    }

    [Fact]
    public void Ambiguous_or_conflicting_input_is_refused_with_a_reason()
    {
        var import = NewImportService();
        var path = WriteMedia("Some.Unnamed.File.mkv");

        var noMarker = Assert.Throws<EpisodeRegistrationException>(() => import.RegisterEpisodeImport(
            new ConfirmEpisodeImportCommand(path, 2048, Parse(path, null, null, "Some Unnamed File"), DateTimeOffset.UtcNow)));
        Assert.Equal(EpisodeRegistrationRefusal.NoEpisodeMarker, noMarker.Refusal);

        var noTitle = Assert.Throws<EpisodeRegistrationException>(() => import.RegisterEpisodeImport(
            new ConfirmEpisodeImportCommand(path, 2048, Parse(path, 1, 2, null), DateTimeOffset.UtcNow)));
        Assert.Equal(EpisodeRegistrationRefusal.NoShowTitle, noTitle.Refusal);

        // A file already registered as a movie's media file is never moved into the TV hierarchy.
        var moviePath = WriteMedia("Dune.Part.Two.2024.1080p.mkv");
        import.RegisterMovieImport(new ConfirmMovieImportCommand(
            moviePath, 2048, Parse(moviePath, null, null, "Dune Part Two"), DateTimeOffset.UtcNow));

        var asEpisode = Assert.Throws<EpisodeRegistrationException>(() => import.RegisterEpisodeImport(
            new ConfirmEpisodeImportCommand(moviePath, 2048, Parse(moviePath, 1, 1, "Dune Part Two"), DateTimeOffset.UtcNow)));
        Assert.Equal(EpisodeRegistrationRefusal.AlreadyAMovieFile, asEpisode.Refusal);

        using var read = _factory.Begin();
        Assert.Empty(read.TvShows.ListAll());
    }

    [Fact]
    public void A_deleted_episode_file_stays_registered_and_reports_missing()
    {
        var import = NewImportService();
        var present = WriteMedia("Severance.S01E01.1080p.mkv");
        var vanishing = WriteMedia("Severance.S01E02.1080p.mkv");

        var kept = import.RegisterEpisodeImport(Command(present));
        var lost = import.RegisterEpisodeImport(Command(vanishing));
        File.Delete(vanishing);

        var reconciliation = new ReconciliationService(MediaFiles(), new AvailabilityInspector(), Movies());
        var result = reconciliation.CheckLibrary();

        Assert.Equal(2, result.FileProgress.CheckedFiles);
        Assert.Equal(1, result.FileProgress.MissingFiles);

        var tv = NewTvService();
        var details = tv.GetShowDetails(kept.Show.Id);
        var episodes = details.Seasons.Single().Episodes;

        Assert.Equal(2, episodes.Count);
        Assert.Equal(EpisodeAvailability.Present, episodes.Single(e => e.Id == kept.Episode.Id).Availability);
        Assert.Equal(EpisodeAvailability.Missing, episodes.Single(e => e.Id == lost.Episode.Id).Availability);

        // The registration survives: the row, its link and its file record are all still there.
        Assert.Equal(lost.Episode.Id, tv.FindEpisodeForMediaFile(lost.MediaFile.Id));
        Assert.Single(tv.ListEpisodeFiles(lost.Episode.Id));
        Assert.NotNull(MediaFiles().GetById(lost.MediaFile.Id));

        var listed = tv.ListShows().Single();
        Assert.Equal(2, listed.EpisodeCount);
        Assert.Equal(1, listed.EpisodesWithLocalFile);
        Assert.Equal(1, listed.MissingEpisodeCount);
    }

    [Fact]
    public void Movies_and_shows_coexist_in_one_catalog()
    {
        var import = NewImportService();
        var moviePath = WriteMedia("Oppenheimer.2023.2160p.mkv");
        var episodePath = WriteMedia("Better.Call.Saul.S02E07.1080p.mkv");

        var movie = import.RegisterMovieImport(new ConfirmMovieImportCommand(
            moviePath, 2048, Parse(moviePath, null, null, "Oppenheimer"), DateTimeOffset.UtcNow));
        var episode = import.RegisterEpisodeImport(Command(episodePath));

        var library = new LibraryService(Movies(), MediaFiles(), new PersonalLibraryRepository(_connectionString));

        Assert.Single(library.ListMovies());
        Assert.Equal(movie.Movie.Id, library.ListMovies()[0].Id);
        Assert.Single(NewTvService().ListShows());

        // The episode's file is not a movie file, and the movie's file is not an episode file.
        Assert.Null(MediaFiles().GetById(episode.MediaFile.Id)!.MovieId);
        Assert.Null(NewTvService().FindEpisodeForMediaFile(movie.MediaFile.Id));
    }

    [Fact]
    public void Clearing_the_library_empties_the_tv_hierarchy_and_reports_it()
    {
        var import = NewImportService();
        import.RegisterMovieImport(new ConfirmMovieImportCommand(
            WriteMedia("Past.Lives.2023.1080p.mkv"), 2048,
            Parse("Past.Lives.2023.1080p.mkv", null, null, "Past Lives"), DateTimeOffset.UtcNow));
        import.RegisterEpisodeImport(Command(WriteMedia("Severance.S01E01.1080p.mkv")));
        import.RegisterEpisodeImport(Command(WriteMedia("Severance.S01E02.1080p.mkv")));

        var settings = new SettingsService(new LibraryMaintenanceRepository(_connectionString));
        var result = settings.ClearLibraryData();

        Assert.Equal(1, result.MoviesRemoved);
        Assert.Equal(3, result.MediaFilesRemoved);
        Assert.Equal(1, result.ShowsRemoved);
        Assert.Equal(1, result.SeasonsRemoved);
        Assert.Equal(2, result.EpisodesRemoved);

        using var read = _factory.Begin();
        Assert.Empty(read.TvShows.ListAll());
        Assert.Empty(read.MediaFiles.ListAll());
        Assert.Equal(0, read.Movies.CountAll());

        // Clearing the index never deletes media: the files are still on disk.
        Assert.Equal(3, Directory.GetFiles(_workspace, "*.mkv").Length);
    }

    [Fact]
    public void Relink_lifecycle_tv_episode_file_repoints_episode_and_clears_missing()
    {
        var import = NewImportService();
        var path = WriteMedia("Severance.S01E01.1080p.mkv");
        var registered = import.RegisterEpisodeImport(Command(path));

        var inspector = new AvailabilityInspector();
        var reconciliation = new ReconciliationService(MediaFiles(), inspector, Movies());
        var tv = NewTvService();

        // 1. Initial health check: healthy
        var initial = reconciliation.CheckLibrary();
        Assert.Equal(0, initial.FileProgress.MissingFiles);

        // 2. Delete episode file on disk and reconcile: marks missing
        File.Delete(path);
        reconciliation.ReconcileLibraryFiles();

        var missing = MediaFiles().ListMissing();
        Assert.Single(missing);
        Assert.Equal(registered.MediaFile.Id, missing[0].Id);
        Assert.Equal(MediaFileStatus.Missing, missing[0].Status);

        var episodeId = tv.FindEpisodeForMediaFile(registered.MediaFile.Id);
        Assert.NotNull(episodeId);

        // 3. Prepare replacement file (same size 2048 bytes)
        var replacement = WriteMedia("Severance.S01E01.Repaired.1080p.mkv");

        // 4. Prepare relink (validates existence, size, collisions without moving/deleting file)
        var preview = reconciliation.PrepareMediaRelink(registered.MediaFile.Id, replacement);
        Assert.NotNull(preview);
        Assert.Equal(replacement, preview.CandidatePath);
        Assert.True(File.Exists(replacement), "Candidate file must not be moved or deleted during prepare.");

        // 5. Confirm relink
        var result = reconciliation.ConfirmMediaRelink(preview.PreviewId);
        Assert.NotNull(result);
        Assert.Equal(replacement, result.MediaFile.CurrentPath);
        Assert.Equal(MediaFileStatus.Present, result.MediaFile.Status);
        Assert.True(File.Exists(replacement), "Candidate file must not be moved or deleted during confirm.");

        // 6. Verify TV episode points to the new path
        var episodeFiles = tv.ListEpisodeFiles(episodeId.Value);
        Assert.Single(episodeFiles);
        Assert.Equal(replacement, episodeFiles[0].CurrentPath);
        Assert.Equal(MediaFileStatus.Present, episodeFiles[0].Status);

        // 7. Restart simulation: fresh service instances reading from database
        var freshReconciliation = new ReconciliationService(MediaFiles(), inspector, Movies());
        var restartHealth = freshReconciliation.CheckLibrary();
        Assert.Equal(0, restartHealth.FileProgress.MissingFiles);
        Assert.Equal(1, restartHealth.FileProgress.CheckedFiles);
        Assert.Empty(MediaFiles().ListMissing());
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private ImportService NewImportService() =>
        new(_factory, new NoProvider(), new MediaDiscoveryService());

    private TvLibraryService NewTvService() => new(
        new PerCallTvShowRepository(_factory),
        new PerCallTvSeasonRepository(_factory),
        new PerCallTvEpisodeRepository(_factory));

    private IMediaFileRepository MediaFiles() => new PerCallMediaFileRepository(_factory);

    private IMovieRepository Movies() => new PerCallMovieRepository(_factory);

    private string WriteMedia(string fileName)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllBytes(path, new byte[2048]);
        return path;
    }

    /// <summary>Registers a file the way the real scan does: parsed by the production parser.</summary>
    private ConfirmEpisodeImportCommand Command(string path)
    {
        var discovered = new MediaDiscoveryService()
            .Discover(Path.GetDirectoryName(path)!, recursive: false)
            .Single(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

        return new ConfirmEpisodeImportCommand(
            path,
            discovered.FileSize ?? 0,
            discovered.ParsedMedia!,
            DateTimeOffset.UtcNow);
    }

    private static ParsedMedia Parse(string path, int? seasonNumber, int? episodeNumber, string? title) => new(
        Path.GetFileName(path),
        seasonNumber is null && episodeNumber is null ? MediaType.Movie : MediaType.TvEpisode,
        title,
        null,
        "1080p",
        null,
        "x265",
        Path.GetExtension(path),
        seasonNumber,
        episodeNumber);

    private sealed class NoProvider : IMetadataProvider
    {
        public string ProviderName => "none";

        public IReadOnlyList<DropSort.Domain.Metadata.Contracts.MovieCandidate> Search(
            DropSort.Domain.Metadata.Contracts.MovieSearchQuery query) => [];

        public DropSort.Domain.Metadata.Contracts.MovieMetadata? GetMovie(string externalId) => null;
    }
}
