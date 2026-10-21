using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Trakt;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;
using DropSort.Domain.Library.Shows;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TraktLocalSnapshotBuilderTests
{
    [Theory]
    [InlineData("tmdb", "100", 100, null, null, null)]
    [InlineData("imdb", "tt0137523", null, "tt0137523", null, null)]
    [InlineData("trakt", "42", null, null, 42L, null)]
    [InlineData("tvdb", "200", null, null, null, 200)]
    public async Task Movie_candidate_extracts_exact_provider_ids_without_title_matching(
        string provider, string externalId, int? expectedTmdb, string? expectedImdb, long? expectedTrakt, int? expectedTvdb)
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var movie = CreateMovie(1, "Fight Club", provider, externalId);
        movieRepo.Add(movie);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        var candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal(1, candidate.LocalId);
        Assert.Equal(TraktMediaKind.Movie, candidate.Kind);
        Assert.Equal(expectedTmdb, candidate.Identity.TmdbId);
        Assert.Equal(expectedImdb, candidate.Identity.ImdbId);
        Assert.Equal(expectedTrakt, candidate.Identity.TraktId);
        Assert.Equal(expectedTvdb, candidate.Identity.TvdbId);

        // Never matches by title alone
        var titleOnlyQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 99999);
        var matchResult = snapshot.Match(titleOnlyQuery);
        Assert.Equal(TraktMatchStatus.NoMatch, matchResult.Status);
    }

    [Fact]
    public async Task Episode_direct_external_id_only_when_parent_provider_lineage_verified()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        // Show 1: TMDB verified provider -> season & episode external IDs come from TMDB enrich path
        var show1 = CreateShow(1, "Breaking Bad", "tmdb", "1396");
        showRepo.Add(show1);
        var season1 = CreateSeason(10, 1, 1, "Season 1", externalId: "3572");
        seasonRepo.Add(season1);
        var ep1 = CreateEpisode(100, 10, 1, "Pilot", externalId: "62085");
        episodeRepo.Add(ep1);

        // Show 2: TVDB show provider -> episode externalId must NOT be inferred as TVDB direct ID
        var show2 = CreateShow(2, "TVDB Show", "tvdb", "81189");
        showRepo.Add(show2);
        var season2 = CreateSeason(20, 2, 1, "Season 1", externalId: "5555");
        seasonRepo.Add(season2);
        var ep2 = CreateEpisode(200, 20, 1, "Episode 1", externalId: "9999");
        episodeRepo.Add(ep2);

        // Show 3: IMDb show provider -> episode externalId must NOT be inferred as IMDb direct ID
        var show3 = CreateShow(3, "IMDb Show", "imdb", "tt0903747");
        showRepo.Add(show3);
        var season3 = CreateSeason(30, 3, 1, "Season 1");
        seasonRepo.Add(season3);
        var ep3 = CreateEpisode(300, 30, 1, "Episode 1", externalId: "tt0959621");
        episodeRepo.Add(ep3);

        // Show 4: Unverified / missing provider
        var show4 = CreateShow(4, "Home Movies", null, null);
        showRepo.Add(show4);
        var season4 = CreateSeason(40, 4, 1, "Season 1");
        seasonRepo.Add(season4);
        var ep4 = CreateEpisode(400, 40, 1, "Episode 1", externalId: "8888");
        episodeRepo.Add(ep4);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        // Episode 1 (TMDB lineage verified): has direct TmdbId and parent coordinates
        var ep1Candidate = snapshot.Candidates.First(c => c.LocalId == 100 && c.Kind == TraktMediaKind.Episode);
        Assert.Equal(62085, ep1Candidate.Identity.TmdbId);
        Assert.Equal(1396, ep1Candidate.Identity.ShowTmdbId);
        Assert.Equal(1, ep1Candidate.Identity.SeasonNumber);
        Assert.Equal(1, ep1Candidate.Identity.EpisodeNumber);

        // Season 1 (TMDB lineage verified): has direct TmdbId and parent coordinates
        var s1Candidate = snapshot.Candidates.First(c => c.LocalId == 10 && c.Kind == TraktMediaKind.Season);
        Assert.Equal(3572, s1Candidate.Identity.TmdbId);
        Assert.Equal(1396, s1Candidate.Identity.ShowTmdbId);
        Assert.Equal(1, s1Candidate.Identity.SeasonNumber);

        // Episode 2 (TVDB show): direct TVDB ID must NOT be inferred (direct IDs remain null, parent+coords preserved)
        var ep2Candidate = snapshot.Candidates.First(c => c.LocalId == 200 && c.Kind == TraktMediaKind.Episode);
        Assert.Null(ep2Candidate.Identity.TvdbId);
        Assert.Null(ep2Candidate.Identity.TmdbId);
        Assert.Equal(81189, ep2Candidate.Identity.ShowTvdbId);
        Assert.Equal(1, ep2Candidate.Identity.SeasonNumber);
        Assert.Equal(1, ep2Candidate.Identity.EpisodeNumber);

        // Episode 3 (IMDb show): direct IMDb ID must NOT be inferred
        var ep3Candidate = snapshot.Candidates.First(c => c.LocalId == 300 && c.Kind == TraktMediaKind.Episode);
        Assert.Null(ep3Candidate.Identity.ImdbId);
        Assert.Equal("tt0903747", ep3Candidate.Identity.ShowImdbId);

        // Episode 4 (unverified lineage): direct IDs and parent IDs are null
        var ep4Candidate = snapshot.Candidates.First(c => c.LocalId == 400 && c.Kind == TraktMediaKind.Episode);
        Assert.Null(ep4Candidate.Identity.TmdbId);
        Assert.Null(ep4Candidate.Identity.TvdbId);
        Assert.Null(ep4Candidate.Identity.ShowTmdbId);
    }

    [Fact]
    public async Task Availability_independent_uses_inspector_and_ignores_persisted_catalog_row_status()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        // Movie 1: DB says Present, but inspector says Missing -> Missing
        var movie1 = CreateMovie(1, "Movie 1", "tmdb", "101");
        movieRepo.Add(movie1);
        var file1 = CreateMediaFile(1, 1, @"C:\media\movie1.mkv", MediaFileStatus.Present);
        mediaFileRepo.AddFile(file1);
        inspector.SetStatus(@"C:\media\movie1.mkv", AvailabilityInspectionStatus.Missing);

        // Movie 2: DB says Missing, but inspector says Present -> Available
        var movie2 = CreateMovie(2, "Movie 2", "tmdb", "102");
        movieRepo.Add(movie2);
        var file2 = CreateMediaFile(2, 2, @"C:\media\movie2.mkv", MediaFileStatus.Missing);
        mediaFileRepo.AddFile(file2);
        inspector.SetStatus(@"C:\media\movie2.mkv", AvailabilityInspectionStatus.Present);

        // Movie 3: inspector says StorageUnavailable -> Unknown
        var movie3 = CreateMovie(3, "Movie 3", "tmdb", "103");
        movieRepo.Add(movie3);
        var file3 = CreateMediaFile(3, 3, @"D:\offline\movie3.mkv", MediaFileStatus.Present);
        mediaFileRepo.AddFile(file3);
        inspector.SetStatus(@"D:\offline\movie3.mkv", AvailabilityInspectionStatus.StorageUnavailable);

        // Movie 4: inspector says Error -> Unknown
        var movie4 = CreateMovie(4, "Movie 4", "tmdb", "104");
        movieRepo.Add(movie4);
        var file4 = CreateMediaFile(4, 4, @"E:\error\movie4.mkv", MediaFileStatus.Present);
        mediaFileRepo.AddFile(file4);
        inspector.SetStatus(@"E:\error\movie4.mkv", AvailabilityInspectionStatus.Error);

        // Movie 5: No files registered -> Missing
        var movie5 = CreateMovie(5, "Movie 5", "tmdb", "105");
        movieRepo.Add(movie5);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        Assert.Equal(TraktAvailability.Missing, snapshot.Candidates.First(c => c.LocalId == 1).Availability);
        Assert.Equal(TraktAvailability.Available, snapshot.Candidates.First(c => c.LocalId == 2).Availability);
        Assert.Equal(TraktAvailability.Unknown, snapshot.Candidates.First(c => c.LocalId == 3).Availability);
        Assert.Equal(TraktAvailability.Unknown, snapshot.Candidates.First(c => c.LocalId == 4).Availability);
        Assert.Equal(TraktAvailability.Missing, snapshot.Candidates.First(c => c.LocalId == 5).Availability);
    }

    [Fact]
    public async Task Shows_and_seasons_with_available_episodes_are_conservatively_partial()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var show = CreateShow(1, "Drama", "tmdb", "500");
        showRepo.Add(show);
        var season = CreateSeason(10, 1, 1, "Season 1");
        seasonRepo.Add(season);
        var ep1 = CreateEpisode(100, 10, 1, "Ep 1");
        var ep2 = CreateEpisode(101, 10, 2, "Ep 2");
        episodeRepo.Add(ep1);
        episodeRepo.Add(ep2);

        // Both episodes available locally
        var file1 = CreateMediaFile(1, null, @"C:\shows\ep1.mkv", MediaFileStatus.Present);
        var file2 = CreateMediaFile(2, null, @"C:\shows\ep2.mkv", MediaFileStatus.Present);
        inspector.SetStatus(@"C:\shows\ep1.mkv", AvailabilityInspectionStatus.Present);
        inspector.SetStatus(@"C:\shows\ep2.mkv", AvailabilityInspectionStatus.Present);

        episodeRepo.LinkFile(100, file1);
        episodeRepo.LinkFile(101, file2);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        // Individual episodes are Available
        Assert.Equal(TraktAvailability.Available, snapshot.Candidates.First(c => c.LocalId == 100 && c.Kind == TraktMediaKind.Episode).Availability);
        Assert.Equal(TraktAvailability.Available, snapshot.Candidates.First(c => c.LocalId == 101 && c.Kind == TraktMediaKind.Episode).Availability);

        // Season and Show are Partial (never claim whole show Available from locally registered subset)
        var seasonCandidate = snapshot.Candidates.First(c => c.LocalId == 10 && c.Kind == TraktMediaKind.Season);
        Assert.Equal(TraktAvailability.Partial, seasonCandidate.Availability);

        var showCandidate = snapshot.Candidates.First(c => c.LocalId == 1 && c.Kind == TraktMediaKind.Show);
        Assert.Equal(TraktAvailability.Partial, showCandidate.Availability);
    }

    [Fact]
    public async Task Batched_file_association_read_invokes_ListMediaFilesByShow_once_per_show()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var show = CreateShow(1, "Batched Show", "tmdb", "777");
        showRepo.Add(show);
        var season = CreateSeason(10, 1, 1, "Season 1");
        seasonRepo.Add(season);
        for (int i = 1; i <= 5; i++)
        {
            var ep = CreateEpisode(100 + i, 10, i, $"Ep {i}");
            episodeRepo.Add(ep);
        }

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        await builder.BuildSnapshotAsync();

        Assert.Equal(1, episodeRepo.ListMediaFilesByShowCallCount);
    }

    [Fact]
    public async Task Cache_path_inspection_per_build_queries_inspector_once_per_distinct_path()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var movie1 = CreateMovie(1, "Movie 1", "tmdb", "101");
        var movie2 = CreateMovie(2, "Movie 2", "tmdb", "102");
        movieRepo.Add(movie1);
        movieRepo.Add(movie2);

        // Both point to the exact same path
        var file1 = CreateMediaFile(1, 1, @"C:\shared\file.mkv", MediaFileStatus.Present);
        var file2 = CreateMediaFile(2, 2, @"C:\shared\file.mkv", MediaFileStatus.Present);
        mediaFileRepo.AddFile(file1);
        mediaFileRepo.AddFile(file2);
        inspector.SetStatus(@"C:\shared\file.mkv", AvailabilityInspectionStatus.Present);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        await builder.BuildSnapshotAsync();

        Assert.Equal(1, inspector.InspectCallCount);
    }

    [Fact]
    public async Task Cancellation_token_aborts_snapshot_creation()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildSnapshotAsync(cts.Token));
    }

    [Fact]
    public async Task Movie_media_files_loaded_once_via_ListAll_avoiding_N_plus_1_queries()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        for (int i = 1; i <= 10; i++)
        {
            var movie = CreateMovie(i, $"Movie {i}", "tmdb", $"{1000 + i}");
            movieRepo.Add(movie);
            var file = CreateMediaFile(i, i, $@"C:\media\movie{i}.mkv", MediaFileStatus.Present);
            mediaFileRepo.AddFile(file);
            inspector.SetStatus($@"C:\media\movie{i}.mkv", AvailabilityInspectionStatus.Present);
        }

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        Assert.Equal(10, snapshot.Candidates.Count(c => c.Kind == TraktMediaKind.Movie));
        // Single call to ListAll loads all movie media files
        Assert.Equal(1, mediaFileRepo.ListAllCallCount);
        // GetByMovieId is NEVER called per movie (0 calls)
        Assert.Equal(0, mediaFileRepo.GetByMovieIdCallCount);
    }

    [Fact]
    public async Task Episode_with_missing_season_omits_identity_and_does_not_falsely_match_specials()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        var show = CreateShow(1, "Orphaned Episode Show", "tmdb", "5555");
        showRepo.Add(show);

        // Episode has SeasonId = 999 which does NOT exist in seasonRepo
        var orphanEpisode = CreateEpisode(101, seasonId: 999, number: 1, title: "Mystery Episode");
        episodeRepo.AddDirect(orphanEpisode);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        var epCandidate = Assert.Single(snapshot.Candidates, c => c.LocalId == 101 && c.Kind == TraktMediaKind.Episode);
        // Missing season omits coordinates/identity -> IsValid is false
        Assert.False(epCandidate.Identity.IsValid);
        Assert.Null(epCandidate.Identity.SeasonNumber);

        // Query for season 0 special (S00E01) must NOT match this orphan episode
        var specialQuery = new TraktIdentity(
            TraktMediaKind.Episode,
            ShowTmdbId: 5555,
            SeasonNumber: 0,
            EpisodeNumber: 1);

        var matchResult = snapshot.Match(specialQuery);
        Assert.Equal(TraktMatchStatus.NoMatch, matchResult.Status);
    }

    [Fact]
    public async Task Cancellation_during_file_inspection_rethrows_OperationCanceled_and_is_not_swallowed()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new CancellingAvailabilityInspector();

        var movie = CreateMovie(1, "Movie 1", "tmdb", "101");
        movieRepo.Add(movie);
        var file = CreateMediaFile(1, 1, @"C:\media\movie1.mkv", MediaFileStatus.Present);
        mediaFileRepo.AddFile(file);

        using var cts = new CancellationTokenSource();
        inspector.CtsToCancel = cts;

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);

        // Must rethrow OperationCanceledException instead of catching and returning Error/Unknown
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildSnapshotAsync(cts.Token));
    }

    [Fact]
    public async Task Imdb_id_requires_tt_with_at_least_7_digits_and_trims()
    {
        var movieRepo = new FakeMovieRepository();
        var mediaFileRepo = new FakeMediaFileRepository();
        var showRepo = new FakeTvShowRepository();
        var seasonRepo = new FakeTvSeasonRepository();
        var episodeRepo = new FakeTvEpisodeRepository(seasonRepo);
        var inspector = new FakeAvailabilityInspector();

        // 1. Valid IMDb ID with whitespace -> trimmed and valid
        var movieValid = CreateMovie(1, "Valid Movie", "imdb", "  tt0137523  ");
        movieRepo.Add(movieValid);

        // 2. Invalid IMDb ID with fewer than 7 digits (tt123)
        var movieInvalid = CreateMovie(2, "Invalid Movie", "imdb", "tt123");
        movieRepo.Add(movieInvalid);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, mediaFileRepo, showRepo, seasonRepo, episodeRepo, inspector);
        var snapshot = await builder.BuildSnapshotAsync();

        var validCandidate = snapshot.Candidates.First(c => c.LocalId == 1);
        Assert.Equal("tt0137523", validCandidate.Identity.ImdbId);
        Assert.True(validCandidate.Identity.IsValid);

        var invalidCandidate = snapshot.Candidates.First(c => c.LocalId == 2);
        // tt123 is invalid -> Identity.IsValid is false
        Assert.False(invalidCandidate.Identity.IsValid);

        // Remote matching ignores invalid candidate
        var query = new TraktIdentity(TraktMediaKind.Movie, ImdbId: "tt0137523");
        var match = snapshot.Match(query);
        Assert.Equal(TraktMatchStatus.Matched, match.Status);
        Assert.Equal(1, match.Candidate?.LocalId);
    }

    // --- Helpers and Fakes ---

    private static Movie CreateMovie(int id, string title, string? provider, string? externalId)
    {
        var data = new MovieCatalogData(
            provider: provider,
            externalId: externalId,
            title: title,
            originalTitle: null,
            year: 2020,
            overview: "Overview",
            genres: ImmutableArray<string>.Empty,
            runtimeMinutes: 120,
            rating: 8.0,
            posterReference: null,
            metadataStatus: provider != null ? MetadataStatus.Ready : MetadataStatus.Pending);

        return new Movie(id, data, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, reviewMatch: false, lastOpenedAt: null);
    }

    private static TvShow CreateShow(int id, string title, string? provider, string? externalId)
    {
        var data = new TvShowCatalogData(
            provider: provider,
            externalId: externalId,
            title: title,
            year: 2020,
            metadataStatus: provider != null ? MetadataStatus.Ready : MetadataStatus.Pending);

        return new TvShow(id, data, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private static TvSeason CreateSeason(int id, int showId, int number, string title, string? externalId = null) =>
        new(id, showId, number, title, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, externalId: externalId);

    private static TvEpisode CreateEpisode(int id, int seasonId, int number, string title, string? externalId = null) =>
        new(id, seasonId, number, title, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, externalId: externalId);

    private static MediaFile CreateMediaFile(int id, int? movieId, string path, MediaFileStatus status) =>
        new(id, movieId, path, 1024L, ".mkv", "1080p", "H.264", "BRRip", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeAvailabilityInspector : IAvailabilityInspector
    {
        private readonly Dictionary<string, AvailabilityInspectionStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);
        public int InspectCallCount { get; private set; }

        public void SetStatus(string path, AvailabilityInspectionStatus status) => _statuses[path] = status;

        public AvailabilityInspection Inspect(string path)
        {
            InspectCallCount++;
            var status = _statuses.TryGetValue(path, out var s) ? s : AvailabilityInspectionStatus.Missing;
            return new AvailabilityInspection(path, status);
        }

        public bool IsStorageAvailable(string path) => true;
    }

    private sealed class CancellingAvailabilityInspector : IAvailabilityInspector
    {
        public CancellationTokenSource? CtsToCancel { get; set; }

        public AvailabilityInspection Inspect(string path)
        {
            CtsToCancel?.Cancel();
            throw new OperationCanceledException("Inspection was cancelled.");
        }

        public bool IsStorageAvailable(string path) => true;
    }

    private sealed class FakeMovieRepository : IMovieRepository
    {
        private readonly List<Movie> _movies = new();
        public void Add(Movie movie) => _movies.Add(movie);
        public IReadOnlyList<Movie> ListAll() => _movies;
        public Movie? GetById(int id) => _movies.FirstOrDefault(m => m.Id == id);
        public Movie? GetByExternalId(string provider, string externalId) =>
            _movies.FirstOrDefault(m => string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase) && m.ExternalId == externalId);
        public IReadOnlyList<Movie> ListPage(int afterId, int limit) => _movies.Where(m => m.Id > afterId).Take(limit).ToList();
        public int CountAll() => _movies.Count;
        public Movie Create(MovieCatalogData data, DateTimeOffset now) => throw new NotImplementedException();
        public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now) => throw new NotImplementedException();
        public void SetReviewMatch(int id, bool value) => throw new NotImplementedException();
        public void RecordOpened(int id, DateTimeOffset openedAt) => throw new NotImplementedException();
        public void Delete(int id) => throw new NotImplementedException();
    }

    private sealed class FakeMediaFileRepository : IMediaFileRepository
    {
        private readonly List<MediaFile> _files = new();
        public int ListAllCallCount { get; private set; }
        public int GetByMovieIdCallCount { get; private set; }

        public void AddFile(MediaFile file) => _files.Add(file);
        public IReadOnlyList<MediaFile> GetByMovieId(int movieId)
        {
            GetByMovieIdCallCount++;
            return _files.Where(f => f.MovieId == movieId).ToList();
        }
        public MediaFile? GetById(int id) => _files.FirstOrDefault(f => f.Id == id);
        public MediaFile? GetByPath(string path) => _files.FirstOrDefault(f => f.CurrentPath == path);
        public IReadOnlyList<MediaFile> ListAll()
        {
            ListAllCallCount++;
            return _files;
        }
        public IReadOnlyList<MediaFile> ListMissing() => _files.Where(f => f.Status == MediaFileStatus.Missing).ToList();
        public IReadOnlySet<int> ListMovieIdsWithPresentFile() => _files.Where(f => f.MovieId.HasValue && f.Status == MediaFileStatus.Present).Select(f => f.MovieId!.Value).ToHashSet();
        public IReadOnlyDictionary<int, int> CountFilesByMovie() => _files.Where(f => f.MovieId.HasValue).GroupBy(f => f.MovieId!.Value).ToDictionary(g => g.Key, g => g.Count());
        public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) => throw new NotImplementedException();
        public MediaFile LinkToMovie(int id, int movieId) => throw new NotImplementedException();
        public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) => throw new NotImplementedException();
        public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => throw new NotImplementedException();
        public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) => throw new NotImplementedException();
        public void Delete(int id) => throw new NotImplementedException();
    }

    private sealed class FakeTvShowRepository : ITvShowRepository
    {
        private readonly List<TvShow> _shows = new();
        public void Add(TvShow show) => _shows.Add(show);
        public IReadOnlyList<TvShow> ListAll() => _shows;
        public TvShow? GetById(int id) => _shows.FirstOrDefault(s => s.Id == id);
        public TvShow? GetBySortTitle(string sortTitle) => _shows.FirstOrDefault(s => s.SortTitle == sortTitle);
        public TvShow? GetByAlias(string aliasSortTitle) => null;
        public void RegisterAlias(int showId, string aliasSortTitle, DateTimeOffset now) { }
        public void RecordOpened(int id, DateTimeOffset openedAt) { }
        public TvShow? GetByExternalId(string provider, string externalId) =>
            _shows.FirstOrDefault(s => string.Equals(s.Provider, provider, StringComparison.OrdinalIgnoreCase) && s.ExternalId == externalId);
        public int CountAll() => _shows.Count;
        public TvShow Create(TvShowCatalogData data, DateTimeOffset now) => throw new NotImplementedException();
        public TvShow UpdateMetadata(int id, TvShowCatalogData data, DateTimeOffset now) => throw new NotImplementedException();
        public void Delete(int id) => throw new NotImplementedException();
    }

    private sealed class FakeTvSeasonRepository : ITvSeasonRepository
    {
        private readonly List<TvSeason> _seasons = new();
        public void Add(TvSeason season) => _seasons.Add(season);
        public IReadOnlyList<TvSeason> ListByShow(int showId) => _seasons.Where(s => s.ShowId == showId).ToList();
        public TvSeason? GetById(int id) => _seasons.FirstOrDefault(s => s.Id == id);
        public TvSeason? GetByNumber(int showId, int seasonNumber) => _seasons.FirstOrDefault(s => s.ShowId == showId && s.Number == seasonNumber);
        public TvSeason Create(int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now) => throw new NotImplementedException();
        public TvSeason UpdateMetadata(int id, string? title, string? overview, string? posterPath, string? airDate, string? externalId, DateTimeOffset now) => throw new NotImplementedException();
        public void Delete(int id) => throw new NotImplementedException();
    }

    private sealed class FakeTvEpisodeRepository : ITvEpisodeRepository
    {
        private readonly FakeTvSeasonRepository _seasonRepo;
        private readonly List<TvEpisode> _episodes = new();
        private readonly Dictionary<int, List<MediaFile>> _filesByEpisode = new();
        public int ListMediaFilesByShowCallCount { get; private set; }

        public FakeTvEpisodeRepository(FakeTvSeasonRepository seasonRepo)
        {
            _seasonRepo = seasonRepo;
        }

        public void Add(TvEpisode episode) => _episodes.Add(episode);

        public void AddDirect(TvEpisode episode) => _episodes.Add(episode);

        public void LinkFile(int episodeId, MediaFile file)
        {
            if (!_filesByEpisode.TryGetValue(episodeId, out var list))
            {
                list = new List<MediaFile>();
                _filesByEpisode[episodeId] = list;
            }
            list.Add(file);
        }

        public IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> ListMediaFilesByShow(int showId)
        {
            ListMediaFilesByShowCallCount++;
            return _filesByEpisode.ToDictionary(k => k.Key, v => (IReadOnlyList<MediaFile>)v.Value);
        }

        public IReadOnlyList<TvEpisode> ListByShow(int showId)
        {
            // Return all episodes for the show, or direct episodes if orphaned
            var seasonIds = _seasonRepo.ListByShow(showId).Select(s => s.Id).ToHashSet();
            return _episodes.Where(e => seasonIds.Contains(e.SeasonId) || e.SeasonId == 999).ToList();
        }
        public TvEpisode? GetById(int id) => _episodes.FirstOrDefault(e => e.Id == id);
        public TvEpisode? GetByNumber(int seasonId, int episodeNumber) => _episodes.FirstOrDefault(e => e.SeasonId == seasonId && e.Number == episodeNumber);
        public IReadOnlyList<TvEpisode> ListBySeason(int seasonId) => _episodes.Where(e => e.SeasonId == seasonId).ToList();
        public TvEpisode Create(int seasonId, int episodeNumber, string? title, string? overview, int? runtimeMinutes, DateTimeOffset? airDate, DateTimeOffset now) => throw new NotImplementedException();
        public TvEpisode UpdateMetadata(int id, string? title, string? overview, int? runtimeMinutes, string? airDate, double? rating, string? stillPath, string? externalId, DateTimeOffset now) => throw new NotImplementedException();
        public void Delete(int id) => throw new NotImplementedException();
        public void LinkMediaFile(int episodeId, int mediaFileId, DateTimeOffset now) => throw new NotImplementedException();
        public void UnlinkMediaFile(int mediaFileId) => throw new NotImplementedException();
        public int? GetEpisodeIdForMediaFile(int mediaFileId) => null;
        public IReadOnlyList<MediaFile> ListMediaFiles(int episodeId) =>
            _filesByEpisode.TryGetValue(episodeId, out var files) ? files : Array.Empty<MediaFile>();
    }
}
