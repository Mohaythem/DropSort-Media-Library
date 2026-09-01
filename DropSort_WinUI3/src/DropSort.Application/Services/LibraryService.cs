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
        return _movies.ListAll().Select(MapToListItem).ToList();
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
            movie.Genres, movie.RuntimeMinutes, movie.Rating, movie.PosterReference, files);
    }

    private MovieListItem MapToListItem(Movie movie)
    {
        var files = _mediaFiles.GetByMovieId(movie.Id);
        bool isMissing = files.Count == 0 || files.All(f => f.Status == MediaFileStatus.Missing);
        bool pending = movie.MetadataStatus == MetadataStatus.Pending || movie.MetadataStatus == MetadataStatus.NeedsMatch;
        return new MovieListItem(movie.Id, movie.Title, movie.Year, movie.PosterReference, isMissing, pending);
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

    public IReadOnlyList<MovieListItem> ListPersonalMovies(PersonalLibrarySection section)
    {
        return _personal.ListMovies(section).Select(summary => MapToListItem(summary.Movie)).ToList();
    }

    private PersonalMovieSnapshot MapSnapshot(PersonalMovieState state) =>
        new PersonalMovieSnapshot(state.MovieId, state.Preference, state.IsWatchlisted, state.WatchCount, state.LastWatched);
}
