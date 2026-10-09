using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Application.Services;

/// <summary>
/// The read side of the TV catalog.
/// <para>
/// A show's hierarchy is loaded with four reads - the show, its seasons, its episodes and the media
/// files of those episodes - and assembled in memory, so opening a show is not one query per episode.
/// Availability is computed from the registered files alone: no file means NoFile, at least one file
/// present means Present, and every file missing means Missing. Nothing here deletes catalog state
/// because a file disappeared.
/// </para>
/// </summary>
public sealed class TvLibraryService(
    ITvShowRepository shows,
    ITvSeasonRepository seasons,
    ITvEpisodeRepository episodes,
    IWatchHistoryRepository? history = null,
    IMetadataProvider? metadataProvider = null,
    Func<DateTimeOffset>? clock = null) : ITvLibraryUiActions
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public DateTimeOffset RecordShowOpened(int showId)
    {
        var openedAt = _clock().ToUniversalTime();
        shows.RecordOpened(showId, openedAt);
        return openedAt;
    }
    public IReadOnlyList<TvShowListItem> ListShows()
    {
        var result = new List<TvShowListItem>();

        foreach (var show in shows.ListAll())
        {
            var showSeasons = seasons.ListByShow(show.Id);
            var showEpisodes = episodes.ListByShow(show.Id);
            var files = episodes.ListMediaFilesByShow(show.Id);

            var withLocalFile = 0;
            var missing = 0;

            foreach (var episode in showEpisodes)
            {
                switch (Availability(files, episode.Id))
                {
                    case EpisodeAvailability.Present:
                        withLocalFile++;
                        break;
                    case EpisodeAvailability.Missing:
                        missing++;
                        break;
                }
            }

            result.Add(new TvShowListItem(
                show.Id,
                show.Title,
                show.Year,
                show.PosterReference,
                showSeasons.Count,
                showEpisodes.Count,
                withLocalFile,
                missing,
                show.DateAdded,
                show.LastOpenedAt,
                show.MetadataStatus));
        }

        return result;
    }

    /// <summary>
    /// The show's hierarchy from local catalog state only - no network, no provider, deterministic and fast.
    /// Missing-episode gaps are inferred from the known local episode numbers. Provider-confirmed enrichment
    /// is a separate, asynchronous step (<see cref="GetShowDetailsEnrichedAsync" />); opening a show never
    /// waits on it.
    /// </summary>
    public TvShowDetails GetShowDetails(int showId)
    {
        var graph = ReadShowGraph(showId);
        var seasonDetails = new List<TvSeasonDetails>();

        foreach (var season in graph.Seasons)
        {
            var seasonEpisodes = graph.EpisodesBySeason.TryGetValue(season.Id, out var found) ? found : [];
            var displayEpisodes = BuildLocalSeasonEpisodes(season.Number, seasonEpisodes, graph.Files, graph.Watched);
            seasonDetails.Add(new TvSeasonDetails(season.Id, season.Number, season.Title, displayEpisodes));
        }

        return ComposeDetails(graph.Show, seasonDetails);
    }

    /// <summary>The show, its files, the watched set, episodes-by-season and seasons - the local reads.</summary>
    private ShowGraph ReadShowGraph(int showId)
    {
        var show = shows.GetById(showId)
            ?? throw new KeyNotFoundException($"TV show {showId} was not found.");
        var files = episodes.ListMediaFilesByShow(showId);
        var watched = history?.GetWatchedEpisodeIds(showId) ?? new HashSet<int>();
        var episodesBySeason = episodes.ListByShow(showId)
            .GroupBy(episode => episode.SeasonId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TvEpisode>)[.. group.OrderBy(episode => episode.Number)]);
        return new ShowGraph(show, files, watched, episodesBySeason, seasons.ListByShow(showId));
    }

    private static TvShowDetails ComposeDetails(TvShow show, IReadOnlyList<TvSeasonDetails> seasonDetails) =>
        new(show.Id, show.Title, show.Data.OriginalTitle, show.Year, show.Overview, show.Genres,
            show.PosterReference, seasonDetails, show.ExternalId, show.MetadataStatus);

    private readonly record struct ShowGraph(
        TvShow Show,
        IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> Files,
        IReadOnlySet<int> Watched,
        IReadOnlyDictionary<int, IReadOnlyList<TvEpisode>> EpisodesBySeason,
        IReadOnlyList<TvSeason> Seasons);

    public IReadOnlyList<MediaFile> ListEpisodeFiles(int episodeId) => episodes.ListMediaFiles(episodeId);

    public int? FindEpisodeForMediaFile(int mediaFileId) => episodes.GetEpisodeIdForMediaFile(mediaFileId);

    public IReadOnlyList<TvContinueWatchingItem> ListContinueWatching(int limit = 20)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (history is null) return [];
        var result = new List<TvContinueWatchingItem>();
        foreach (var show in shows.ListAll())
        {
            var watched = history.GetWatchedEpisodeIds(show.Id);
            if (watched.Count == 0) continue;
            var allEpisodes = episodes.ListByShow(show.Id)
                .OrderBy(episode => seasons.GetById(episode.SeasonId)?.Number ?? int.MaxValue)
                .ThenBy(episode => episode.Number)
                .ToList();
            var next = allEpisodes.FirstOrDefault(episode => !watched.Contains(episode.Id) && IsPresent(episodes.ListMediaFiles(episode.Id)));
            if (next is null) continue;
            var season = seasons.GetById(next.SeasonId);
            result.Add(new TvContinueWatchingItem(
                show.Id, show.Title, show.PosterReference, show.BackdropReference,
                next.Id, season?.Number ?? 0, next.Number, next.Title,
                next.StillReference ?? show.BackdropReference ?? show.PosterReference, true));
            if (result.Count >= limit) break;
        }
        return result;
    }

    /// <summary>
    /// Provider-confirmed enrichment, awaited off the UI load path: re-reads the local hierarchy, then asks
    /// the provider for each season's episode list. A confirmed season gets provider-confirmed gaps; an
    /// unanswered season keeps local inference. Returns null when the show is unmatched, no provider exists,
    /// or nothing was confirmed, so the caller keeps the rendered local result. Never writes catalog state,
    /// never changes local ownership, never invents a media file.
    /// </summary>
    public async Task<TvShowDetails?> GetShowDetailsEnrichedAsync(int showId, CancellationToken cancellationToken = default)
    {
        if (metadataProvider is null)
        {
            return null;
        }

        var graph = ReadShowGraph(showId);
        if (string.IsNullOrWhiteSpace(graph.Show.ExternalId) || graph.Show.MetadataStatus != MetadataStatus.Ready)
        {
            return null;
        }

        var seasonDetails = new List<TvSeasonDetails>();
        var enrichedAny = false;

        foreach (var season in graph.Seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonEpisodes = graph.EpisodesBySeason.TryGetValue(season.Id, out var found) ? found : [];

            TvSeasonMetadata? providerSeason = null;
            try
            {
                providerSeason = await metadataProvider
                    .GetTvSeasonAsync(graph.Show.ExternalId!, season.Number, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                providerSeason = null;
            }
            IReadOnlyList<TvEpisodeDetails> displayEpisodes;
            if (providerSeason != null && !providerSeason.Episodes.IsDefaultOrEmpty)
            {
                displayEpisodes = BuildProviderConfirmedEpisodes(seasonEpisodes, providerSeason.Episodes, graph.Files, graph.Watched);
                enrichedAny = true;
            }
            else
            {
                displayEpisodes = BuildLocalSeasonEpisodes(season.Number, seasonEpisodes, graph.Files, graph.Watched);
            }

            seasonDetails.Add(new TvSeasonDetails(season.Id, season.Number, season.Title, displayEpisodes));
        }

        return enrichedAny ? ComposeDetails(graph.Show, seasonDetails) : null;
    }

    private static IReadOnlyList<TvEpisodeDetails> BuildProviderConfirmedEpisodes(
        IReadOnlyList<TvEpisode> dbEpisodes,
        ImmutableArray<TvEpisodeMetadata> providerEpisodes,
        IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> files,
        IReadOnlySet<int> watched)
    {
        var dbByNumber = dbEpisodes.ToDictionary(e => e.Number);
        var result = new List<TvEpisodeDetails>();
        var seenNumbers = new HashSet<int>();

        foreach (var providerEp in providerEpisodes.OrderBy(e => e.EpisodeNumber))
        {
            seenNumbers.Add(providerEp.EpisodeNumber);
            if (dbByNumber.TryGetValue(providerEp.EpisodeNumber, out var dbEp))
            {
                result.Add(new TvEpisodeDetails(
                    dbEp.Id,
                    dbEp.Number,
                    dbEp.Title ?? providerEp.Title,
                    dbEp.RuntimeMinutes ?? providerEp.RuntimeMinutes,
                    Availability(files, dbEp.Id),
                    files.TryGetValue(dbEp.Id, out var episodeFiles) ? episodeFiles : [],
                    watched.Contains(dbEp.Id)));
            }
            else
            {
                result.Add(new TvEpisodeDetails(
                    0,
                    providerEp.EpisodeNumber,
                    providerEp.Title,
                    providerEp.RuntimeMinutes,
                    EpisodeAvailability.NoFile,
                    [],
                    false));
            }
        }

        foreach (var dbEp in dbEpisodes)
        {
            if (!seenNumbers.Contains(dbEp.Number))
            {
                result.Add(new TvEpisodeDetails(
                    dbEp.Id,
                    dbEp.Number,
                    dbEp.Title,
                    dbEp.RuntimeMinutes,
                    Availability(files, dbEp.Id),
                    files.TryGetValue(dbEp.Id, out var episodeFiles) ? episodeFiles : [],
                    watched.Contains(dbEp.Id)));
            }
        }

        return [.. result.OrderBy(e => e.Number)];
    }

    private static IReadOnlyList<TvEpisodeDetails> BuildLocalSeasonEpisodes(
        int seasonNumber,
        IReadOnlyList<TvEpisode> dbEpisodes,
        IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> files,
        IReadOnlySet<int> watched)
    {
        if (dbEpisodes.Count == 0)
        {
            return [];
        }

        var dbByNumber = dbEpisodes.ToDictionary(e => e.Number);

        // Specials / Season 0: Do not infer numeric gaps unless provider metadata defines them.
        if (seasonNumber == 0)
        {
            return [.. dbEpisodes.OrderBy(e => e.Number).Select(dbEp => new TvEpisodeDetails(
                dbEp.Id,
                dbEp.Number,
                dbEp.Title,
                dbEp.RuntimeMinutes,
                Availability(files, dbEp.Id),
                files.TryGetValue(dbEp.Id, out var episodeFiles) ? episodeFiles : [],
                watched.Contains(dbEp.Id)))];
        }

        var min = dbEpisodes.Min(e => e.Number);
        var max = dbEpisodes.Max(e => e.Number);
        var result = new List<TvEpisodeDetails>(max - min + 1);

        for (var num = min; num <= max; num++)
        {
            if (dbByNumber.TryGetValue(num, out var dbEp))
            {
                result.Add(new TvEpisodeDetails(
                    dbEp.Id,
                    dbEp.Number,
                    dbEp.Title,
                    dbEp.RuntimeMinutes,
                    Availability(files, dbEp.Id),
                    files.TryGetValue(dbEp.Id, out var episodeFiles) ? episodeFiles : [],
                    watched.Contains(dbEp.Id)));
            }
            else
            {
                result.Add(new TvEpisodeDetails(
                    0,
                    num,
                    null,
                    null,
                    EpisodeAvailability.NoFile,
                    [],
                    false));
            }
        }

        return result;
    }

    private static bool IsPresent(IReadOnlyList<MediaFile> files) => files.Any(file => file.Status == MediaFileStatus.Present);

    /// <summary>
    /// An episode is Present when any registered file is on disk, Missing when it has files and none of
    /// them are, and NoFile when nothing has been registered for it yet.
    /// </summary>
    private static EpisodeAvailability Availability(
        IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> files,
        int episodeId)
    {
        if (!files.TryGetValue(episodeId, out var episodeFiles) || episodeFiles.Count == 0)
        {
            return EpisodeAvailability.NoFile;
        }

        return episodeFiles.Any(file => file.Status == MediaFileStatus.Present)
            ? EpisodeAvailability.Present
            : EpisodeAvailability.Missing;
    }
}
