using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Services;

public class LibraryService : ILibraryUiActions, IPersonalLibraryUiActions
{
    private readonly IMovieRepository _movies;
    private readonly IMediaFileRepository _mediaFiles;
    private readonly IPersonalLibraryRepository _personal;
    private readonly Func<DateTimeOffset> _clock;

    public LibraryService(
        IMovieRepository movies,
        IMediaFileRepository mediaFiles,
        IPersonalLibraryRepository personal,
        Func<DateTimeOffset>? clock = null)
    {
        _movies = movies;
        _mediaFiles = mediaFiles;
        _personal = personal;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<MovieListItem> ListMovies()
    {
        // One presence query for the whole grid instead of a per-movie GetByMovieId (the ListMovies N+1
        // that blocked the UI thread for ~38s at 10K). One file-count query serves the multi-file badge.
        var moviesWithFile = _mediaFiles.ListMovieIdsWithPresentFile();
        var fileCounts = _mediaFiles.CountFilesByMovie();
        return _movies.ListAll()
            .Select(movie => ToListItem(movie, moviesWithFile.Contains(movie.Id), fileCounts.GetValueOrDefault(movie.Id, 0)))
            .ToList();
    }

    public MovieListItem GetMovieItem(int movieId)
    {
        var movie = _movies.GetById(movieId) ?? throw new ArgumentException($"Movie {movieId} not found");
        return MapToListItem(movie);
    }

    public MovieDetails GetMovieDetails(int movieId)
    {
        var movie = _movies.GetById(movieId) ?? throw new ArgumentException($"Movie {movieId} not found");
        var files = _mediaFiles.GetByMovieId(movieId);
        return new MovieDetails(
            movie.Id, movie.Title, movie.OriginalTitle, movie.Year, movie.Overview,
            movie.Genres, movie.RuntimeMinutes, movie.Rating, movie.PosterReference, files, movie.ReviewMatch,
            MovieOrganizationNaming.HasTrustworthyMetadata(movie));
    }

    public DateTimeOffset RecordMovieOpened(int movieId)
    {
        var openedAt = _clock().ToUniversalTime();
        _movies.RecordOpened(movieId, openedAt);
        return openedAt;
    }

    private MovieListItem MapToListItem(Movie movie)
    {
        var files = _mediaFiles.GetByMovieId(movie.Id);
        return ToListItem(movie, files.Any(f => f.Status == MediaFileStatus.Present), files.Count);
    }

    private static MovieListItem ToListItem(Movie movie, bool hasPresentFile, int fileCount)
    {
        bool pending = movie.MetadataStatus is MetadataStatus.Pending
            or MetadataStatus.NeedsMatch
            or MetadataStatus.Failed;
        return new MovieListItem(movie.Id, movie.Title, movie.Year, movie.PosterReference, !hasPresentFile, pending, fileCount, movie.ReviewMatch, movie.DateAdded, movie.LastOpenedAt, movie.BackdropReference);
    }

    public PersonalMovieSnapshot GetPersonalSnapshot(int movieId)
    {
        return MapSnapshot(_personal.GetState(movieId));
    }

    public PersonalMovieSnapshot SetPersonalPreference(int movieId, PersonalPreference preference)
    {
        return MapSnapshot(_personal.SetPreference(movieId, preference, _clock()));
    }

    public PersonalMovieSnapshot ClearPersonalPreference(int movieId) =>
        MapSnapshot(_personal.ClearPreference(movieId, _clock()));

    public PersonalMovieSnapshot AddToWatchlist(int movieId)
    {
        return MapSnapshot(_personal.AddToWatchlist(movieId, _clock()));
    }

    public PersonalMovieSnapshot RemoveFromWatchlist(int movieId)
    {
        return MapSnapshot(_personal.RemoveFromWatchlist(movieId, _clock()));
    }

    public PersonalMovieSnapshot RecordWatch(int movieId, DateTimeOffset? watchedAt = null)
    {
        var now = _clock();
        _personal.AddWatchEvent(movieId, watchedAt ?? now, now);
        return MapSnapshot(_personal.GetState(movieId));
    }

    public PersonalMovieSnapshot RemoveWatchEvent(int eventId)
    {
        var removed = _personal.DeleteWatchEvent(eventId);
        return MapSnapshot(_personal.GetState(removed.MovieId));
    }

    public IReadOnlyList<WatchEvent> ListWatchEvents(int movieId) =>
        _personal.GetWatchEvents(movieId)
            .OrderByDescending(watch => watch.WatchedAt)
            .ToList();

    public IReadOnlyList<MovieListItem> ListPersonalMovies(PersonalLibrarySection section)
    {
        // The summary already carries file counts, so map from it instead of a per-movie GetByMovieId.
        return _personal.ListMovies(section, limit: int.MaxValue)
            .Select(summary => ToListItem(summary.Movie, summary.MediaFileCount > summary.MissingFileCount, summary.MediaFileCount))
            .ToList();
    }

    public IReadOnlyList<CustomList> ListCustomLists() =>
        _personal.ListCustomLists().Select(list => new CustomList(list.Id, list.Name, list.ItemCount)).ToList();

    public CustomList CreateCustomList(string name)
    {
        var id = _personal.CreateCustomList(name, _clock());
        return new CustomList(id, name.Trim(), 0);
    }

    public void RenameCustomList(int listId, string name) => _personal.RenameCustomList(listId, name, _clock());

    public void DeleteCustomList(int listId) => _personal.DeleteCustomList(listId);

    public IReadOnlySet<int> AddMovieToCustomList(int listId, int movieId)
    {
        _personal.AddMovieToCustomList(listId, movieId, _clock());
        return _personal.GetCustomListIdsForMovie(movieId);
    }

    public IReadOnlySet<int> RemoveMovieFromCustomList(int listId, int movieId)
    {
        _personal.RemoveMovieFromCustomList(listId, movieId);
        return _personal.GetCustomListIdsForMovie(movieId);
    }

    public IReadOnlySet<int> GetCustomListIdsForMovie(int movieId) =>
        _personal.GetCustomListIdsForMovie(movieId);

    public IReadOnlySet<int> AddShowToCustomList(int listId, int showId)
    {
        _personal.AddShowToCustomList(listId, showId, _clock());
        return _personal.GetCustomListIdsForShow(showId);
    }

    public IReadOnlySet<int> RemoveShowFromCustomList(int listId, int showId)
    {
        _personal.RemoveShowFromCustomList(listId, showId);
        return _personal.GetCustomListIdsForShow(showId);
    }

    public IReadOnlySet<int> GetCustomListIdsForShow(int showId) =>
        _personal.GetCustomListIdsForShow(showId);

    public IReadOnlyList<CustomListItem> ListCustomListItems(int listId) =>
        _personal.ListCustomListItems(listId)
            .Select(member => new CustomListItem(
                member.Kind, member.Id, member.Title, member.Year, member.PosterReference, member.IsMissing))
            .ToList();

    private PersonalMovieSnapshot MapSnapshot(PersonalMovieState state) =>
        new PersonalMovieSnapshot(state.MovieId, state.Preference, state.IsWatchlisted, state.WatchCount, state.LastWatched);
}
