using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Media.Matcher;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.FileSystem.Inspection;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class SmartTvImportSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tbdmovies-smarttv-" + Guid.NewGuid().ToString("N"));
    private readonly string _connection;
    private readonly CatalogUnitOfWorkFactory _factory;
    public SmartTvImportSafetyTests()
    {
        Directory.CreateDirectory(_root);
        _connection = "Data Source=" + Path.Combine(_root, "library.db");
        new DatabaseMigrator(_connection).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connection);
    }
    private DiscoveredMedia Media(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, "media", relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[256]);
        return new MediaDiscoveryService().Discover(Path.GetDirectoryName(path)!, false).Single(f => f.Path == path);
    }
    private ImportService Service(IMetadataProvider? provider = null) => new(_factory, provider ?? new Offline(),
        matching: new MetadataMatchingService(_factory, provider ?? new Offline()), fileValidator: new ImportFileValidator());
    private long Count(string table)
    {
        using var c = new SqliteConnection(_connection); c.Open();
        using var command = c.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long)command.ExecuteScalar()!;
    }
    private void Sql(string sql)
    {
        using var c = new SqliteConnection(_connection); c.Open();
        using var command = c.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    [Fact]
    public async Task Folder_analysis_is_read_only_then_import_and_reimport_preserve_one_two_three_hierarchy()
    {
        Media("Breaking Bad/Season 01/S01E01.mkv");
        Media("Breaking Bad/Season 01/S01E02.mkv");
        Media("Breaking Bad/Season 02/S02E01.mkv");
        var root = Path.Combine(_root, "media", "Breaking Bad");
        var analysis = new TvBatchAnalysisService().Analyze(root, new MediaDiscoveryService().Discover(root, true));
        Assert.Equal(0, Count("tv_shows"));
        var selections = analysis.AllFiles.Select(f => new ImportSelection(f.CurrentItem, LocalOnly: true)).ToArray();
        var imported = await Service().ImportSelectedAsync(selections);
        Assert.Equal(3, imported.AddedCount);
        Assert.Equal(0, imported.NeedsAttentionCount);
        Assert.All(imported.Items, item => Assert.Equal(ImportItemStatus.ReadyToAdd, item.Status));
        Assert.Equal(3, (await Service().ImportSelectedAsync(selections)).ExistingCount);
        Assert.Equal(1, Count("tv_shows")); Assert.Equal(2, Count("tv_seasons")); Assert.Equal(3, Count("tv_episodes"));
        Assert.Equal(3, Count("episode_media_files")); Assert.Equal(3, Count("media_files")); Assert.Equal(0, Count("movies"));
    }

    [Fact]
    public async Task Existing_target_additional_file_same_episode_and_alias_survive_metadata_title_change()
    {
        var first = Media("Original.Show.S01E01.mkv");
        await Service().ImportSelectedAsync([new(first, LocalOnly: true)]);
        int id;
        using (var uow = _factory.Begin())
        {
            id = Assert.Single(uow.TvShows.ListAll()).Id;
            uow.TvShows.UpdateMetadata(id, new TvShowCatalogData("tmdb", "42", "Canonical"), DateTimeOffset.UtcNow);
            uow.Commit();
        }
        var additional = Media("Corrected.Show.S01E01.1080p.mkv");
        var result = await Service().ImportSelectedAsync([new(additional, ExistingShowId: id, OriginalLocalTitle: "Original Show")]);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, Count("tv_shows")); Assert.Equal(1, Count("tv_episodes")); Assert.Equal(2, Count("episode_media_files"));
        using var read = _factory.Begin();
        Assert.Equal(id, read.TvShows.GetByAlias("Original Show")!.Id);
        Assert.Equal(id, read.TvShows.GetByAlias("Corrected Show")!.Id);
        Assert.Equal("42", read.TvShows.GetById(id)!.Data.ExternalId);
    }

    [Fact]
    public async Task Alias_conflict_refuses_existing_target_without_reassociation()
    {
        await Service().ImportSelectedAsync([new(Media("First.Show.S01E01.mkv")), new(Media("Second.Show.S01E01.mkv"))]);
        int target;
        using (var uow = _factory.Begin()) target = uow.TvShows.GetByAlias("Second Show")!.Id;
        var refused = await Service().ImportSelectedAsync([new(Media("First.Show.S01E02.mkv"), ExistingShowId: target)]);
        Assert.Equal(1, refused.RegistrationFailuresCount);
        Assert.Equal(2, Count("tv_shows")); Assert.Equal(2, Count("tv_episodes")); Assert.Equal(2, Count("media_files"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removed_or_modified_after_scan_is_refused(bool modified)
    {
        var item = Media("Show.S01E01.mkv");
        if (modified) File.SetLastWriteTimeUtc(item.Path, item.Observation!.LastWriteTimeUtc.AddMinutes(1));
        else File.Delete(item.Path);
        var result = await Service().ImportSelectedAsync([new(item)]);
        Assert.Equal(1, result.RegistrationFailuresCount);
        Assert.Equal(0, Count("tv_shows")); Assert.Equal(0, Count("media_files"));
    }

    [Fact]
    public async Task Corrected_identity_is_saved_and_original_folder_name_is_preserved_as_alias()
    {
        var item = Media("Local Name/Season 02/S02E01.mkv");
        var candidate = Assert.Single(new TvBatchAnalysisService().Analyze(Path.Combine(_root, "media", "Local Name"), [item]).AllFiles);
        var corrected = candidate.WithCorrection("Reviewed Name", 3, 4);
        Assert.Equal(item.Observation, corrected.CurrentItem.Observation);
        var result = await Service().ImportSelectedAsync([new(corrected.CurrentItem, OriginalLocalTitle: candidate.Title, LocalOnly: true)]);
        Assert.Equal(1, result.AddedCount);
        using var uow = _factory.Begin();
        var show = Assert.Single(uow.TvShows.ListAll());
        Assert.Equal("Reviewed Name", show.Title);
        Assert.Equal(show.Id, uow.TvShows.GetByAlias("Local Name")!.Id);
        var season = Assert.Single(uow.TvSeasons.ListByShow(show.Id));
        Assert.Equal(3, season.Number); Assert.Equal(4, Assert.Single(uow.TvEpisodes.ListBySeason(season.Id)).Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Movie_file_removed_or_modified_after_scan_is_also_refused(bool modified)
    {
        var item = Media("Example.Movie.2024.mkv");
        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
        if (modified) File.SetLastWriteTimeUtc(item.Path, item.Observation!.LastWriteTimeUtc.AddMinutes(1));
        else File.Delete(item.Path);
        var result = await Service().ImportSelectedAsync([new(item, LocalOnly: true)]);
        Assert.Equal(1, result.RegistrationFailuresCount);
        Assert.Equal(0, Count("movies")); Assert.Equal(0, Count("media_files"));
    }

    [Fact]
    public async Task Available_movie_import_is_preserved_with_file_validation_enabled()
    {
        var result = await Service().ImportSelectedAsync([new(Media("Example.Movie.2024.mkv"), LocalOnly: true)]);
        Assert.Equal(1, result.AddedCount); Assert.Equal(0, result.RegistrationFailuresCount);
        Assert.Equal(1, Count("movies")); Assert.Equal(1, Count("media_files")); Assert.Equal(0, Count("tv_shows"));
    }

    [Fact]
    public async Task Public_movie_registration_cannot_reassociate_a_tv_owned_path()
    {
        var episode = Media("Show.S01E01.mkv");
        await Service().ImportSelectedAsync([new(episode, LocalOnly: true)]);
        var movie = Media("Example.Movie.2024.mkv");
        Assert.Throws<InvalidOperationException>(() => Service().RegisterMovieImport(new(
            episode.Path, episode.FileSize!.Value, movie.ParsedMedia!, DateTimeOffset.UtcNow)));
        Assert.Equal(0, Count("movies")); Assert.Equal(1, Count("tv_episodes")); Assert.Equal(1, Count("episode_media_files"));
        using var uow = _factory.Begin();
        Assert.Null(uow.MediaFiles.GetByPath(episode.Path)!.MovieId);
    }

    [Fact]
    public async Task Corrected_title_cannot_silently_redirect_to_owner_of_original_alias()
    {
        await Service().ImportSelectedAsync([new(Media("Original.Show.S01E01.mkv"))]);
        var result = await Service().ImportSelectedAsync([new(Media("Different.Show.S01E02.mkv"), OriginalLocalTitle: "Original Show")]);
        Assert.Equal(1, result.RegistrationFailuresCount); Assert.Equal(1, Count("tv_episodes")); Assert.Equal(1, Count("tv_shows"));
    }

    [Fact]
    public async Task Metadata_identity_owned_by_another_series_is_refused_before_local_registration()
    {
        int target;
        using (var uow = _factory.Begin())
        {
            target = uow.TvShows.Create(new TvShowCatalogData("tmdb", "42", "Existing"), DateTimeOffset.UtcNow).Id;
            uow.Commit();
        }
        var preview = new ImportMetadataPreview(ImportItemStatus.Matched, MatchStatus.Matched,
            TvCandidate: new TvCandidate("tmdb", "42", "Existing", null, null, null, null, null), IsManual: true);
        var media = Media("Local.Show.S01E01.mkv");
        var refused = await Service().ImportSelectedAsync([new(media, preview)]);
        Assert.Equal(1, refused.RegistrationFailuresCount); Assert.Equal(0, Count("media_files")); Assert.Equal(1, Count("tv_shows"));
        var selected = await Service().ImportSelectedAsync([new(media, preview, ExistingShowId: target)]);
        Assert.Equal(1, selected.AddedCount); Assert.Equal(1, Count("tv_shows"));
    }

    [Fact]
    public async Task Cancellation_keeps_first_commit_and_reports_remaining_items_without_full_progress()
    {
        var selections = Enumerable.Range(1, 3).Select(n => new ImportSelection(Media($"Show.S01E{n:00}.mkv"), LocalOnly: true)).ToArray();
        using var cancellation = new CancellationTokenSource();
        var progress = new List<ImportProgress>();
        var result = await Service().ImportSelectedAsync(selections, cancellation.Token, p =>
        {
            progress.Add(p);
            if (p.Processed == 1) cancellation.Cancel();
        });
        Assert.Equal(1, result.AddedCount); Assert.Equal(2, result.NotProcessedCount); Assert.Equal(3, result.Items.Count);
        Assert.Equal(1, progress.Max(p => p.Processed)); Assert.Equal(1, Count("tv_episodes"));
        var retry = await Service().ImportSelectedAsync(selections);
        Assert.Equal(2, retry.AddedCount); Assert.Equal(1, retry.ExistingCount);
    }

    [Fact]
    public async Task Excluded_unresolved_file_never_reaches_validation_or_persistence()
    {
        var excluded = Media("Show.S01E01.mkv"); File.Delete(excluded.Path);
        var result = await Service().ImportSelectedAsync([new(excluded, IsExcluded: true), new(Media("Show.S01E02.mkv"))]);
        Assert.Equal(1, result.SkippedCount); Assert.Equal(1, result.AddedCount); Assert.Equal(1, Count("tv_episodes"));
    }

    [Fact]
    public async Task Database_failure_rolls_back_one_item_continues_and_can_be_retried()
    {
        Sql("CREATE TRIGGER qa_reject BEFORE INSERT ON media_files WHEN NEW.current_path LIKE '%reject%' BEGIN SELECT RAISE(ABORT, 'QA failure'); END;");
        var selections = new[] { new ImportSelection(Media("Show.S01E01.mkv")), new(Media("reject/Show.S01E02.mkv")), new(Media("Show.S01E03.mkv")) };
        var result = await Service().ImportSelectedAsync(selections);
        Assert.Equal(2, result.AddedCount); Assert.Equal(1, result.RegistrationFailuresCount); Assert.Equal(2, Count("tv_episodes"));
        Sql("DROP TRIGGER qa_reject;");
        var retry = await Service().ImportSelectedAsync([selections[1]]);
        Assert.Equal(1, retry.AddedCount); Assert.Equal(3, Count("tv_episodes"));
    }

    [Fact]
    public async Task Provider_failure_keeps_successful_local_records()
    {
        var provider = new ThrowingProvider();
        var result = await Service(provider).ImportSelectedAsync([new(Media("Show.S01E01.mkv")), new(Media("Show.S01E02.mkv"))]);
        Assert.Equal(2, result.AddedCount); Assert.Equal(2, result.MetadataErrorsCount);
        Assert.Equal(1, provider.SearchCalls);
        Assert.Equal(1, Count("tv_shows")); Assert.Equal(2, Count("tv_episodes"));
    }

    [Fact]
    public async Task Local_only_skips_configured_provider_and_preserves_pending_local_metadata()
    {
        var provider = new ThrowingProvider();
        var result = await Service(provider).ImportSelectedAsync([
            new(Media("Show.S01E01.mkv"), LocalOnly: true), new(Media("Show.S01E02.mkv"), LocalOnly: true)]);
        Assert.Equal(2, result.AddedCount); Assert.Equal(0, result.NeedsAttentionCount);
        Assert.Equal(0, provider.SearchCalls);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Pending, Assert.Single(uow.TvShows.ListAll()).MetadataStatus);
    }

    private class Offline : IMetadataProvider
    {
        public string ProviderName => "qa";
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;
    }
    private sealed class ThrowingProvider : Offline, IMetadataProvider
    {
        public int SearchCalls { get; private set; }
        public bool IsConfigured => true;
        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            throw new IOException("Isolated provider failure");
        }
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var absolute = Path.GetFullPath(_root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), absolute, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(absolute, true);
    }
}
