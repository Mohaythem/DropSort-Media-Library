using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Media.Parser;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Domain.Media.Matcher;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

[Collection("ImportSelectedSerial")]
public sealed class ImportSelectedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tbdmovies_import_" + Guid.NewGuid().ToString("N"));
    private readonly CatalogUnitOfWorkFactory _factory;

    public ImportSelectedTests()
    {
        Directory.CreateDirectory(_root);
        var connection = "Data Source=" + Path.Combine(_root, "catalog.db");
        new DatabaseMigrator(connection).Migrate();
        _factory = new CatalogUnitOfWorkFactory(connection);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task MixedSelection_RegistersOnlySelectedItems()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var movie = Discovered("Movie.mkv", MediaType.Movie, "Movie", 2020);
        var episode = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", null, 1, 1);

        var result = await service.ImportSelectedAsync([new ImportSelection(movie), new ImportSelection(episode)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.Equal(2, result.Items.Count);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        Assert.Single(uow.TvShows.ListAll());
    }

    [Fact]
    public async Task ImportSelected_ReportsRealProcessedOfTotalProgress()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var movie = Discovered("Movie.mkv", MediaType.Movie, "Movie", 2020);
        var episode = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", null, 1, 1);

        var updates = new List<ImportProgress>();
        var result = await service.ImportSelectedAsync(
            [new ImportSelection(movie), new ImportSelection(episode)],
            progress: update => updates.Add(update));

        Assert.Equal(2, result.RegisteredCount);
        Assert.NotEmpty(updates);
        // Total is always the selection count, Processed never exceeds it and is non-decreasing, and the
        // final report is the completed total.
        Assert.All(updates, u => Assert.Equal(2, u.Total));
        Assert.All(updates, u => Assert.InRange(u.Processed, 0, 2));
        for (var i = 1; i < updates.Count; i++)
        {
            Assert.True(updates[i].Processed >= updates[i - 1].Processed);
        }
        Assert.Equal(new ImportProgress(2, 2), updates[^1]);
    }

    // --- Duplicate identity: same movie in two folders becomes one Movie with many media files ---------

    private DiscoveredMedia MovieFile(string fileName, string title, int? year, string extension = ".mkv", string? resolution = "1080p")
    {
        var path = Path.Combine(_root, fileName);
        return new DiscoveredMedia(path, 100, new ParsedMedia(fileName, MediaType.Movie, title, year,
            resolution, "web", "h264", extension, null, null),
            DiscoveryClassification.MovieCandidate, null);
    }

    [Fact]
    public void ConfirmMovieImport_ReusesIdentityForThreeEditionsAndRejectsRepeatedPath()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var candidate = Candidate("Movie", "movie-1");
        var first = MovieFile("First.1080p.mkv", "Movie", 2020);
        var original = service.ConfirmMovieImport(new(first.Path, first.FileSize!.Value, first.ParsedMedia!,
            DateTimeOffset.UtcNow, candidate));
        var personal = new PersonalLibraryRepository("Data Source=" + Path.Combine(_root, "catalog.db"));
        var listId = personal.CreateCustomList("Favorites", DateTimeOffset.UtcNow);
        personal.AddMovieToCustomList(listId, original.Movie.Id, DateTimeOffset.UtcNow);
        personal.AddWatchEvent(original.Movie.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var second = MovieFile("Second.2160p.mp4", "Different filename", 2020, extension: ".mp4", resolution: "2160p");
        var third = MovieFile("Third.Directors.Cut.mkv", "Different filename", 2020);
        foreach (var file in new[] { second, third })
        {
            var added = service.ConfirmMovieImport(new(file.Path, file.FileSize!.Value, file.ParsedMedia!,
                DateTimeOffset.UtcNow, candidate));
            Assert.False(added.AlreadyRegistered);
            Assert.Equal(original.Movie, added.Movie);
        }
        // A repeated file cannot be reassigned even when another identity is selected.
        var repeated = service.ConfirmMovieImport(new(first.Path, first.FileSize!.Value, first.ParsedMedia!,
            DateTimeOffset.UtcNow, Candidate("Other", "other")));
        Assert.True(repeated.AlreadyRegistered);
        Assert.Equal(original.Movie.Id, repeated.Movie.Id);

        // Path identity normalizes case-insensitively: uppercase path is also recognized as already registered.
        var repeatedUpper = service.ConfirmMovieImport(new(first.Path.ToUpperInvariant(), first.FileSize!.Value, first.ParsedMedia!,
            DateTimeOffset.UtcNow, Candidate("Other", "other")));
        Assert.True(repeatedUpper.AlreadyRegistered);
        Assert.Equal(original.Movie.Id, repeatedUpper.Movie.Id);
        Assert.Equal(repeated.MediaFile.Id, repeatedUpper.MediaFile.Id);
        Assert.True(service.IsMediaFileRegistered(first.Path.ToUpperInvariant()));

        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        var files = uow.MediaFiles.GetByMovieId(original.Movie.Id);
        Assert.Equal(3, files.Count);
        Assert.Equal(3, files.Select(f => f.CurrentPath).Distinct().Count());
        Assert.Contains(files, f => f.Extension == ".mkv" && f.Resolution == "1080p" && f.CurrentPath == first.Path);
        Assert.Contains(files, f => f.Extension == ".mp4" && f.Resolution == "2160p" && f.CurrentPath == second.Path);
        Assert.Contains(files, f => f.Extension == ".mkv" && f.Resolution == "1080p" && f.CurrentPath == third.Path);
        Assert.NotNull(uow.MediaFiles.GetByPath(first.Path.ToUpperInvariant()));
        Assert.Equal(repeated.MediaFile.Id, uow.MediaFiles.GetByPath(first.Path.ToUpperInvariant())!.Id);
        Assert.Equal(1, provider.MovieDetailsCount);

        using var db = new SqliteConnection("Data Source=" + Path.Combine(_root, "catalog.db"));
        db.Open();
        foreach (var table in new[] { "custom_list_members", "watch_events" })
        {
            using var query = db.CreateCommand();
            query.CommandText = $"SELECT COUNT(*) FROM {table} WHERE movie_id = $id";
            query.Parameters.AddWithValue("$id", original.Movie.Id);
            Assert.Equal(1L, query.ExecuteScalar());
        }
    }

    [Fact]
    public async Task ConfirmedBatchSelection_ReusesExistingMovieWithoutConfiguredProvider()
    {
        var initialService = CreateService(new CountingProvider());
        var first = MovieFile("Original.mkv", "Movie", 2020);
        var candidate = Candidate("Movie", "movie-1");
        var original = initialService.ConfirmMovieImport(new(first.Path, first.FileSize!.Value, first.ParsedMedia!,
            DateTimeOffset.UtcNow, candidate));
        var offlineProvider = new CountingProvider { IsConfigured = false };
        var service = CreateService(offlineProvider);
        var preview = new ImportMetadataPreview(ImportItemStatus.Matched, MatchStatus.Matched,
            MovieCandidate: candidate, IsManual: true);
        var second = MovieFile("Second.mkv", "Alternate edition", null);
        var third = MovieFile("Third.mp4", "Alternate edition", null, ".mp4");
        var result = await service.ImportSelectedAsync(
            [new(second, preview), new(third, preview), new(second, preview)]);
        Assert.Equal(2, result.Items.Count(item => !item.AlreadyRegistered));
        Assert.Equal(ImportItemStatus.AlreadyInLibrary, result.Items[2].Status);
        Assert.All(result.Items.Take(2), item => Assert.Equal(ImportItemStatus.Matched, item.Status));
        using var uow = _factory.Begin();
        Assert.Equal(original.Movie, Assert.Single(uow.Movies.ListAll()));
        Assert.Equal(3, uow.MediaFiles.GetByMovieId(original.Movie.Id).Count);
        Assert.Equal(0, offlineProvider.MovieDetailsCount);
        Assert.Equal(0, offlineProvider.MovieSearchCount);
    }

    [Fact]
    public async Task SameTmdbMovieFromTwoFolders_ProducesOneMovieWithTwoMediaFiles()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var first = MovieFile("RootA_Movie.mkv", "Movie", 2020);
        var second = MovieFile("RootB_Movie.mkv", "Movie", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(first), new ImportSelection(second)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.All(result.Items, item => Assert.Equal(ImportItemStatus.Matched, item.Status));

        using var uow = _factory.Begin();
        var movies = uow.Movies.ListAll();
        // One logical movie, not two — no empty duplicate card.
        Assert.Single(movies);
        var movie = movies[0];
        // Metadata retained once on the single movie.
        Assert.Equal("tmdb", movie.Provider);
        Assert.Equal("movie-1", movie.ExternalId);
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        // Both local files are attached to it as distinct media files.
        var files = uow.MediaFiles.GetByMovieId(movie.Id);
        Assert.Equal(2, files.Count);
        Assert.Equal(2, files.Select(f => f.CurrentPath).Distinct().Count());
        // The batched multi-file count (drives the Library "N files" badge) sees both files on one movie.
        Assert.Equal(2, uow.MediaFiles.CountFilesByMovie()[movie.Id]);
    }

    [Fact]
    public async Task RootOrderDoesNotDecideMovieIdentityOrMetadataOwner()
    {
        // Importing the two copies in the reverse order still yields one matched movie with both files;
        // whichever file is processed first cannot "own" the metadata to the exclusion of the other.
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var second = MovieFile("Z_second_root.mkv", "Movie", 2020);
        var first = MovieFile("A_first_root.mkv", "Movie", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(second), new ImportSelection(first)]);

        Assert.Equal(2, result.RegisteredCount);
        using var uow = _factory.Begin();
        var movie = Assert.Single(uow.Movies.ListAll());
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Equal(2, uow.MediaFiles.GetByMovieId(movie.Id).Count);
    }

    [Fact]
    public async Task DifferingExtensionsForSameMovieRemainDistinctMediaFiles()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var mkv = MovieFile("Movie.mkv", "Movie", 2020, ".mkv");
        var mp4 = MovieFile("Movie.mp4", "Movie", 2020, ".mp4");

        await service.ImportSelectedAsync([new ImportSelection(mkv), new ImportSelection(mp4)]);

        using var uow = _factory.Begin();
        var movie = Assert.Single(uow.Movies.ListAll());
        var files = uow.MediaFiles.GetByMovieId(movie.Id);
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.CurrentPath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.CurrentPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NoConfidentMatch_DoesNotMergeSeparateFiles()
    {
        // With no usable TMDB result, two same-titled files stay two unmatched movies — a weak/absent match
        // must never collapse distinct files into one identity.
        var provider = new CountingProvider { MovieCandidates = [] };
        var service = CreateService(provider);
        var first = MovieFile("One.mkv", "Movie", 2020);
        var second = MovieFile("Two.mkv", "Movie", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(first), new ImportSelection(second)]);

        Assert.All(result.Items, item => Assert.NotEqual(ImportItemStatus.Matched, item.Status));
        using var uow = _factory.Begin();
        var movies = uow.Movies.ListAll();
        Assert.Equal(2, movies.Count);                 // not merged
        Assert.All(movies, m => Assert.Null(m.ExternalId)); // neither claimed an identity
    }

    [Fact]
    public async Task AssociatingASecondFilePreservesExistingWatchAndListState()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);

        // First import creates and matches the movie.
        await service.ImportSelectedAsync([new ImportSelection(MovieFile("First.mkv", "Movie", 2020))]);

        var connection = "Data Source=" + Path.Combine(_root, "catalog.db");
        int movieId;
        using (var uow = _factory.Begin())
        {
            movieId = uow.Movies.ListAll()[0].Id;
        }

        // Give the movie real user state: a custom-list membership and a watch event.
        var personal = new PersonalLibraryRepository(connection);
        var listId = personal.CreateCustomList("Favorites", DateTimeOffset.UtcNow);
        personal.AddMovieToCustomList(listId, movieId, DateTimeOffset.UtcNow);
        personal.AddWatchEvent(movieId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Second copy of the same movie in another folder associates into the existing movie.
        await service.ImportSelectedAsync([new ImportSelection(MovieFile("Second.mkv", "Movie", 2020))]);

        using (var uow = _factory.Begin())
        {
            var movie = Assert.Single(uow.Movies.ListAll());
            Assert.Equal(movieId, movie.Id);                       // same movie entity
            Assert.Equal(2, uow.MediaFiles.GetByMovieId(movie.Id).Count);
        }
        // User state on the surviving movie is untouched.
        Assert.Contains(listId, personal.GetCustomListIdsForMovie(movieId));
        Assert.Single(personal.GetWatchEvents(movieId));
    }

    [Fact]
    public void MixedDiscovery_ClassifiesMovieAndTvEpisodeTogether()
    {
        File.WriteAllBytes(Path.Combine(_root, "Film (2020).mkv"), [1]);
        File.WriteAllBytes(Path.Combine(_root, "Show.S01E02.mkv"), [2]);

        var items = new MediaDiscoveryService().Discover(_root, recursive: true);

        Assert.Contains(items, item => item.Classification == DiscoveryClassification.MovieCandidate);
        Assert.Contains(items, item => item.Classification == DiscoveryClassification.TvEpisodeCandidate);
    }

    [Fact]
    public async Task UnselectedItem_IsNotRegistered()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var selected = Discovered("Selected.mkv", MediaType.Movie, "Selected", 2020);
        var unselected = Discovered("Unselected.S01E01.mkv", MediaType.TvEpisode, "Unselected", null, 1, 1);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.Equal(1, result.RegisteredCount);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        Assert.Empty(uow.TvShows.ListAll());
        Assert.DoesNotContain(result.Items, item => item.Path == unselected.Path);
    }

    [Fact]
    public async Task CancelledSelection_LeavesCatalogUnchanged()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var selected = Discovered("Cancelled.mkv", MediaType.Movie, "Cancelled", 2020);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)], cancellation.Token);

        var outcome = Assert.Single(result.Items);
        Assert.Equal(ImportItemStatus.NotProcessedCancelled, outcome.Status);
        Assert.False(outcome.Registered);
        using var uow = _factory.Begin();
        Assert.Empty(uow.Movies.ListAll());
        Assert.Empty(uow.TvShows.ListAll());
    }

    [Fact]
    public async Task HighConfidenceMovie_IsAutoMatched_AndReady()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "1")] };
        var service = CreateService(provider);
        var selected = Discovered("Exact.mkv", MediaType.Movie, "Exact", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.True(result.Items[0].AutoMatched);
        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.Equal(1, result.MatchedCount);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Equal("1", movie.ExternalId);
    }

    [Fact]
    public async Task NoMovieCandidate_IsReportedAsNoMatchFound_AndRemainsLocallyRegistered()
    {
        var provider = new CountingProvider { MovieCandidates = [] };
        var service = CreateService(provider);
        var selected = Discovered("NoMatch.mkv", MediaType.Movie, "No Match", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.Equal(ImportItemStatus.NoMatchFound, result.Items[0].Status);
        Assert.Equal(MatchStatus.NoMatch, result.Items[0].MatchStatus);
        Assert.Equal(1, result.NoMatchFoundCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.NeedsMatch, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task PreviewMatched_IsPure_AndStagedImportDoesNotSearchAgain()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "1")] };
        var service = CreateService(provider);
        var item = Discovered("Preview.mkv", MediaType.Movie, "Exact", 2020);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.Equal(ImportItemStatus.Matched, preview.Status);
        Assert.NotNull(preview.MovieCandidate);
        Assert.Equal(1, provider.MovieSearchCount);
        using (var uow = _factory.Begin()) Assert.Empty(uow.Movies.ListAll());

        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);
        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.True(result.Items[0].AutoMatched);
        Assert.Equal(1, provider.MovieSearchCount);
    }

    [Fact]
    public async Task PreviewAmbiguousStrong_IsReadyToAdd_AndStagedImportAppliesWithReviewFlag()
    {
        // Two equally-strong (tied) candidates. The review preview no longer routes this to manual review:
        // it is surfaced as Matched (ready to add) while keeping MatchStatus = ReviewRequired, so the staged
        // import applies metadata and persists a Review Match flag. This is the real Add Media path (a staged
        // preview always exists), so it must accept the top (higher-popularity) candidate end to end.
        var ambiguousProvider = new CountingProvider
        {
            MovieCandidates =
            [
                new MovieCandidate("tmdb", "low", "Exact", null, 2020, null, null, null, popularity: 1),
                new MovieCandidate("tmdb", "high", "Exact", null, 2020, null, null, null, popularity: 9),
            ]
        };
        var ambiguousService = CreateService(ambiguousProvider);
        var ambiguousItem = Discovered("AmbiguousImport.mkv", MediaType.Movie, "Exact", 2020);

        var ambiguous = await ambiguousService.PreviewMetadataAsync(ambiguousItem);
        Assert.Equal(ImportItemStatus.Matched, ambiguous.Status);              // ready to add, not blocked
        Assert.Equal(MatchStatus.ReviewRequired, ambiguous.MatchStatus);       // still flagged for review
        Assert.Equal("high", ambiguous.MovieCandidate!.ExternalId);            // popularity tie-break in the preview

        var ambiguousResult = await ambiguousService.ImportSelectedAsync([
            new ImportSelection(ambiguousItem, ambiguous)]);
        Assert.True(ambiguousResult.Items[0].Registered);
        Assert.Equal(ImportItemStatus.Matched, ambiguousResult.Items[0].Status);
        Assert.Null(ambiguousResult.Items[0].MetadataFailureKind);
        Assert.Equal(0, ambiguousResult.MetadataErrorsCount);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);              // metadata applied
        Assert.Equal("high", movie.ExternalId);                               // higher-popularity of the tie
        Assert.True(movie.ReviewMatch);                                       // persisted Review Match
    }

    [Fact]
    public async Task PreviewNoCandidate_IsNoMatchFound_AndStagedImportStaysNeedsMatch()
    {
        var noMatchProvider = new CountingProvider { MovieCandidates = [] };
        var noMatchService = CreateService(noMatchProvider);
        var noMatchItem = Discovered("NoMatchPreview.mkv", MediaType.Movie, "Nothing", 2020);

        var noMatch = await noMatchService.PreviewMetadataAsync(noMatchItem);
        Assert.Equal(ImportItemStatus.NoMatchFound, noMatch.Status);
        Assert.Equal(MatchStatus.NoMatch, noMatch.MatchStatus);

        var noMatchResult = await noMatchService.ImportSelectedAsync([
            new ImportSelection(noMatchItem, noMatch)]);
        Assert.True(noMatchResult.Items[0].Registered);
        Assert.Equal(ImportItemStatus.NoMatchFound, noMatchResult.Items[0].Status);
        Assert.Equal(0, noMatchResult.MetadataErrorsCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.NeedsMatch, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task PreviewExactTitleNoYear_IsReadyToAdd_AtExactlySeventyFivePercent()
    {
        // The "Prison Break" case: an exact title with no parsed year scores exactly 0.75 (title 1.0 * 0.75,
        // no year bonus). That is below the 0.80 clear-winner gate, so it is ReviewRequired — but at exactly
        // the 0.75 bulk-accept threshold, so the review preview must surface it as ready to add, not NeedsMatch.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "pb", "Prison Break", null, 2005, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Prison.Break.mkv", MediaType.Movie, "Prison Break", null);

        var preview = await service.PreviewMetadataAsync(item);

        Assert.Equal(ImportItemStatus.Matched, preview.Status);         // ready to add at exactly 75%
        Assert.Equal(MatchStatus.ReviewRequired, preview.MatchStatus);  // still a review-band acceptance
        Assert.Equal("pb", preview.MovieCandidate!.ExternalId);
    }

    [Fact]
    public async Task StagedExactSeventyFive_AppliesMetadataDuringAddWithReviewFlag()
    {
        // The core regression: a 0.75 staged preview reaches the metadata-application phase and applies the
        // candidate (previously it was gated out and left NeedsMatch), persisting a Review Match flag.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "pb", "Prison Break", null, 2005, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Prison.Break.mkv", MediaType.Movie, "Prison Break", null);

        var preview = await service.PreviewMetadataAsync(item);
        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.True(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);   // metadata was applied, not skipped
        Assert.Equal("pb", movie.ExternalId);
        Assert.True(movie.ReviewMatch);                             // 0.75-0.80 acceptance is flagged
    }

    [Fact]
    public async Task PreviewWeakCandidate_IsNotReadyToAdd_RegardlessOfPopularity()
    {
        // A weakly-matching title stays below the 0.75 threshold; the review preview must not surface it as
        // ready to add even when the candidate is hugely popular. Popularity never makes a weak match ready.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "pop", "Matrix Reloaded", null, 2020, null, null, null, popularity: 999)]
        };
        var service = CreateService(provider);
        var item = Discovered("Matrix.mkv", MediaType.Movie, "Matrix", 2020);

        var preview = await service.PreviewMetadataAsync(item);

        Assert.NotEqual(ImportItemStatus.Matched, preview.Status);      // not offered as ready
        Assert.NotEqual(ImportItemStatus.ReadyToAdd, preview.Status);
    }

    [Fact]
    public async Task PreviewServiceFailure_IsError_NotNeedsMatch_AndRetryIsSingleItem()
    {
        var provider = new CountingProvider { MovieSearchFailureKind = MetadataFailureKind.Authentication };
        var service = CreateService(provider);
        var first = Discovered("FirstRetry.mkv", MediaType.Movie, "First", 2020);
        var second = Discovered("SecondRetry.mkv", MediaType.Movie, "Second", 2020);

        var firstResult = await service.PreviewMetadataAsync(first);
        Assert.Equal(ImportItemStatus.MetadataError, firstResult.Status);
        Assert.NotEqual(ImportItemStatus.NeedsMatch, firstResult.Status);
        var countAfterFirst = provider.MovieSearchCount;
        var retry = await service.RetryMetadataAsync(first);
        Assert.Equal(ImportItemStatus.MetadataError, retry.Status);
        Assert.Equal(countAfterFirst + 1, provider.MovieSearchCount);
        var beforeSecond = provider.MovieSearchCount;
        var secondResult = await service.PreviewMetadataAsync(second);
        Assert.Equal(ImportItemStatus.MetadataError, secondResult.Status);
        Assert.Equal(beforeSecond + 1, provider.MovieSearchCount);
        using var uow = _factory.Begin();
        Assert.Empty(uow.Movies.ListAll());
    }

    [Fact]
    public async Task StagedNoMatchAndServiceError_PreserveRegistrationAndPersistTruthfulState()
    {
        var noMatchProvider = new CountingProvider { MovieCandidates = [] };
        var noMatchService = CreateService(noMatchProvider);
        var noMatchItem = Discovered("StagedNoMatch.mkv", MediaType.Movie, "No Match", 2020);
        var noMatchPreview = await noMatchService.PreviewMetadataAsync(noMatchItem);
        var noMatchResult = await noMatchService.ImportSelectedAsync([
            new ImportSelection(noMatchItem, noMatchPreview)]);

        Assert.True(noMatchResult.Items[0].Registered);
        Assert.Equal(ImportItemStatus.NoMatchFound, noMatchResult.Items[0].Status);
        Assert.Null(noMatchResult.Items[0].MetadataFailureKind);
        Assert.Equal(0, noMatchResult.MetadataErrorsCount);
        Assert.Equal(1, noMatchProvider.MovieSearchCount);

        var errorProvider = new CountingProvider { MovieSearchFailureKind = MetadataFailureKind.Network };
        var errorService = CreateService(errorProvider);
        var errorItem = Discovered("StagedError.mkv", MediaType.Movie, "Network Error", 2020);
        var errorPreview = await errorService.PreviewMetadataAsync(errorItem);
        var errorResult = await errorService.ImportSelectedAsync([
            new ImportSelection(errorItem, errorPreview)]);

        Assert.True(errorResult.Items[0].Registered);
        Assert.Equal(ImportItemStatus.MetadataError, errorResult.Items[0].Status);
        Assert.Equal(MetadataFailureKind.Network, errorResult.Items[0].MetadataFailureKind);
        Assert.Equal(1, errorProvider.MovieSearchCount);
        using var uow = _factory.Begin();
        Assert.Equal(2, uow.Movies.ListAll().Count);
        Assert.Contains(uow.Movies.ListAll(), movie => movie.MetadataStatus == MetadataStatus.NeedsMatch);
        Assert.Contains(uow.Movies.ListAll(), movie => movie.MetadataStatus == MetadataStatus.Failed);
    }

    [Fact]
    public async Task StagedManualMatch_IsPersistedButNotCountedAsAutomatic()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Manual", "manual-1")] };
        var service = CreateService(provider);
        var item = Discovered("ManualStage.mkv", MediaType.Movie, "Manual", 2020);
        var preview = new ImportMetadataPreview(
            ImportItemStatus.Matched,
            MatchStatus.Matched,
            MovieCandidate: Candidate("Manual", "manual-1"),
            IsManual: true);

        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.False(result.Items[0].AutoMatched);
        Assert.Equal(0, provider.MovieSearchCount);
        using var uow = _factory.Begin();
        Assert.Equal("manual-1", uow.Movies.ListAll().Single().ExternalId);
    }

    [Fact]
    public async Task PreviewTvMatch_IsPure_AndStagedEpisodeUsesThePreview()
    {
        var provider = new CountingProvider
        {
            TvCandidates = [new TvCandidate("tmdb", "show-preview", "Preview Show", null, 2020, "Overview", 8.2, "/poster.jpg", null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Preview.Show.S01E01.mkv", MediaType.TvEpisode, "Preview Show", 2020, 1, 1);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.Equal(ImportItemStatus.Matched, preview.Status);
        Assert.NotNull(preview.TvCandidate);
        Assert.Equal(1, provider.TvSearchCount);
        using (var uow = _factory.Begin()) Assert.Empty(uow.TvShows.ListAll());

        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);
        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.Equal(1, provider.TvSearchCount);
        using var finalUow = _factory.Begin();
        Assert.Equal("show-preview", finalUow.TvShows.ListAll().Single().ExternalId);
    }

    [Fact]
    public async Task GroupedTvRows_ManualPreviewWinsWithoutStaleStatusConflict()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var first = Discovered("Mixed.Show.S01E01.mkv", MediaType.TvEpisode, "Mixed Show", 2020, 1, 1);
        var second = Discovered("Mixed.Show.S01E02.mkv", MediaType.TvEpisode, "Mixed Show", 2020, 1, 2);
        var ambiguous = new ImportMetadataPreview(
            ImportItemStatus.NeedsMatch,
            MatchStatus.ReviewRequired,
            Failure: "Select a match manually.");
        var manualCandidate = new TvCandidate(
            "tmdb", "mixed-manual", "Mixed Show", null, 2020, "Overview", 8.1, "/mixed.jpg", null);
        var manual = new ImportMetadataPreview(
            ImportItemStatus.Matched,
            MatchStatus.Matched,
            TvCandidate: manualCandidate,
            IsManual: true);

        var result = await service.ImportSelectedAsync([
            new ImportSelection(first, ambiguous),
            new ImportSelection(second, manual)]);

        Assert.Equal(ImportItemStatus.NeedsMatch, result.Items[0].Status);
        Assert.Equal(ImportItemStatus.Matched, result.Items[1].Status);
        Assert.Equal(0, provider.TvSearchCount);
        Assert.Equal(1, provider.TvMatchCount);
        using var uow = _factory.Begin();
        Assert.Equal("mixed-manual", uow.TvShows.ListAll().Single().ExternalId);
        Assert.Equal(MetadataStatus.Ready, uow.TvShows.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task GroupedTvRows_ConflictingManualPreviewsRefuseRegistrationBeforeWrites()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var first = Discovered("Conflict.Show.S01E01.mkv", MediaType.TvEpisode, "Conflict Show", 2020, 1, 1);
        var second = Discovered("Conflict.Show.S01E02.mkv", MediaType.TvEpisode, "Conflict Show", 2020, 1, 2);
        var firstManual = new ImportMetadataPreview(
            ImportItemStatus.Matched,
            MatchStatus.Matched,
            TvCandidate: new TvCandidate("tmdb", "show-a", "Conflict A", null, 2020, null, null, null, null),
            IsManual: true);
        var secondManual = new ImportMetadataPreview(
            ImportItemStatus.Matched,
            MatchStatus.Matched,
            TvCandidate: new TvCandidate("tmdb", "show-b", "Conflict B", null, 2020, null, null, null, null),
            IsManual: true);

        var result = await service.ImportSelectedAsync([
            new ImportSelection(first, firstManual),
            new ImportSelection(second, secondManual)]);

        Assert.All(result.Items, item =>
        {
            Assert.False(item.Registered);
            Assert.Equal(ImportItemStatus.RegistrationFailed, item.Status);
        });
        Assert.Equal(2, result.RegistrationFailuresCount);
        Assert.Equal(0, provider.TvMatchCount);
        using var uow = _factory.Begin();
        Assert.Empty(uow.TvShows.ListAll());
    }

    [Fact]
    public async Task StagedTvMetadataUnavailable_PersistsFailureWithoutBlockingRegistration()
    {
        var provider = new CountingProvider { IsConfigured = false };
        var service = CreateService(provider);
        var item = Discovered("Unconfigured.Show.S01E01.mkv", MediaType.TvEpisode, "Unconfigured Show", 2020, 1, 1);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.Equal(ImportItemStatus.MetadataUnavailable, preview.Status);
        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);

        Assert.True(result.Items[0].Registered);
        Assert.Equal(ImportItemStatus.MetadataUnavailable, result.Items[0].Status);
        Assert.Equal(1, result.MetadataUnavailableCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Failed, uow.TvShows.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task InvalidCredential_IsReportedAsMetadataError_AndDoesNotBecomeNeedsMatch()
    {
        var provider = new CountingProvider { MovieSearchFailureKind = MetadataFailureKind.Authentication };
        var service = CreateService(provider);
        var selected = Discovered("InvalidToken.mkv", MediaType.Movie, "Invalid Token", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.Equal(ImportItemStatus.MetadataError, result.Items[0].Status);
        Assert.Equal(MetadataFailureKind.Authentication, result.Items[0].MetadataFailureKind);
        Assert.Equal(0, result.NeedsMatchCount);
        Assert.Equal(1, result.MetadataErrorsCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Failed, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task ResultCountsSeparateAddedExistingMatchedPendingAndMetadataErrors()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "1")] };
        var service = CreateService(provider);
        var selected = Discovered("Counts.mkv", MediaType.Movie, "Exact", 2020);

        var first = await service.ImportSelectedAsync([new ImportSelection(selected)]);
        var second = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.Equal(1, first.AddedCount);
        Assert.Equal(0, first.ExistingCount);
        Assert.Equal(1, first.MatchedCount);
        Assert.Equal(0, first.NeedsMatchCount);
        Assert.Equal(1, second.ExistingCount);
        Assert.Equal(0, second.AddedCount);
        Assert.Equal(1, second.AlreadyInLibraryCount);
        Assert.Equal(0, second.MatchedCount);
    }

    [Fact]
    public async Task ExistingMoviePath_IsDetectedBeforeMetadataAndCannotDuplicate()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "exact-1")] };
        var service = CreateService(provider);
        var item = Discovered("Existing.mkv", MediaType.Movie, "Exact", 2020);

        await service.ImportSelectedAsync([new ImportSelection(item)]);
        var callsAfterFirstImport = provider.MovieSearchCount;
        var preview = await service.PreviewMetadataAsync(item);
        var second = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.Equal(ImportItemStatus.AlreadyInLibrary, preview.Status);
        Assert.Equal(callsAfterFirstImport, provider.MovieSearchCount);
        Assert.Single(second.Items);
        Assert.True(second.Items[0].Registered);
        Assert.True(second.Items[0].AlreadyRegistered);
        Assert.Equal(ImportItemStatus.AlreadyInLibrary, second.Items[0].Status);
        Assert.Equal(1, second.ExistingCount);
        Assert.Equal(1, second.AlreadyInLibraryCount);
        Assert.Equal(0, second.AddedCount);
        Assert.Equal(0, second.MatchedCount);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        Assert.Single(uow.MediaFiles.ListAll());
    }

    [Fact]
    public async Task RegisteredPathIdentity_IsCanonicalAndDoesNotUseMovieIdentity()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Same Movie", "same-movie")] };
        var service = CreateService(provider);
        var first = Discovered("Identity-A.mkv", MediaType.Movie, "Same Movie", 2020);
        var second = Discovered("Identity-B.mkv", MediaType.Movie, "Same Movie", 2020);

        var firstResult = await service.ImportSelectedAsync([new ImportSelection(first)]);
        Assert.Equal(ImportItemStatus.Matched, firstResult.Items[0].Status);

        Assert.True(service.IsMediaFileRegistered(first.Path));
        Assert.True(service.IsMediaFileRegistered(first.Path.ToLowerInvariant()));
        Assert.False(service.IsMediaFileRegistered(second.Path));

        var providerCallsBeforeSecond = provider.MovieSearchCount;
        var secondPreview = await service.PreviewMetadataAsync(second);
        Assert.Equal(ImportItemStatus.Matched, secondPreview.Status);
        Assert.Equal(providerCallsBeforeSecond + 1, provider.MovieSearchCount);
        Assert.False(service.IsMediaFileRegistered(second.Path));
    }

    [Fact]
    public async Task MixedExistingAndNewSelection_OnlyNewItemCallsMetadataProvider()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("New", "new-1")] };
        var service = CreateService(provider);
        var existing = Discovered("Mixed-existing.mkv", MediaType.Movie, "Existing", 2020);
        var added = Discovered("Mixed-new.mkv", MediaType.Movie, "New", 2021);

        service.RegisterMovieImport(new ConfirmMovieImportCommand(
            existing.Path,
            existing.FileSize!.Value,
            existing.ParsedMedia!,
            DateTimeOffset.UtcNow));

        var existingPreview = await service.PreviewMetadataAsync(existing);
        Assert.Equal(ImportItemStatus.AlreadyInLibrary, existingPreview.Status);
        Assert.Equal(0, provider.MovieSearchCount);
        var newPreview = await service.PreviewMetadataAsync(added);
        Assert.Equal(ImportItemStatus.Matched, newPreview.Status);
        Assert.Equal(1, provider.MovieSearchCount);

        var result = await service.ImportSelectedAsync([
            new ImportSelection(existing, existingPreview),
            new ImportSelection(added, newPreview)]);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(ImportItemStatus.AlreadyInLibrary, result.Items[0].Status);
        Assert.Equal(ImportItemStatus.Matched, result.Items[1].Status);
        Assert.True(result.Items[0].AlreadyRegistered);
        Assert.False(result.Items[1].AlreadyRegistered);
        Assert.Equal(1, provider.MovieSearchCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.AlreadyInLibraryCount);
        Assert.Equal(1, result.AddedCount);
        using var uow = _factory.Begin();
        Assert.Equal(2, uow.MediaFiles.ListAll().Count);
    }

    [Fact]
    public async Task ExistingTvEpisode_IsReportedAlreadyInLibraryWithoutSearchOrDuplicate()
    {
        var provider = new CountingProvider
        {
            TvCandidates = [new TvCandidate("tmdb", "show-1", "Show", null, 2020, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Existing.Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1);

        await service.ImportSelectedAsync([new ImportSelection(item)]);
        var searchesAfterFirstImport = provider.TvSearchCount;
        var matchesAfterFirstImport = provider.TvMatchCount;
        var second = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.Equal(searchesAfterFirstImport, provider.TvSearchCount);
        Assert.Equal(matchesAfterFirstImport, provider.TvMatchCount);
        Assert.Equal(ImportItemStatus.AlreadyInLibrary, second.Items[0].Status);
        Assert.True(second.Items[0].AlreadyRegistered);
        using var uow = _factory.Begin();
        var show = Assert.Single(uow.TvShows.ListAll());
        Assert.Single(uow.TvEpisodes.ListByShow(show.Id));
        Assert.Single(uow.MediaFiles.ListAll());
    }

    [Fact]
    public async Task AllSelectedItems_RegisterBeforeSlowMetadataSearch()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<MovieCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CountingProvider { MovieSearchGate = gate };
        var service = CreateService(provider);
        var first = Discovered("First.mkv", MediaType.Movie, "First", 2020);
        var second = Discovered("Second.mkv", MediaType.Movie, "Second", 2021);

        var importTask = service.ImportSelectedAsync([
            new ImportSelection(first),
            new ImportSelection(second)]);
        await provider.MovieSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using (var uow = _factory.Begin())
        {
            Assert.Equal(2, uow.Movies.ListAll().Count);
        }

        gate.SetResult([]);
        var result = await importTask;
        Assert.Equal(2, result.RegisteredCount);
    }

    [Fact]
    public async Task ManualMatchDuringAutomaticSearch_IsNotOverwritten()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<MovieCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var automaticCandidate = Candidate("Automatic", "automatic-1");
        var provider = new CountingProvider
        {
            MovieCandidates = [automaticCandidate],
            MovieSearchGate = gate,
        };
        var matching = new MetadataMatchingService(_factory, provider);
        var service = new ImportService(_factory, provider, matching: matching);
        var selected = Discovered("Race.mkv", MediaType.Movie, "Race", 2020);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        await provider.MovieSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int movieId;
        using (var uow = _factory.Begin())
        {
            movieId = uow.Movies.ListAll().Single().Id;
        }

        await matching.MatchMovieAsync(movieId, Candidate("Manual", "manual-1"));
        gate.SetResult([automaticCandidate]);

        var result = await importTask;

        Assert.False(result.Items[0].AutoMatched);
        using var finalUow = _factory.Begin();
        var movie = finalUow.Movies.GetById(movieId)!;
        Assert.Equal("manual-1", movie.ExternalId);
    }

    [Fact]
    public async Task ManualTvMatchDuringAutomaticNoMatch_ReconcilesResult()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<TvCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CountingProvider { TvSearchGate = gate };
        var matching = new MetadataMatchingService(_factory, provider);
        var service = new ImportService(_factory, provider, matching: matching);
        var selected = Discovered("Race.Show.S01E01.mkv", MediaType.TvEpisode, "Race Show", 2020, 1, 1);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        await provider.TvSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        await matching.MatchTvShowAsync(showId, new TvCandidate("tmdb", "manual-tv-1", "Race Show", null, 2020, null, null, null, null));
        gate.SetResult([]);

        var result = await importTask;

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.False(result.Items[0].AutoMatched);
        using var finalUow = _factory.Begin();
        Assert.Equal("manual-tv-1", finalUow.TvShows.ListAll().Single().ExternalId);
    }

    [Fact]
    public async Task ManualTvMatchDuringAutomaticMatch_ReconcilesStaleResult()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<TvCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var automaticCandidate = new TvCandidate("tmdb", "automatic-tv-1", "Race Match", null, 2020, null, null, null, null);
        var provider = new CountingProvider { TvSearchGate = gate };
        var matching = new MetadataMatchingService(_factory, provider);
        var service = new ImportService(_factory, provider, matching: matching);
        var selected = Discovered("Race.Match.S01E01.mkv", MediaType.TvEpisode, "Race Match", 2020, 1, 1);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        await provider.TvSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        await matching.MatchTvShowAsync(showId, new TvCandidate("tmdb", "manual-tv-2", "Race Match", null, 2020, null, null, null, null));
        gate.SetResult([automaticCandidate]);

        var result = await importTask;

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        Assert.Equal(MatchStatus.Matched, result.Items[0].MatchStatus);
        Assert.False(result.Items[0].AutoMatched);
        using var finalUow = _factory.Begin();
        Assert.Equal("manual-tv-2", finalUow.TvShows.ListAll().Single().ExternalId);
    }

    [Fact]
    public async Task ProviderFailureAfterRegistration_PreservesLocalMovie()
    {
        var provider = new CountingProvider { ThrowOnMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Offline.mkv", MediaType.Movie, "Offline", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.False(result.Items[0].AutoMatched);
        Assert.Equal(ImportItemStatus.MetadataError, result.Items[0].Status);
        Assert.Equal(MetadataFailureKind.Api, result.Items[0].MetadataFailureKind);
        Assert.Equal(1, result.MetadataErrorsCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Failed, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task UnconfiguredProvider_SkipsMetadataAndPreservesLocalMovie()
    {
        var provider = new CountingProvider { IsConfigured = false, ThrowOnMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Unconfigured.mkv", MediaType.Movie, "Unconfigured", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.False(result.Items[0].AutoMatched);
        Assert.Equal(ImportItemStatus.MetadataUnavailable, result.Items[0].Status);
        Assert.Equal(MetadataFailureKind.NotConfigured, result.Items[0].MetadataFailureKind);
        Assert.Equal(1, result.MetadataUnavailableCount);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Failed, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task PosterAcquisition_DoesNotBlockLocalRegistration()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Poster", "poster-1")] };
        var poster = new BlockingPosterService();
        var service = new ImportService(
            _factory,
            provider,
            matching: new MetadataMatchingService(_factory, provider, poster));
        var selected = Discovered("Poster.mkv", MediaType.Movie, "Poster", 2020);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        var completed = await Task.WhenAny(importTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(importTask, completed);
        var result = await importTask;
        Assert.True(result.Items[0].Registered);
        Assert.True(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task CanceledProviderSearch_PreservesLocalMovie()
    {
        var provider = new CountingProvider { CancelMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Canceled.mkv", MediaType.Movie, "Canceled", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.Equal(ImportItemStatus.MetadataCancelled, result.Items[0].Status);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task DuplicateTvEpisodes_SearchAndMatchShowOnlyOnce()
    {
        var provider = new CountingProvider { TvCandidates = [new TvCandidate("tmdb", "show-1", "Show", null, 2020, null, null, null)] };
        var service = CreateService(provider);
        var first = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1);
        var second = Discovered("Show.S01E02.mkv", MediaType.TvEpisode, "Show", 2020, 1, 2);

        var result = await service.ImportSelectedAsync([new ImportSelection(first), new ImportSelection(second)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.Equal(1, provider.TvSearchCount);
        Assert.Equal(1, provider.TvMatchCount);
        Assert.All(result.Items, item => Assert.True(item.AutoMatched));
    }

    [Fact]
    public async Task NewEpisodeOnMatchedShow_RefreshesStoredIdentityWithoutRematch()
    {
        var provider = new CountingProvider
        {
            TvCandidates = [new TvCandidate("tmdb", "show-1", "Show", null, 2020, null, null, null)],
            TvMetadata = new TvShowMetadata(
                "tmdb",
                "show-1",
                "Show",
                null,
                2020,
                "Overview",
                ImmutableArray<string>.Empty,
                null,
                "/show.jpg",
                null,
                [new TvSeasonMetadata(
                    1,
                    "Season 1",
                    null,
                    null,
                    null,
                    [new TvEpisodeMetadata(2, "Episode Two", null, 42, null, null, null, "ep-2")])])
        };
        var service = CreateService(provider);

        await service.ImportSelectedAsync([
            new ImportSelection(Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1))]);
        var secondResult = await service.ImportSelectedAsync([
            new ImportSelection(Discovered("Show.S01E02.mkv", MediaType.TvEpisode, "Show", 2020, 1, 2))]);

        Assert.True(secondResult.Items[0].AutoMatched);
        Assert.Equal(1, provider.TvSearchCount);
        Assert.Equal(2, provider.TvMatchCount);
        using var uow = _factory.Begin();
        var show = uow.TvShows.ListAll().Single();
        Assert.Equal("show-1", show.ExternalId);
        var episode = uow.TvEpisodes.ListByShow(show.Id).Single(item => item.Number == 2);
        Assert.Equal("Episode Two", episode.Title);
        Assert.Equal("ep-2", episode.ExternalId);
    }

    // --- TV batch matching & confidence -------------------------------------------------------------

    private static TvCandidate TvShowCandidate(string title, string id, int? year = 2020) =>
        new("tmdb", id, title, null, year, "overview", 8.0, "/show.jpg", "/bg.jpg");

    [Fact]
    public async Task TvShow_ResolvesOnce_WhenOnlySomeEpisodesOfTheShowAreSelected()
    {
        // Three episodes of one show, only two selected. The show must still resolve to one identity with
        // metadata; the show lookup/match happens once, not once per episode; the unselected episode is
        // simply not imported and never creates a second show.
        var provider = new CountingProvider { TvCandidates = [TvShowCandidate("Show", "show-1")] };
        var service = CreateService(provider);
        var e1 = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1);
        var e2 = Discovered("Show.S01E02.mkv", MediaType.TvEpisode, "Show", 2020, 1, 2);
        // e3 is discovered but deliberately NOT selected for import.

        var result = await service.ImportSelectedAsync([new ImportSelection(e1), new ImportSelection(e2)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.Equal(1, provider.TvSearchCount);   // show looked up once, reused across episodes
        Assert.Equal(1, provider.TvMatchCount);    // show metadata fetched once

        using var uow = _factory.Begin();
        var show = Assert.Single(uow.TvShows.ListAll());
        Assert.Equal(MetadataStatus.Ready, show.MetadataStatus);      // show metadata resolved
        Assert.Equal("tmdb", show.Data.Provider);
        Assert.Equal("show-1", show.Data.ExternalId);
        Assert.Equal(2, uow.TvEpisodes.ListByShow(show.Id).Count);    // only the two selected episodes
    }

    [Fact]
    public async Task BulkAdd_AcceptsTopCandidateAtSeventyFivePercentConfidence()
    {
        // An exact-title candidate with no year to corroborate scores exactly 0.75 — below the 0.80
        // clear-winner bar but at the bulk-accept threshold — so a user-initiated Add accepts it instead
        // of forcing a manual Review Match.
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "movie-75")] };
        var service = CreateService(provider);
        var item = Discovered("Exact.mkv", MediaType.Movie, "Exact", null); // no year -> confidence 0.75

        var result = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = Assert.Single(uow.Movies.ListAll());
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Equal("movie-75", movie.ExternalId);
    }

    [Fact]
    public async Task BulkAdd_LeavesBelowThresholdCandidateAsNeedsReview()
    {
        // A weak, inexact title match stays below 0.75; a user-initiated Add must not guess it — it stays
        // unresolved for review and never gets a provider identity attached.
        var provider = new CountingProvider { MovieCandidates = [Candidate("Matrix Reloaded", "movie-low")] };
        var service = CreateService(provider);
        var item = Discovered("Matrix.mkv", MediaType.Movie, "Matrix", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.NotEqual(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = Assert.Single(uow.Movies.ListAll());
        Assert.NotEqual(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Null(movie.ExternalId); // no metadata attached below threshold
    }

    [Fact]
    public void Scorer_ExactTitleNoYear_ScoresExactlySeventyFivePercentAndNeedsReview()
    {
        // Locks the boundary the bulk-accept rule depends on: an exact-title match with no year to
        // corroborate is exactly 0.75 confidence and is ReviewRequired by the unattended scorer (< 0.80).
        var decision = MetadataScorer.Score("Exact", null, [Candidate("Exact", "id")]);
        Assert.Equal(0.75, decision.Confidence);
        Assert.Equal(MatchStatus.ReviewRequired, decision.Status);
    }

    [Fact]
    public async Task LocalShow_GroupsManyEpisodesUnderOneUnmatchedShow_WhenTmdbFindsNothing()
    {
        // Many episodes of the same locally-titled (Arabic) show that TMDB cannot resolve must group under
        // ONE local unmatched show by parsed title — never one incomplete show per episode.
        var provider = new CountingProvider { TvCandidates = [] }; // TMDB returns no show
        var service = CreateService(provider);
        var selections = Enumerable.Range(1, 30)
            .Select(n => new ImportSelection(Discovered(
                $"Moslsl.S01E{n:D2}.mkv", MediaType.TvEpisode, "مسلسل عربي", 2020, 1, n)))
            .ToArray();

        var result = await service.ImportSelectedAsync(selections);

        Assert.Equal(30, result.RegisteredCount);
        using var uow = _factory.Begin();
        var show = Assert.Single(uow.TvShows.ListAll());          // one local show, not 30
        Assert.Equal("مسلسل عربي", show.Title);                    // parsed local title
        Assert.NotEqual(MetadataStatus.Ready, show.MetadataStatus); // unmatched / needs metadata
        Assert.Equal(30, uow.TvEpisodes.ListByShow(show.Id).Count); // all episodes attached
    }

    [Fact]
    public async Task LocalShow_LaterManualMatch_EnrichesTheSameShowAndKeepsEpisodes()
    {
        var provider = new CountingProvider { TvCandidates = [] };
        var service = CreateService(provider);
        await service.ImportSelectedAsync(Enumerable.Range(1, 3)
            .Select(n => new ImportSelection(Discovered(
                $"MoslslB.S01E{n:D2}.mkv", MediaType.TvEpisode, "مسلسل ثاني", 2020, 1, n)))
            .ToArray());

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll()[0].Id;
        }

        var matching = new MetadataMatchingService(_factory, provider);
        await matching.MatchTvShowAsync(showId, TvShowCandidate("مسلسل ثاني", "ar-2"));

        using var after = _factory.Begin();
        var show = Assert.Single(after.TvShows.ListAll());        // still ONE show, enriched in place
        Assert.Equal(showId, show.Id);
        Assert.Equal(MetadataStatus.Ready, show.MetadataStatus);
        Assert.Equal("ar-2", show.Data.ExternalId);
        Assert.Equal(3, after.TvEpisodes.ListByShow(show.Id).Count);
    }

    [Fact]
    public async Task ManualRematchOfOneShow_DoesNotModifyOtherShows()
    {
        int aId, bId;
        using (var uow = _factory.Begin())
        {
            aId = uow.TvShows.Create(new TvShowCatalogData("tmdb", "X", "Alpha", year: 2020, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow).Id;
            bId = uow.TvShows.Create(new TvShowCatalogData("tmdb", "Y", "Beta", year: 2019, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow).Id;
            uow.Commit();
        }

        var provider = new CountingProvider();
        var matching = new MetadataMatchingService(_factory, provider);
        // Rematch A to a brand-new identity Z; B must be entirely untouched.
        await matching.MatchTvShowAsync(aId, TvShowCandidate("Alpha", "Z"), fetchEpisodes: false);

        using var after = _factory.Begin();
        var a = after.TvShows.GetById(aId)!;
        var b = after.TvShows.GetById(bId)!;
        Assert.Equal("Z", a.Data.ExternalId);                     // A updated
        Assert.Equal("Y", b.Data.ExternalId);                     // B unchanged
        Assert.Equal("Beta", b.Title);
        Assert.Equal(MetadataStatus.Ready, b.MetadataStatus);
    }

    [Fact]
    public async Task ManualRematch_ToIdentityOwnedByAnotherShow_FailsAsConflictWithoutPartialWrite()
    {
        int aId, bId;
        using (var uow = _factory.Begin())
        {
            aId = uow.TvShows.Create(new TvShowCatalogData("tmdb", "AX", "Alpha", year: 2020, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow).Id;
            bId = uow.TvShows.Create(new TvShowCatalogData("tmdb", "BY", "Beta", year: 2019, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow).Id;
            uow.Commit();
        }

        var matching = new MetadataMatchingService(_factory, new CountingProvider());

        // Matching A onto B's provider identity collides; it must be refused before any write.
        var error = await Assert.ThrowsAsync<MetadataServiceException>(
            () => matching.MatchTvShowAsync(aId, TvShowCandidate("Alpha", "BY"), fetchEpisodes: false));
        Assert.Equal(MetadataFailureKind.IdentityConflict, error.Kind);

        using var after = _factory.Begin();
        Assert.Equal("AX", after.TvShows.GetById(aId)!.Data.ExternalId); // A unchanged (no partial write)
        Assert.Equal("BY", after.TvShows.GetById(bId)!.Data.ExternalId); // B unchanged
    }

    [Fact]
    public async Task AmbiguousMovie_TieBreak_AcceptsHigherPopularityWithReviewFlag()
    {
        // Two equally-strong (tied) candidates: a user-initiated Add no longer blocks — it accepts the
        // higher-popularity candidate and persists a Review Match flag so the item is usable but flagged.
        var provider = new CountingProvider
        {
            MovieCandidates =
            [
                new MovieCandidate("tmdb", "low", "Exact", null, 2020, null, null, null, popularity: 1),
                new MovieCandidate("tmdb", "high", "Exact", null, 2020, null, null, null, popularity: 9),
            ]
        };
        var service = CreateService(provider);
        var selected = Discovered("Exact.mkv", MediaType.Movie, "Exact", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Equal("high", movie.ExternalId);   // highest-popularity of the tie
        Assert.True(movie.ReviewMatch);            // persisted "review recommended"
    }

    [Fact]
    public async Task WeakMatchBelowThreshold_IsNotRescuedByPopularity()
    {
        // A hugely popular but weakly-matching candidate must NOT be accepted: popularity only breaks ties
        // between strong (>= 0.75) candidates; it never rescues a below-threshold match.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "pop", "Matrix Reloaded", null, 2020, null, null, null, popularity: 999)]
        };
        var service = CreateService(provider);
        var item = Discovered("Matrix.mkv", MediaType.Movie, "Matrix", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.NotEqual(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Null(movie.ExternalId);
        Assert.False(movie.ReviewMatch);
    }

    [Fact]
    public async Task ManualConfirm_ClearsReviewMatchFlag()
    {
        // A movie auto-picked from a tie carries ReviewMatch=true; a definitive manual match clears it.
        var provider = new CountingProvider
        {
            MovieCandidates =
            [
                new MovieCandidate("tmdb", "a", "Exact", null, 2020, null, null, null, popularity: 1),
                new MovieCandidate("tmdb", "b", "Exact", null, 2020, null, null, null, popularity: 2),
            ]
        };
        var service = CreateService(provider);
        await service.ImportSelectedAsync([new ImportSelection(Discovered("Exact.mkv", MediaType.Movie, "Exact", 2020))]);

        int movieId;
        using (var uow = _factory.Begin())
        {
            var m = uow.Movies.ListAll().Single();
            Assert.True(m.ReviewMatch);
            movieId = m.Id;
        }

        var matching = new MetadataMatchingService(_factory, provider);
        await matching.MatchMovieAsync(movieId, new MovieCandidate("tmdb", "b", "Exact", null, 2020, null, null, null), reviewMatch: false);

        using var after = _factory.Begin();
        Assert.False(after.Movies.GetById(movieId)!.ReviewMatch); // confirmed → flag cleared
    }

    [Fact]
    public async Task ManualMatch_ToIdentityOfExistingMovie_RefusesWithoutDeletingOfflineMovie()
    {
        // Movie A is matched to TMDB X. Movie B is an unmatched local copy. Manually matching B to X must
        // refuse the collision without deleting B or moving either movie's files.
        var provider = new CountingProvider();
        int aId, bId;
        using (var uow = _factory.Begin())
        {
            aId = uow.Movies.Create(new MovieCatalogData("tmdb", "X", "Canon", null, 2020, null, ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Ready), DateTimeOffset.UtcNow).Id;
            uow.MediaFiles.Add(new VerifiedMediaFileFacts(Path.Combine(_root, "A.mkv"), 100, ".mkv", null, null, null, DateTimeOffset.UtcNow), aId);
            bId = uow.Movies.Create(new MovieCatalogData(null, null, "Canon", null, 2020, null, ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending), DateTimeOffset.UtcNow).Id;
            uow.MediaFiles.Add(new VerifiedMediaFileFacts(Path.Combine(_root, "B.mkv"), 100, ".mkv", null, null, null, DateTimeOffset.UtcNow), bId);
            uow.Commit();
        }

        var matching = new MetadataMatchingService(_factory, provider);
        var error = await Assert.ThrowsAsync<MetadataServiceException>(() =>
            matching.MatchMovieAsync(bId, new MovieCandidate("tmdb", "X", "Canon", null, 2020, null, null, null)));
        Assert.Equal(MetadataFailureKind.IdentityConflict, error.Kind);
        using var after = _factory.Begin();
        Assert.Equal(2, after.Movies.ListAll().Count);
        Assert.Single(after.MediaFiles.GetByMovieId(aId));
        Assert.Single(after.MediaFiles.GetByMovieId(bId));
        Assert.Equal("X", after.Movies.GetById(aId)!.ExternalId);
        Assert.Null(after.Movies.GetById(bId)!.ExternalId);
    }

    [Fact]
    public async Task SoleResultBelowThreshold_WithExactYearAndContainment_IsAcceptedAsReviewMatch()
    {
        // "Dune Part One (2021)" and TMDB returns only "Dune (2021)" (~49%). One credible sole result whose
        // title is the leading base of the query and whose year matches is accepted with a Review Match.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "dune", "Dune", null, 2021, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Dune.Part.One.2021.mkv", MediaType.Movie, "Dune Part One", 2021);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.Equal(ImportItemStatus.Matched, preview.Status);            // offered as ready to add
        Assert.Equal(MatchStatus.ReviewRequired, preview.MatchStatus);     // flagged for review

        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);
        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);          // metadata applied
        Assert.Equal("dune", movie.ExternalId);
        Assert.True(movie.ReviewMatch);                                    // persisted Review Match
    }

    [Fact]
    public async Task SoleResultBelowThreshold_IsAcceptedEvenWithoutAStagedPreview()
    {
        // The same corroborated sole-result fallback also applies on a direct Add with no staged preview.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "dune", "Dune", null, 2021, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Dune.Part.One.2021.mkv", MediaType.Movie, "Dune Part One", 2021);

        var result = await service.ImportSelectedAsync([new ImportSelection(item)]);

        Assert.Equal(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal("dune", movie.ExternalId);
        Assert.True(movie.ReviewMatch);
    }

    [Fact]
    public async Task SoleResultWithConflictingYear_StaysNeedsReview()
    {
        // Same titles, but the sole result's year conflicts (1984 vs 2021): it must NOT be accepted.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "dune84", "Dune", null, 1984, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Dune.Part.One.2021.mkv", MediaType.Movie, "Dune Part One", 2021);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.NotEqual(ImportItemStatus.Matched, preview.Status);

        var result = await service.ImportSelectedAsync([new ImportSelection(item, preview)]);
        Assert.NotEqual(ImportItemStatus.Matched, result.Items[0].Status);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Null(movie.ExternalId);
        Assert.False(movie.ReviewMatch);
    }

    [Fact]
    public async Task SoleResultWithUnrelatedTitle_IsNotAccepted()
    {
        // A sole result whose title is not the leading base of the query is unrelated and stays for review.
        var provider = new CountingProvider
        {
            MovieCandidates = [new MovieCandidate("tmdb", "dune", "Dune", null, 2021, null, null, null)]
        };
        var service = CreateService(provider);
        var item = Discovered("Completely.Different.2021.mkv", MediaType.Movie, "Completely Different", 2021);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.NotEqual(ImportItemStatus.Matched, preview.Status);
    }

    [Fact]
    public async Task TwoWeakResults_AreNotAcceptedByTheSoleResultFallback()
    {
        // The fallback requires exactly one candidate (no competitor). Two below-threshold results stay
        // Needs Review even if one of them looks like a base-title containment.
        var provider = new CountingProvider
        {
            MovieCandidates =
            [
                new MovieCandidate("tmdb", "dune", "Dune", null, 2021, null, null, null),
                new MovieCandidate("tmdb", "other", "Dune Messiah", null, 2021, null, null, null),
            ]
        };
        var service = CreateService(provider);
        var item = Discovered("Dune.Part.One.2021.mkv", MediaType.Movie, "Dune Part One", 2021);

        var preview = await service.PreviewMetadataAsync(item);
        Assert.NotEqual(ImportItemStatus.Matched, preview.Status);
    }

    private ImportService CreateService(CountingProvider provider) =>
        new(_factory, provider, matching: new MetadataMatchingService(_factory, provider));

    private DiscoveredMedia Discovered(string fileName, MediaType type, string title, int? year,
        int? season = null, int? episode = null)
    {
        var path = Path.Combine(_root, fileName);
        return new DiscoveredMedia(path, 100, new ParsedMedia(fileName, type, title, year,
            "1080p", "web", "h264", ".mkv", season, episode),
            type == MediaType.Movie ? DiscoveryClassification.MovieCandidate : DiscoveryClassification.TvEpisodeCandidate, null);
    }

    private static MovieCandidate Candidate(string title, string id) =>
        new("tmdb", id, title, null, 2020, null, null, null);

    private sealed class CountingProvider : IMetadataProvider
    {
        public string ProviderName => "tmdb";
        public bool IsConfigured { get; init; } = true;
        public IReadOnlyList<MovieCandidate> MovieCandidates { get; init; } = [Candidate("Movie", "movie-1")];
        public TaskCompletionSource<IReadOnlyList<MovieCandidate>>? MovieSearchGate { get; init; }
        public TaskCompletionSource MovieSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<TvCandidate> TvCandidates { get; init; } = [];
        public TaskCompletionSource<IReadOnlyList<TvCandidate>>? TvSearchGate { get; init; }
        public TaskCompletionSource TvSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TvShowMetadata? TvMetadata { get; init; }
        public bool ThrowOnMovieSearch { get; init; }
        public MetadataFailureKind? MovieSearchFailureKind { get; init; }
        public bool CancelMovieSearch { get; init; }
        public int TvSearchCount { get; private set; }
        public int MovieSearchCount { get; private set; }
        public int TvMatchCount { get; private set; }

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => MovieCandidates;
        public int MovieDetailsCount { get; private set; }
        public MovieMetadata? GetMovie(string externalId)
        {
            MovieDetailsCount++;
            return new("tmdb", externalId, "Movie", null, 2020,
                null, ImmutableArray<string>.Empty, null, null, "/movie.jpg");
        }
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken ct = default)
        {
            MovieSearchCount++;
            if (MovieSearchFailureKind is { } failureKind) throw new MetadataServiceException(failureKind);
            if (ThrowOnMovieSearch) throw new InvalidOperationException("offline");
            if (CancelMovieSearch) throw new OperationCanceledException(ct);
            if (MovieSearchGate is { } gate)
            {
                MovieSearchStarted.TrySetResult();
                return gate.Task;
            }
            return Task.FromResult(MovieCandidates);
        }
        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken ct = default) =>
            Task.FromResult(GetMovie(externalId));
        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken ct = default)
        {
            TvSearchCount++;
            if (TvSearchGate is { } gate)
            {
                TvSearchStarted.TrySetResult();
                return gate.Task;
            }
            return Task.FromResult(TvCandidates);
        }
        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken ct = default)
        {
            TvMatchCount++;
            return Task.FromResult<TvShowMetadata?>(TvMetadata ?? new TvShowMetadata("tmdb", externalId, "Show", null, 2020,
                null, ImmutableArray<string>.Empty, null, null, null, ImmutableArray<TvSeasonMetadata>.Empty));
        }
    }

    private sealed class BlockingPosterService : IPosterService
    {
        public PosterAsset? LoadPoster(PosterRequest request) => null;

        public Task<string?> EnsurePosterCachedAsync(
            string provider,
            string reference,
            CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<string?>().Task;
    }
}
