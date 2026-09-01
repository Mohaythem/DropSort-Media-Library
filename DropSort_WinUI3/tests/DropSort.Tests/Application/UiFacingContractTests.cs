using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// The two contracts the wired UI depends on that had no coverage: the watch history the Movie Details
/// page lists (and removes rows from by event id), and the progress Check Library draws while the
/// reconciliation pass walks the disk.
/// </summary>
public class UiFacingContractTests
{
    [Fact]
    public void Watch_events_are_listed_newest_first_with_their_ids()
    {
        var personal = new RecordingPersonalRepository();
        var service = new LibraryService(new EmptyMovieRepository(), new EmptyMediaFileRepository(), personal);

        personal.Add(1, DateTimeOffset.Parse("2024-03-02T20:00:00Z"));
        personal.Add(1, DateTimeOffset.Parse("2026-08-14T20:00:00Z"));

        var events = service.ListWatchEvents(1);

        Assert.Equal(2, events.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-08-14T20:00:00Z"), events[0].WatchedAt);
        Assert.All(events, watch => Assert.True(watch.Id > 0));
    }

    [Fact]
    public void Check_library_reports_progress_while_it_walks_the_files()
    {
        var files = new StubMediaFileRepository(
        [
            MediaFileFor(1, @"C:\media\one.mkv"),
            MediaFileFor(2, @"C:\media\two.mkv"),
        ]);

        var service = new ReconciliationService(files, new PresentInspector(), new EmptyMovieRepository());
        var updates = new List<LibraryHealthProgress>();

        var result = service.CheckLibrary(progress: updates.Add);

        // One update per file plus the final summary: a silent callback would leave the designed
        // progress row frozen for the whole scan, and the count has to climb as files are checked.
        Assert.True(updates.Count >= 3, "expected per-file progress, got " + updates.Count);
        Assert.Equal(1, updates[0].FileProgress.CheckedFiles);
        Assert.Equal(2, updates[1].FileProgress.CheckedFiles);
        Assert.Equal(2, result.FileProgress.CheckedFiles);
    }

    private static MediaFile MediaFileFor(int id, string path) => new(
        id,
        null,
        path,
        7,
        ".mkv",
        null,
        null,
        null,
        MediaFileStatus.Present,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private sealed class PresentInspector : IAvailabilityInspector
    {
        public AvailabilityInspection Inspect(string path) => new(
            path,
            AvailabilityInspectionStatus.Present,
            new MediaFileIdentity(7, 0, 0, 0, 0));
    }

    private sealed class StubMediaFileRepository(IReadOnlyList<MediaFile> files) : IMediaFileRepository
    {
        public MediaFile? GetById(int id) => files.FirstOrDefault(file => file.Id == id);

        public MediaFile? GetByPath(string path) => null;

        public IReadOnlyList<MediaFile> GetByMovieId(int movieId) => [];

        public IReadOnlyList<MediaFile> ListAll() => files;

        public IReadOnlyList<MediaFile> ListMissing() => [];

        public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) => throw new NotSupportedException();

        public MediaFile LinkToMovie(int id, int movieId) => throw new NotSupportedException();

        public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) => GetById(id)!;

        public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => GetById(id)!;

        public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) => GetById(id)!;

        public void Delete(int id)
        {
        }
    }

    private sealed class EmptyMediaFileRepository : IMediaFileRepository
    {
        public MediaFile? GetById(int id) => null;

        public MediaFile? GetByPath(string path) => null;

        public IReadOnlyList<MediaFile> GetByMovieId(int movieId) => [];

        public IReadOnlyList<MediaFile> ListAll() => [];

        public IReadOnlyList<MediaFile> ListMissing() => [];

        public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) => throw new NotSupportedException();

        public MediaFile LinkToMovie(int id, int movieId) => throw new NotSupportedException();

        public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) => throw new NotSupportedException();

        public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => throw new NotSupportedException();

        public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) => throw new NotSupportedException();

        public void Delete(int id)
        {
        }
    }

    private sealed class EmptyMovieRepository : IMovieRepository
    {
        public Movie? GetById(int id) => null;

        public Movie? GetByExternalId(string provider, string externalId) => null;

        public IReadOnlyList<Movie> ListAll() => [];

        public IReadOnlyList<Movie> ListPage(int afterId, int limit) => [];

        public int CountAll() => 0;

        public Movie Create(MovieCatalogData data, DateTimeOffset now) => throw new NotSupportedException();

        public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now) => throw new NotSupportedException();

        public void Delete(int id)
        {
        }
    }

    private sealed class RecordingPersonalRepository : IPersonalLibraryRepository
    {
        private readonly List<WatchEvent> _events = [];
        private int _nextId = 1;

        public void Add(int movieId, DateTimeOffset watchedAt) =>
            _events.Add(new WatchEvent(_nextId++, movieId, watchedAt, _events.Count > 0));

        public IReadOnlyList<WatchEvent> GetWatchEvents(int movieId) =>
            [.. _events.Where(watch => watch.MovieId == movieId)];

        public PersonalMovieState GetState(int movieId) => new(movieId, PersonalPreference.NoOpinion, null, 0, null, null, null);

        public PersonalMovieState SetPreference(int movieId, PersonalPreference preference, DateTimeOffset now) =>
            GetState(movieId);

        public PersonalMovieState ClearPreference(int movieId, DateTimeOffset now) => GetState(movieId);

        public PersonalMovieState AddToWatchlist(int movieId, DateTimeOffset now) => GetState(movieId);

        public PersonalMovieState RemoveFromWatchlist(int movieId, DateTimeOffset now) => GetState(movieId);

        public WatchEvent AddWatchEvent(int movieId, DateTimeOffset watchedAt, DateTimeOffset createdAt)
        {
            Add(movieId, watchedAt);
            return _events[^1];
        }

        public WatchEvent DeleteWatchEvent(int eventId)
        {
            var removed = _events.First(watch => watch.Id == eventId);
            _events.Remove(removed);
            return removed;
        }

        public IReadOnlyList<PersonalMovieSummary> ListMovies(
            PersonalLibrarySection section,
            int limit = 100,
            int offset = 0) => [];
    }
}
