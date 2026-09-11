using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Metadata.Contracts;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using DropSort.Tests.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class Migration7AndRepositoryEnrichmentTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_mig7_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;

    public Migration7AndRepositoryEnrichmentTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "migration7.db");
        _connectionString = $"Data Source={_databasePath}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void MetadataModels_SupportTvAndBackdropEnrichments()
    {
        var tvSearch = new TvSearchQuery("Breaking Bad", 2008, "Breaking Bad", "tmdb");
        Assert.Equal("Breaking Bad", tvSearch.Title);
        Assert.Equal(2008, tvSearch.FirstAirYear);

        var tvCandidate = new TvCandidate("tmdb", "1", "Show", "Orig", 2020, "Overview", 8.5, "/poster.jpg", "/backdrop.jpg");
        Assert.Equal("/backdrop.jpg", tvCandidate.BackdropReference);

        var episodeMeta = new TvEpisodeMetadata(1, "Pilot", "Pilot desc", 50, "2008-01-20", 9.0, "/still.jpg", "ep-1");
        var seasonMeta = new TvSeasonMetadata(1, "Season 1", "Season 1 desc", "/season_poster.jpg", "2008-01-20", ImmutableArray.Create(episodeMeta), "season-1");
        var showMeta = new TvShowMetadata(
            "tmdb", "1", "Show", null, 2008, "Show desc", ImmutableArray.Create("Drama"), 9.5, "/poster.jpg", "/backdrop.jpg",
            ImmutableArray.Create(seasonMeta), "Remember my name");

        Assert.Equal("Remember my name", showMeta.Tagline);
        Assert.Equal("/backdrop.jpg", showMeta.BackdropReference);
        Assert.Single(showMeta.Seasons);
        Assert.Single(showMeta.Seasons[0].Episodes);

        var movieCandidate = new MovieCandidate("tmdb", "10", "Movie", null, 2024, "Desc", 8.0, "/p.jpg", "/b.jpg", "Tagline");
        Assert.Equal("/b.jpg", movieCandidate.BackdropReference);
        Assert.Equal("Tagline", movieCandidate.Tagline);

        var movieMeta = new MovieMetadata("tmdb", "10", "Movie", null, 2024, "Desc", ImmutableArray.Create("Action"), 120, 8.0, "/p.jpg", "/b.jpg", "Movie Tagline");
        Assert.Equal("/b.jpg", movieMeta.BackdropReference);
        Assert.Equal("Movie Tagline", movieMeta.Tagline);
    }

    [Fact]
    public void DomainEntities_PreserveEnrichedProperties()
    {
        var movieData = new MovieCatalogData(
            "tmdb", "100", "Inception", null, 2010, "Overview",
            ImmutableArray.Create("Sci-Fi"), 148, 8.8, "/poster.jpg",
            MetadataStatus.Ready, "/backdrop.jpg", "Your mind is the scene of the crime");
        var movie = new Movie(1, movieData, Now, Now, Now);
        Assert.Equal("/backdrop.jpg", movie.BackdropReference);
        Assert.Equal("Your mind is the scene of the crime", movie.Tagline);

        var showData = new TvShowCatalogData(
            "tmdb", "200", "Severance", null, 2022, "Overview",
            ImmutableArray.Create("Thriller"), "/poster.jpg", MetadataStatus.Ready,
            8.7, "/show_bg.jpg", "Please do not attempt to leave");
        var show = new TvShow(1, showData, Now, Now, Now);
        Assert.Equal(8.7, show.Rating);
        Assert.Equal("/show_bg.jpg", show.BackdropReference);
        Assert.Equal("Please do not attempt to leave", show.Tagline);

        var season = new TvSeason(1, 1, 1, "Season 1", "Overview", Now, Now, "/s_poster.jpg", "2022-02-18", "s-1");
        Assert.Equal("/s_poster.jpg", season.PosterReference);
        Assert.Equal("2022-02-18", season.AirDate);
        Assert.Equal("s-1", season.ExternalId);

        var episode = new TvEpisode(1, 1, 1, "Good News", "Overview", 55, Now, Now, Now, "/still.jpg", 8.9, "ep-1");
        Assert.Equal("/still.jpg", episode.StillReference);
        Assert.Equal(8.9, episode.Rating);
        Assert.Equal("ep-1", episode.ExternalId);
    }

    [Fact]
    public void MovieRepository_RoundTripsBackdropAndTagline()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var repo = new MovieRepository(connection);

        var data = new MovieCatalogData(
            "tmdb", "300", "Interstellar", null, 2014, "Mankind was born on Earth.",
            ImmutableArray.Create("Adventure", "Drama", "Sci-Fi"), 169, 8.7, "/poster.jpg",
            MetadataStatus.Ready, "/interstellar_bg.jpg", "Mankind was born on Earth. It was never meant to die here.");

        var created = repo.Create(data, Now);
        Assert.Equal("/interstellar_bg.jpg", created.BackdropReference);
        Assert.Equal("Mankind was born on Earth. It was never meant to die here.", created.Tagline);

        var fetched = repo.GetById(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(created.BackdropReference, fetched.BackdropReference);
        Assert.Equal(created.Tagline, fetched.Tagline);

        var updatedData = new MovieCatalogData(
            "tmdb", "300", "Interstellar", null, 2014, "Updated overview",
            ImmutableArray.Create("Adventure", "Sci-Fi"), 169, 8.9, "/poster2.jpg",
            MetadataStatus.Ready, "/new_bg.jpg", "New tagline");
        var updated = repo.UpdateMetadata(created.Id, updatedData, Now.AddHours(1));
        Assert.Equal("/new_bg.jpg", updated.BackdropReference);
        Assert.Equal("New tagline", updated.Tagline);
    }

    [Fact]
    public void TvRepositories_RoundTripAllEnrichedFields()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var showRepo = new TvShowRepository(connection);
        var seasonRepo = new TvSeasonRepository(connection);
        var episodeRepo = new TvEpisodeRepository(connection);

        // 1. Show
        var showData = new TvShowCatalogData(
            "tmdb", "1001", "Dark", null, 2017, "Time travel thriller",
            ImmutableArray.Create("Sci-Fi", "Mystery"), "/dark_poster.jpg", MetadataStatus.Ready,
            8.7, "/dark_bg.jpg", "The question isn't where, but when");
        var show = showRepo.Create(showData, Now);
        Assert.Equal(8.7, show.Rating);
        Assert.Equal("/dark_bg.jpg", show.BackdropReference);
        Assert.Equal("The question isn't where, but when", show.Tagline);

        var fetchedShow = showRepo.GetById(show.Id)!;
        Assert.Equal(show.Rating, fetchedShow.Rating);
        Assert.Equal(show.BackdropReference, fetchedShow.BackdropReference);
        Assert.Equal(show.Tagline, fetchedShow.Tagline);

        // Update show metadata
        var updatedShowData = new TvShowCatalogData(
            "tmdb", "1001", "Dark", null, 2017, "Updated description",
            ImmutableArray.Create("Sci-Fi"), "/dark_poster2.jpg", MetadataStatus.Ready,
            8.9, "/dark_bg2.jpg", "Updated tagline");
        var updatedShow = showRepo.UpdateMetadata(show.Id, updatedShowData, Now.AddHours(1));
        Assert.Equal(8.9, updatedShow.Rating);
        Assert.Equal("/dark_bg2.jpg", updatedShow.BackdropReference);
        Assert.Equal("Updated tagline", updatedShow.Tagline);

        // 2. Season
        var season = seasonRepo.Create(show.Id, 1, "Season 1", "Overview", Now);
        Assert.Null(season.PosterReference);
        Assert.Null(season.AirDate);
        Assert.Null(season.ExternalId);

        var updatedSeason = seasonRepo.UpdateMetadata(
            season.Id, "Secrets", "Winden secrets", "/season1.jpg", "2017-12-01", "s1-ext", Now.AddHours(2));
        Assert.Equal("/season1.jpg", updatedSeason.PosterReference);
        Assert.Equal("2017-12-01", updatedSeason.AirDate);
        Assert.Equal("s1-ext", updatedSeason.ExternalId);

        var fetchedSeason = seasonRepo.GetById(season.Id)!;
        Assert.Equal(updatedSeason.PosterReference, fetchedSeason.PosterReference);
        Assert.Equal(updatedSeason.AirDate, fetchedSeason.AirDate);
        Assert.Equal(updatedSeason.ExternalId, fetchedSeason.ExternalId);

        // 3. Episode
        var episode = episodeRepo.Create(season.Id, 1, "Secrets", "Pilot", 51, Now, Now);
        Assert.Null(episode.StillReference);
        Assert.Null(episode.Rating);
        Assert.Null(episode.ExternalId);

        var updatedEpisode = episodeRepo.UpdateMetadata(
            episode.Id, "Secrets", "A child disappears", 52, "2017-12-01", 8.8, "/still1.jpg", "ep1-ext", Now.AddHours(3));
        Assert.Equal("/still1.jpg", updatedEpisode.StillReference);
        Assert.Equal(8.8, updatedEpisode.Rating);
        Assert.Equal("ep1-ext", updatedEpisode.ExternalId);

        var fetchedEpisode = episodeRepo.GetById(episode.Id)!;
        Assert.Equal(updatedEpisode.StillReference, fetchedEpisode.StillReference);
        Assert.Equal(updatedEpisode.Rating, fetchedEpisode.Rating);
        Assert.Equal(updatedEpisode.ExternalId, fetchedEpisode.ExternalId);

        var listedByShow = episodeRepo.ListByShow(show.Id);
        Assert.Single(listedByShow);
        Assert.Equal("/still1.jpg", listedByShow[0].StillReference);
        Assert.Equal(8.8, listedByShow[0].Rating);
        Assert.Equal("ep1-ext", listedByShow[0].ExternalId);
    }

    [Fact]
    public void FakeRepositories_SupportUpdateMetadata()
    {
        var seasonRepo = new FakeTvSeasonRepo();
        var episodeRepo = new FakeTvEpisodeRepo();

        var season = seasonRepo.Create(1, 1, "Season 1", "Overview", Now);
        var updatedSeason = seasonRepo.UpdateMetadata(season.Id, "S1", "Overview", "/poster.jpg", "2024-01-01", "ext-1", Now);
        Assert.Equal("/poster.jpg", updatedSeason.PosterReference);
        Assert.Equal("2024-01-01", updatedSeason.AirDate);
        Assert.Equal("ext-1", updatedSeason.ExternalId);

        var episode = episodeRepo.Create(season.Id, 1, "Ep 1", "Overview", 45, Now, Now);
        var updatedEpisode = episodeRepo.UpdateMetadata(episode.Id, "Ep 1", "Overview", 50, "2024-01-01", 8.5, "/still.jpg", "ep-ext", Now);
        Assert.Equal("/still.jpg", updatedEpisode.StillReference);
        Assert.Equal(8.5, updatedEpisode.Rating);
        Assert.Equal("ep-ext", updatedEpisode.ExternalId);
    }
}
