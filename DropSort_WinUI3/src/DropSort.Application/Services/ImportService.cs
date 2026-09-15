using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Application.External;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Metadata.Contracts;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Matcher;

namespace DropSort.Application.Services;

public class ImportService : IImportUiActions
{
    private readonly ICatalogUnitOfWorkFactory _uowFactory;
    private readonly IMetadataProvider _metadataProvider;
    private readonly IMediaDiscoveryService? _discovery;
    private readonly MetadataMatchingService? _matching;
    private readonly Func<DateTimeOffset> _clock;

    public ImportService(
        ICatalogUnitOfWorkFactory uowFactory,
        IMetadataProvider metadataProvider,
        IMediaDiscoveryService? discovery = null,
        Func<DateTimeOffset>? clock = null,
        MetadataMatchingService? matching = null)
    {
        _uowFactory = uowFactory;
        _metadataProvider = metadataProvider;
        _discovery = discovery;
        _matching = matching;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ImportReviewSession PrepareImportReview(string rootPath, bool recursive, Action<ImportReviewProgress>? progress = null, Func<bool>? isCancelled = null)
    {
        if (_discovery is null) throw new InvalidOperationException("Media discovery is not configured.");
        var items = _discovery.Discover(
            rootPath,
            recursive,
            (discovered, processed, current) => progress?.Invoke(new ImportReviewProgress(discovered, processed, current)),
            isCancelled);
        return new ImportReviewSession(items);
    }

    public MovieFileIngestionResult ConfirmMovieImport(ConfirmMovieImportCommand command)
    {
        // V1: Confirms import by registering locally, then enriching if a candidate was selected.
        var registration = RegisterMovieImport(command);
        if (command.SelectedCandidate != null)
        {
            return EnrichMovieImport(command, registration);
        }
        return registration;
    }

    public async Task<ImportBatchResult> ImportSelectedAsync(
        IReadOnlyList<ImportSelection> selections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        var results = new List<ImportItemResult>(selections.Count);
        var moviesPending = new List<MovieImportWork>();
        var tvShows = new Dictionary<int, TvImportGroup>();

        foreach (var selection in selections)
        {
            ArgumentNullException.ThrowIfNull(selection);
            var item = selection.Item;
            if (item.FileSize is not long size || item.ParsedMedia is null)
            {
                results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                    Failure: "The discovered item has no verified file facts."));
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Classification == DiscoveryClassification.MovieCandidate)
                {
                    var registration = RegisterMovieImport(new ConfirmMovieImportCommand(
                        item.Path, size, item.ParsedMedia, _clock()));
                    var resultIndex = results.Count;
                    results.Add(new ImportItemResult(item.Path, item.Classification, true, false));
                    if (_matching is not null
                        && _metadataProvider.IsConfigured
                        && registration.Movie.MetadataStatus != MetadataStatus.Ready
                        && !string.IsNullOrWhiteSpace(item.ParsedMedia.Title))
                    {
                        moviesPending.Add(new MovieImportWork(
                            resultIndex,
                            registration.Movie.Id,
                            item.ParsedMedia.Title!,
                            item.ParsedMedia.Year,
                            registration.Movie.MetadataStatus));
                    }
                }
                else if (item.Classification == DiscoveryClassification.TvEpisodeCandidate)
                {
                    var registration = RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
                        item.Path, size, item.ParsedMedia, _clock()));
                    var resultIndex = results.Count;
                    results.Add(new ImportItemResult(item.Path, item.Classification, true, false));
                    // A new episode can belong to an already matched show. Only pending/failed
                    // show metadata should trigger an automatic lookup; never replace an accepted
                    // provider identity merely because another local episode was added.
                    var requiresMatch = registration.Show.MetadataStatus != MetadataStatus.Ready;
                    var requiresRefresh = !registration.AlreadyRegistered
                        && registration.Show.MetadataStatus == MetadataStatus.Ready;
                    if (!tvShows.TryGetValue(registration.Show.Id, out var pending))
                    {
                        pending = new TvImportGroup(
                            registration.Show,
                            item.ParsedMedia.Year,
                            requiresMatch,
                            requiresRefresh,
                            registration.Show.MetadataStatus);
                        tvShows.Add(registration.Show.Id, pending);
                    }
                    else
                    {
                        pending.ShouldMatch |= requiresMatch;
                        pending.ShouldRefresh |= requiresRefresh;
                    }
                    pending.ResultIndexes.Add(resultIndex);
                }
                else
                {
                    results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                        Failure: "Only resolved movie and TV episode candidates can be imported."));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                    Failure: error.Message));
            }
        }

        if (_matching is not null && _metadataProvider.IsConfigured)
        {
            foreach (var pending in moviesPending)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var decision = await _matching.SearchMovieCandidatesAsync(
                        pending.Title, pending.Year, cancellationToken);
                    results[pending.ResultIndex] = results[pending.ResultIndex] with { MatchStatus = decision.Status };
                    if (decision.Status == MatchStatus.Matched && decision.Candidate is not null)
                    {
                        await _matching.MatchMovieAsync(
                            pending.MovieId,
                            decision.Candidate,
                            cancellationToken,
                            pending.ExpectedMetadataStatus);
                        results[pending.ResultIndex] = results[pending.ResultIndex] with { AutoMatched = true };
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception error) when (IsBestEffortMetadataFailure(error))
                {
                    results[pending.ResultIndex] = results[pending.ResultIndex] with { Failure = error.Message };
                }
            }

            foreach (var pending in tvShows.Values)
            {
                if (pending.ShouldRefresh)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var refreshed = await _matching.RefreshTvShowMetadataAsync(
                            pending.Show.Id,
                            fetchEpisodes: true,
                            ct: cancellationToken);
                        if (refreshed is not null)
                        {
                            foreach (var index in pending.ResultIndexes)
                                results[index] = results[index] with { AutoMatched = true };
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception error) when (IsBestEffortMetadataFailure(error))
                    {
                        foreach (var index in pending.ResultIndexes)
                            results[index] = results[index] with { Failure = error.Message };
                    }
                }

                if (!pending.ShouldMatch)
                {
                    continue;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var decision = await _matching.SearchTvCandidatesAsync(
                        pending.Show.Title, pending.Year, cancellationToken);
                    foreach (var index in pending.ResultIndexes)
                        results[index] = results[index] with { MatchStatus = decision.Status };
                    if (decision.Status == MatchStatus.Matched && decision.Candidate is not null)
                    {
                        await _matching.MatchTvShowAsync(
                            pending.Show.Id,
                            decision.Candidate,
                            ct: cancellationToken,
                            expectedMetadataStatus: pending.ExpectedMetadataStatus);
                        foreach (var index in pending.ResultIndexes)
                            results[index] = results[index] with { AutoMatched = true };
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception error) when (IsBestEffortMetadataFailure(error))
                {
                    foreach (var index in pending.ResultIndexes)
                        results[index] = results[index] with { Failure = error.Message };
                }
            }
        }

        return new ImportBatchResult(results);
    }

    private static bool IsBestEffortMetadataFailure(Exception error) =>
        error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private sealed record MovieImportWork(
        int ResultIndex,
        int MovieId,
        string Title,
        int? Year,
        MetadataStatus ExpectedMetadataStatus);

    private sealed class TvImportGroup
    {
        public TvImportGroup(
            TvShow show,
            int? year,
            bool shouldMatch,
            bool shouldRefresh,
            MetadataStatus expectedMetadataStatus)
        {
            Show = show;
            Year = year;
            ShouldMatch = shouldMatch;
            ShouldRefresh = shouldRefresh;
            ExpectedMetadataStatus = expectedMetadataStatus;
        }

        public TvShow Show { get; }
        public int? Year { get; }
        public List<int> ResultIndexes { get; } = [];
        public bool ShouldMatch { get; set; }
        public bool ShouldRefresh { get; set; }
        public MetadataStatus ExpectedMetadataStatus { get; }
    }

    public MovieFileIngestionResult RegisterMovieImport(ConfirmMovieImportCommand command)
    {
        var movieData = new MovieCatalogData(
            provider: null,
            externalId: null,
            title: command.ParsedMedia.Title ?? System.IO.Path.GetFileNameWithoutExtension(command.FilePath),
            originalTitle: null,
            year: command.ParsedMedia.Year,
            overview: null,
            genres: System.Collections.Immutable.ImmutableArray<string>.Empty,
            runtimeMinutes: null,
            rating: null,
            posterReference: null,
            metadataStatus: MetadataStatus.Pending);

        var fileFacts = new VerifiedMediaFileFacts(
            command.FilePath,
            command.FileSize,
            command.ParsedMedia.Extension,
            command.ParsedMedia.Resolution,
            command.ParsedMedia.Codec,
            command.ParsedMedia.Source,
            command.ObservedAt);

        using var uow = _uowFactory.Begin();
        
        var existingFile = uow.MediaFiles.GetByPath(command.FilePath);
        if (existingFile != null && existingFile.MovieId.HasValue)
        {
            var existingMovie = uow.Movies.GetById(existingFile.MovieId.Value)!;
            return new MovieFileIngestionResult(existingMovie, existingFile);
        }

        var movie = uow.Movies.Create(movieData, _clock());
        var mediaFile = existingFile == null 
            ? uow.MediaFiles.Add(fileFacts, movie.Id)
            : uow.MediaFiles.LinkToMovie(existingFile.Id, movie.Id);
            
        uow.Commit();
        return new MovieFileIngestionResult(movie, mediaFile);
    }

    public MovieFileIngestionResult EnrichMovieImport(ConfirmMovieImportCommand command, MovieFileIngestionResult registration)
    {
        if (command.SelectedCandidate == null) throw new ArgumentException("Enrichment requires a selected candidate.");
        
        // Simulates TMDB optional enrichment contract
        var remote = _metadataProvider.GetMovie(command.SelectedCandidate.ExternalId);
        if (remote == null) return registration; // Fails gracefully or throws depending on specific error.
        
        var data = new MovieCatalogData(
            provider: remote.Provider,
            externalId: remote.ExternalId,
            title: remote.Title,
            originalTitle: remote.OriginalTitle,
            year: remote.Year,
            overview: remote.Overview,
            genres: remote.Genres,
            runtimeMinutes: remote.RuntimeMinutes,
            rating: remote.Rating,
            posterReference: remote.PosterReference,
            metadataStatus: MetadataStatus.Ready);

        using var uow = _uowFactory.Begin();
        var updatedMovie = uow.Movies.UpdateMetadata(registration.Movie.Id, data, _clock());
        uow.Commit();
        
        return new MovieFileIngestionResult(updatedMovie, registration.MediaFile);
    }

    public ManualMovieSearchResult ManualMovieSearch(string title, string? year = null)
    {
        int? parsedYear = int.TryParse(year, out var y) ? y : null;
        var results = _metadataProvider.Search(new MovieSearchQuery(title, parsedYear));
        return new ManualMovieSearchResult(results);
    }

    /// <summary>
    /// Registers one episode file into the hierarchy.
    /// <para>
    /// Everything happens in one transaction: the show is looked up by its normalized title and created
    /// only if it is new, then the season, then the episode, then the media file is stored and linked.
    /// Re-registering the same path returns the rows it already belongs to, so scanning a folder twice
    /// does not duplicate anything and does not move a file between episodes.
    /// </para>
    /// <para>
    /// A file the parser could not resolve is refused with the reason instead of being registered
    /// against a guessed show or season - that is what keeps an ambiguous name from corrupting the
    /// hierarchy.
    /// </para>
    /// </summary>
    public EpisodeFileIngestionResult RegisterEpisodeImport(ConfirmEpisodeImportCommand command)
    {
        var parsed = command.ParsedMedia;

        if (parsed.SeasonNumber is not int seasonNumber || parsed.EpisodeNumber is not int episodeNumber)
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NoEpisodeMarker,
                $"'{System.IO.Path.GetFileName(command.FilePath)}' carries no season and episode marker.");
        }

        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NoShowTitle,
                $"'{System.IO.Path.GetFileName(command.FilePath)}' has no show name before the episode marker.");
        }

        if (seasonNumber is < 0 or > 999 || episodeNumber is < 0 or > 999)
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NumbersOutOfRange,
                $"Season {seasonNumber} episode {episodeNumber} is outside the range the catalog accepts.");
        }

        var showData = new TvShowCatalogData(provider: null, externalId: null, title: parsed.Title!);
        var now = _clock();

        using var uow = _uowFactory.Begin();

        var existingFile = uow.MediaFiles.GetByPath(command.FilePath);

        if (existingFile is not null)
        {
            if (existingFile.MovieId.HasValue)
            {
                throw new EpisodeRegistrationException(
                    EpisodeRegistrationRefusal.AlreadyAMovieFile,
                    $"'{System.IO.Path.GetFileName(command.FilePath)}' is already registered as a movie file.");
            }

            var owner = uow.TvEpisodes.GetEpisodeIdForMediaFile(existingFile.Id);

            if (owner is int ownerEpisodeId)
            {
                var ownerEpisode = uow.TvEpisodes.GetById(ownerEpisodeId)!;
                var ownerSeason = uow.TvSeasons.GetById(ownerEpisode.SeasonId)!;
                var ownerShow = uow.TvShows.GetById(ownerSeason.ShowId)!;

                var isSameEpisode = ownerSeason.Number == seasonNumber
                    && ownerEpisode.Number == episodeNumber
                    && string.Equals(ownerShow.SortTitle, showData.SortTitle, StringComparison.Ordinal);

                if (!isSameEpisode)
                {
                    throw new EpisodeRegistrationException(
                        EpisodeRegistrationRefusal.OwnedByAnotherEpisode,
                        $"'{System.IO.Path.GetFileName(command.FilePath)}' is already registered as "
                            + $"{ownerShow.Title} S{ownerSeason.Number:00}E{ownerEpisode.Number:00}.");
                }

                return new EpisodeFileIngestionResult(
                    ownerShow,
                    ownerSeason,
                    ownerEpisode,
                    existingFile,
                    AlreadyRegistered: true);
            }
        }

        var show = uow.TvShows.GetBySortTitle(showData.SortTitle)
            ?? uow.TvShows.Create(showData, now);

        var season = uow.TvSeasons.GetByNumber(show.Id, seasonNumber)
            ?? uow.TvSeasons.Create(show.Id, seasonNumber, title: null, overview: null, now);

        var episode = uow.TvEpisodes.GetByNumber(season.Id, episodeNumber)
            ?? uow.TvEpisodes.Create(
                season.Id,
                episodeNumber,
                title: null,
                overview: null,
                runtimeMinutes: null,
                airDate: null,
                now);

        var facts = new VerifiedMediaFileFacts(
            command.FilePath,
            command.FileSize,
            parsed.Extension,
            parsed.Resolution,
            parsed.Codec,
            parsed.Source,
            command.ObservedAt);

        var mediaFile = existingFile ?? uow.MediaFiles.Add(facts);
        uow.TvEpisodes.LinkMediaFile(episode.Id, mediaFile.Id, now);

        uow.Commit();

        return new EpisodeFileIngestionResult(show, season, episode, mediaFile, AlreadyRegistered: false);
    }
}
