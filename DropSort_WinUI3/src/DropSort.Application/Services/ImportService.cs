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

    public bool IsMediaFileRegistered(string filePath)
    {
        using var uow = _uowFactory.Begin();
        return uow.MediaFiles.GetByPath(filePath) is not null;
    }

    public Task<ImportMetadataPreview> RetryMetadataAsync(
        DiscoveredMedia item, CancellationToken cancellationToken = default) =>
        PreviewMetadataAsync(item, cancellationToken);

    public async Task<ImportMetadataPreview> PreviewMetadataAsync(
        DiscoveredMedia item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsMediaFileRegistered(item.Path))
        {
            return new ImportMetadataPreview(ImportItemStatus.AlreadyInLibrary);
        }
        if (item.Classification is not DiscoveryClassification.MovieCandidate
            and not DiscoveryClassification.TvEpisodeCandidate)
        {
            return new ImportMetadataPreview(ImportItemStatus.MetadataError,
                Failure: "Only resolved movie and TV episode candidates can be matched.",
                MetadataFailureKind: MetadataFailureKind.InvalidResponse);
        }
        if (_matching is null || !_metadataProvider.IsConfigured)
        {
            return new ImportMetadataPreview(ImportItemStatus.MetadataUnavailable,
                Failure: "Metadata service is not configured.",
                MetadataFailureKind: MetadataFailureKind.NotConfigured);
        }
        if (item.ParsedMedia is null || string.IsNullOrWhiteSpace(item.ParsedMedia.Title))
        {
            return new ImportMetadataPreview(ImportItemStatus.NoMatchFound,
                MatchStatus: Domain.Media.Matcher.MatchStatus.NoMatch,
                Failure: "No parsed title is available for metadata matching.");
        }

        try
        {
            if (item.Classification == DiscoveryClassification.MovieCandidate)
            {
                var decision = await _matching.SearchMovieCandidatesAsync(
                    item.ParsedMedia.Title!, item.ParsedMedia.Year, cancellationToken);
                if (decision.Status == MatchStatus.Matched && decision.Candidate is null)
                {
                    return new ImportMetadataPreview(
                        ImportItemStatus.MetadataError,
                        MatchStatus: decision.Status,
                        Failure: SafeMetadataFailure(MetadataFailureKind.InvalidResponse),
                        MetadataFailureKind: MetadataFailureKind.InvalidResponse);
                }
                var previewStatus = PreviewStatusFor(decision.Status, decision.Confidence, decision.Candidate is not null);
                var previewCandidate = decision.Candidate;
                var previewMatchStatus = decision.Status;
                if (previewStatus != ImportItemStatus.Matched
                    && SoleCredibleFallback(decision, item.ParsedMedia.Title!, item.ParsedMedia.Year) is { } fallback)
                {
                    // Exactly one credible sole result below 0.75 (e.g. "Dune Part One" -> "Dune 2021"):
                    // surface it as ready to add and persist a Review Match rather than block for review.
                    previewStatus = ImportItemStatus.Matched;
                    previewCandidate = fallback;
                    previewMatchStatus = MatchStatus.ReviewRequired;
                }
                return new ImportMetadataPreview(
                    previewStatus,
                    previewMatchStatus,
                    MovieCandidate: previewCandidate,
                    Failure: previewStatus == ImportItemStatus.Matched ? null
                        : decision.Status == MatchStatus.NoMatch ? "No safe metadata match was found." : null);
            }

            var tvDecision = await _matching.SearchTvCandidatesAsync(
                item.ParsedMedia.Title!, item.ParsedMedia.Year, cancellationToken);
            if (tvDecision.Status == MatchStatus.Matched && tvDecision.Candidate is null)
            {
                return new ImportMetadataPreview(
                    ImportItemStatus.MetadataError,
                    MatchStatus: tvDecision.Status,
                    Failure: SafeMetadataFailure(MetadataFailureKind.InvalidResponse),
                    MetadataFailureKind: MetadataFailureKind.InvalidResponse);
            }
            return new ImportMetadataPreview(
                PreviewStatusFor(tvDecision.Status, tvDecision.Confidence, tvDecision.Candidate is not null),
                tvDecision.Status,
                TvCandidate: tvDecision.Candidate,
                Failure: tvDecision.Status == MatchStatus.NoMatch ? "No safe metadata match was found." : null);
        }
        catch (OperationCanceledException)
        {
            return new ImportMetadataPreview(ImportItemStatus.MetadataCancelled,
                Failure: "Metadata matching was cancelled.");
        }
        catch (Exception error) when (IsBestEffortMetadataFailure(error))
        {
            var kind = GetMetadataFailureKind(error);
            return new ImportMetadataPreview(ImportItemStatus.MetadataError,
                Failure: SafeMetadataFailure(kind), MetadataFailureKind: kind);
        }
    }

    public MovieFileIngestionResult ConfirmMovieImport(ConfirmMovieImportCommand command)
    {
        // V1: Confirms import by registering locally, then enriching if a candidate was selected.
        var registration = RegisterMovieImport(command);
        if (registration.AlreadyRegistered)
        {
            // An exact path is already in the catalog. Do not re-run an explicitly supplied
            // candidate through the metadata provider when this legacy confirmation entry point is
            // used instead of the batch Add Media flow.
            return registration;
        }
        if (command.SelectedCandidate != null)
        {
            return EnrichMovieImport(command, registration);
        }
        return registration;
    }

    public async Task<ImportBatchResult> ImportSelectedAsync(
        IReadOnlyList<ImportSelection> selections,
        CancellationToken cancellationToken = default,
        Action<ImportProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(selections);
        var results = new List<ImportItemResult>(selections.Count);
        var moviesPending = new List<MovieImportWork>();
        var tvShows = new Dictionary<int, TvImportGroup>();

        var processed = 0;
        foreach (var selection in selections)
        {
            // Real processed/total progress for the bulk-add UI, reported as each selected item is
            // registered locally. This is the dominant per-item work in a large batch.
            progress?.Invoke(new ImportProgress(processed, selections.Count));
            processed++;
            ArgumentNullException.ThrowIfNull(selection);
            var item = selection.Item;
            if (item.FileSize is not long size || item.ParsedMedia is null)
            {
                results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                    Failure: "The discovered item has no verified file facts.",
                    Status: ImportItemStatus.RegistrationFailed));
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Path identity is the duplicate boundary. Check it before classification-specific
                // registration and before any metadata work so an existing file never reaches TMDB.
                if (IsMediaFileRegistered(item.Path))
                {
                    results.Add(new ImportItemResult(
                        item.Path,
                        item.Classification,
                        Registered: true,
                        AutoMatched: false,
                        Status: ImportItemStatus.AlreadyInLibrary,
                        AlreadyRegistered: true));
                    continue;
                }

                if (item.Classification == DiscoveryClassification.MovieCandidate)
                {
                    var registration = RegisterMovieImport(new ConfirmMovieImportCommand(
                        item.Path, size, item.ParsedMedia, _clock()));
                    if (registration.AlreadyRegistered)
                    {
                        results.Add(new ImportItemResult(
                            item.Path,
                            item.Classification,
                            Registered: true,
                            AutoMatched: false,
                            Status: ImportItemStatus.AlreadyInLibrary,
                            AlreadyRegistered: true));
                        continue;
                    }

                    var resultIndex = results.Count;
                    var needsMetadata = registration.Movie.MetadataStatus != MetadataStatus.Ready;
                    var staged = selection.MetadataPreview;
                    var canAttemptMetadata = needsMetadata && _matching is not null && _metadataProvider.IsConfigured
                        && (staged is null || staged.Status == ImportItemStatus.Matched);
                    var initialStatus = !needsMetadata ? ImportItemStatus.Matched
                        : staged is not null && staged.Status != ImportItemStatus.Matched ? staged.Status
                        : canAttemptMetadata ? ImportItemStatus.Matching : ImportItemStatus.MetadataUnavailable;
                    results.Add(new ImportItemResult(item.Path, item.Classification, true, false,
                        MatchStatus: staged?.MatchStatus,
                        Failure: staged?.Failure,
                        Status: initialStatus,
                        MetadataFailureKind: staged?.MetadataFailureKind
                            ?? (needsMetadata && !canAttemptMetadata && staged is null
                                ? MetadataFailureKind.NotConfigured : null),
                        AlreadyRegistered: registration.AlreadyRegistered));
                    if (canAttemptMetadata)
                    {
                        moviesPending.Add(new MovieImportWork(
                            resultIndex,
                            registration.Movie.Id,
                            registration.Movie.Title,
                            registration.Movie.Year,
                            registration.Movie.MetadataStatus,
                            staged));
                    }
                    else if (needsMetadata)
                    {
                        if (staged?.Status is ImportItemStatus.NeedsMatch or ImportItemStatus.NoMatchFound)
                        {
                            TrySetMovieStatus(registration.Movie.Id, MetadataStatus.NeedsMatch, registration.Movie.MetadataStatus);
                        }
                        else if (needsMetadata)
                        {
                            TrySetMovieStatus(registration.Movie.Id, MetadataStatus.Failed, registration.Movie.MetadataStatus);
                        }
                    }
                }
                else if (item.Classification == DiscoveryClassification.TvEpisodeCandidate)
                {
                    var registration = RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
                        item.Path, size, item.ParsedMedia, _clock()));
                    if (registration.AlreadyRegistered)
                    {
                        results.Add(new ImportItemResult(
                            item.Path,
                            item.Classification,
                            Registered: true,
                            AutoMatched: false,
                            Status: ImportItemStatus.AlreadyInLibrary,
                            AlreadyRegistered: true));
                        continue;
                    }

                    var resultIndex = results.Count;
                    var requiresMatch = registration.Show.MetadataStatus != MetadataStatus.Ready;
                    var requiresRefresh = !registration.AlreadyRegistered && !requiresMatch;
                    var needsMetadata = requiresMatch || requiresRefresh;
                    var staged = selection.MetadataPreview;
                    var canAttemptMetadata = needsMetadata && _matching is not null && _metadataProvider.IsConfigured
                        && (staged is null || staged.Status == ImportItemStatus.Matched);
                    var initialStatus = !needsMetadata ? ImportItemStatus.Matched
                        : staged is not null && staged.Status != ImportItemStatus.Matched ? staged.Status
                        : canAttemptMetadata ? ImportItemStatus.Matching : ImportItemStatus.MetadataUnavailable;
                    results.Add(new ImportItemResult(item.Path, item.Classification, true, false,
                        MatchStatus: staged?.MatchStatus,
                        Failure: staged?.Failure,
                        Status: initialStatus,
                        MetadataFailureKind: staged?.MetadataFailureKind
                            ?? (needsMetadata && !canAttemptMetadata && staged is null
                                ? MetadataFailureKind.NotConfigured : null),
                        AlreadyRegistered: registration.AlreadyRegistered));

                    if (!tvShows.TryGetValue(registration.Show.Id, out var pending))
                    {
                        pending = new TvImportGroup(
                            registration.Show,
                            item.ParsedMedia.Year,
                            canAttemptMetadata && requiresMatch,
                            canAttemptMetadata && requiresRefresh,
                            registration.Show.MetadataStatus,
                            staged);
                        tvShows.Add(registration.Show.Id, pending);
                    }
                    else
                    {
                        pending.ShouldMatch |= canAttemptMetadata && requiresMatch;
                        pending.ShouldRefresh |= canAttemptMetadata && requiresRefresh;
                        pending.HasConflictingManualMatches |=
                            HasConflictingManualMatches(pending.StagedPreview, staged);
                        pending.StagedPreview = PreferStagedPreview(pending.StagedPreview, staged);
                    }

                    pending.ResultIndexes.Add(resultIndex);
                    if (canAttemptMetadata)
                    {
                        pending.EnrichmentResultIndexes.Add(resultIndex);
                    }
                    else if (requiresMatch && staged is null)
                    {
                        TrySetTvStatus(registration.Show.Id, MetadataStatus.Failed, registration.Show.MetadataStatus);
                    }
                }
                else
                {
                    results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                        Failure: "Only resolved movie and TV episode candidates can be imported.",
                        Status: ImportItemStatus.RegistrationFailed));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception error)
            {
                results.Add(new ImportItemResult(item.Path, item.Classification, false, false,
                    Failure: error.Message,
                    Status: ImportItemStatus.RegistrationFailed));
            }
        }

        // All selected items have been registered locally; report the final count so the UI shows the
        // completed total before the (fast, staged-preview) metadata phase runs.
        progress?.Invoke(new ImportProgress(selections.Count, selections.Count));

        // Apply staged TV terminal states only after every episode in a grouped show is registered.
        // Updating the show inside the registration loop would change its metadata status before a
        // later episode's optimistic match can use the original expected status. This also runs when
        // the provider is unavailable, keeping persisted state truthful; cancellation remains pending
        // for a future retry.
        foreach (var pending in tvShows.Values)
        {
            if (pending.ShouldMatch || pending.ExpectedMetadataStatus == MetadataStatus.Ready)
            {
                continue;
            }

            switch (pending.StagedPreview?.Status)
            {
                case ImportItemStatus.NeedsMatch:
                case ImportItemStatus.NoMatchFound:
                    TrySetTvStatus(pending, MetadataStatus.NeedsMatch);
                    break;
                case ImportItemStatus.MetadataUnavailable:
                case ImportItemStatus.MetadataError:
                    TrySetTvStatus(pending, MetadataStatus.Failed);
                    break;
            }
        }

        if (_matching is not null && _metadataProvider.IsConfigured)
        {
            for (var movieIndex = 0; movieIndex < moviesPending.Count; movieIndex++)
            {
                var pending = moviesPending[movieIndex];
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // A staged candidate already passed the shared eligibility rule during the review pass,
                    // so it is applied without a second search. Preserve its MatchStatus: a manual or clear
                    // 0.80+ match stays Matched (no Review Match), while a 0.75-0.80 acceptance stays
                    // ReviewRequired so the persisted Review Match flag is set below.
                    var staged = pending.StagedPreview;
                    var decision = staged?.MovieCandidate is not null
                        ? new MatchDecision(
                            staged.MatchStatus ?? MatchStatus.Matched,
                            staged.MovieCandidate,
                            staged.MatchStatus == MatchStatus.ReviewRequired ? MetadataScorer.BulkAcceptThreshold : 1.0,
                            System.Collections.Immutable.ImmutableArray<MatchReason>.Empty,
                            System.Collections.Immutable.ImmutableArray<CandidateScore>.Empty)
                        : await _matching.SearchMovieCandidatesAsync(pending.Title, pending.Year, cancellationToken);

                    // Shared eligibility, plus a guarded sole-result fallback: exactly one credible
                    // candidate below 0.75 (e.g. "Dune Part One" -> "Dune 2021") is accepted as a review
                    // match instead of being routed to manual review. Popularity/closeness never rescues;
                    // a competitor, a conflicting year or an unrelated title keeps it Needs Review.
                    var accept = ShouldBulkAccept(decision.Status, decision.Confidence);
                    var reviewMatch = decision.Status == MatchStatus.ReviewRequired;
                    var acceptCandidate = decision.Candidate;
                    if (!accept && staged?.MovieCandidate is null
                        && SoleCredibleFallback(decision, pending.Title, pending.Year) is { } fallback)
                    {
                        accept = true;
                        reviewMatch = true;
                        acceptCandidate = fallback;
                    }

                    if (accept)
                    {
                        if (acceptCandidate is null)
                        {
                            throw new MetadataServiceException(MetadataFailureKind.InvalidResponse);
                        }

                        // Duplicate-identity guard: if another movie already owns this TMDB identity, the
                        // file is the same logical movie in a different folder. Attach it to the existing
                        // movie as another media file and drop the just-created empty movie, instead of
                        // creating a second card (and colliding on UNIQUE(provider, external_id)). Root/scan
                        // order therefore cannot decide which copy "wins" or owns the metadata.
                        if (TryAssociateWithExistingMovie(pending, acceptCandidate, results))
                        {
                            continue;
                        }

                        results[pending.ResultIndex] = results[pending.ResultIndex] with
                        {
                            MatchStatus = decision.Status,
                            Status = ImportItemStatus.Matching
                        };
                        await _matching.MatchMovieAsync(
                            pending.MovieId,
                            acceptCandidate,
                            cancellationToken,
                            pending.ExpectedMetadataStatus,
                            reviewMatch: reviewMatch);
                        results[pending.ResultIndex] = results[pending.ResultIndex] with
                        {
                            AutoMatched = pending.StagedPreview?.IsManual != true,
                            Status = ImportItemStatus.Matched
                        };
                    }
                    else if (decision.Status == MatchStatus.ReviewRequired)
                    {
                        results[pending.ResultIndex] = results[pending.ResultIndex] with
                        {
                            MatchStatus = decision.Status,
                            Status = ImportItemStatus.NeedsMatch
                        };
                        TrySetMovieStatus(pending, MetadataStatus.NeedsMatch);
                        ReconcileMovieResult(pending.ResultIndex, pending.MovieId, results);
                    }
                    else
                    {
                        results[pending.ResultIndex] = results[pending.ResultIndex] with
                        {
                            MatchStatus = decision.Status,
                            Status = ImportItemStatus.NoMatchFound,
                            Failure = "No safe metadata match was found."
                        };
                        TrySetMovieStatus(pending, MetadataStatus.NeedsMatch);
                        ReconcileMovieResult(pending.ResultIndex, pending.MovieId, results);
                    }
                }
                catch (OperationCanceledException)
                {
                    for (var remaining = movieIndex; remaining < moviesPending.Count; remaining++)
                    {
                        var resultIndex = moviesPending[remaining].ResultIndex;
                        results[resultIndex] = results[resultIndex] with { Status = ImportItemStatus.MetadataCancelled };
                    }
                    break;
                }
                catch (Exception error) when (IsBestEffortMetadataFailure(error))
                {
                    var kind = GetMetadataFailureKind(error);
                    results[pending.ResultIndex] = results[pending.ResultIndex] with
                    {
                        Failure = SafeMetadataFailure(kind),
                        Status = ImportItemStatus.MetadataError,
                        MetadataFailureKind = kind
                    };
                    TrySetMovieStatus(pending, MetadataStatus.Failed);
                    ReconcileMovieResult(pending.ResultIndex, pending.MovieId, results);
                }
            }

            foreach (var pending in tvShows.Values)
            {
                if (pending.HasConflictingManualMatches)
                {
                    foreach (var index in pending.ResultIndexes)
                    {
                        results[index] = results[index] with
                        {
                            MatchStatus = MatchStatus.ReviewRequired,
                            Status = ImportItemStatus.NeedsMatch,
                            Failure = "The selected TV matches disagree for this show. Choose one match."
                        };
                    }

                    if (pending.ExpectedMetadataStatus != MetadataStatus.Ready)
                    {
                        TrySetTvStatus(pending, MetadataStatus.NeedsMatch);
                    }

                    ReconcileTvResults(pending, results);
                    continue;
                }

                if (pending.ShouldRefresh)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var refreshed = await _matching.RefreshTvShowMetadataAsync(
                            pending.Show.Id,
                            fetchEpisodes: true,
                            ct: cancellationToken);
                        if (refreshed is null)
                        {
                            throw new MetadataServiceException(MetadataFailureKind.NotFound);
                        }
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                AutoMatched = pending.StagedPreview?.IsManual != true,
                                Status = ImportItemStatus.Matched
                            };
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with { Status = ImportItemStatus.MetadataCancelled };
                        }
                        continue;
                    }
                    catch (Exception error) when (IsBestEffortMetadataFailure(error))
                    {
                        var kind = GetMetadataFailureKind(error);
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                Failure = SafeMetadataFailure(kind),
                                Status = ImportItemStatus.MetadataError,
                                MetadataFailureKind = kind
                            };
                        }
                        if (pending.ExpectedMetadataStatus != MetadataStatus.Ready)
                        {
                            TrySetTvStatus(pending, MetadataStatus.Failed);
                        }
                        ReconcileTvResults(pending, results);
                    }
                }

                if (!pending.ShouldMatch)
                {
                    continue;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var decision = pending.StagedPreview?.TvCandidate is not null
                        ? new TvMatchDecision(MatchStatus.Matched, pending.StagedPreview.TvCandidate, 1.0,
                            System.Collections.Immutable.ImmutableArray<MatchReason>.Empty,
                            System.Collections.Immutable.ImmutableArray<TvCandidateScore>.Empty)
                        : await _matching.SearchTvCandidatesAsync(pending.Show.Title, pending.Year, cancellationToken);
                    if (ShouldBulkAccept(decision.Status, decision.Confidence))
                    {
                        if (decision.Candidate is null)
                        {
                            throw new MetadataServiceException(MetadataFailureKind.InvalidResponse);
                        }
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                MatchStatus = decision.Status,
                                Status = ImportItemStatus.Matching
                            };
                        }
                        await _matching.MatchTvShowAsync(
                            pending.Show.Id,
                            decision.Candidate,
                            ct: cancellationToken,
                            expectedMetadataStatus: pending.ExpectedMetadataStatus);
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                AutoMatched = pending.StagedPreview?.IsManual != true,
                                Status = ImportItemStatus.Matched
                            };
                        }
                    }
                    else if (decision.Status == MatchStatus.ReviewRequired)
                    {
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                MatchStatus = decision.Status,
                                Status = ImportItemStatus.NeedsMatch
                            };
                        }
                        TrySetTvStatus(pending, MetadataStatus.NeedsMatch);
                        ReconcileTvResults(pending, results);
                    }
                    else
                    {
                        foreach (var index in pending.EnrichmentResultIndexes)
                        {
                            results[index] = results[index] with
                            {
                                MatchStatus = decision.Status,
                                Status = ImportItemStatus.NoMatchFound,
                                Failure = "No safe metadata match was found."
                            };
                        }
                        TrySetTvStatus(pending, MetadataStatus.NeedsMatch);
                        ReconcileTvResults(pending, results);
                    }
                }
                catch (OperationCanceledException)
                {
                    foreach (var index in pending.EnrichmentResultIndexes)
                    {
                        results[index] = results[index] with { Status = ImportItemStatus.MetadataCancelled };
                    }
                    continue;
                }
                catch (Exception error) when (IsBestEffortMetadataFailure(error))
                {
                    var kind = GetMetadataFailureKind(error);
                    foreach (var index in pending.EnrichmentResultIndexes)
                    {
                        results[index] = results[index] with
                        {
                            Failure = SafeMetadataFailure(kind),
                            Status = ImportItemStatus.MetadataError,
                            MetadataFailureKind = kind
                        };
                    }
                    TrySetTvStatus(pending, MetadataStatus.Failed);
                    ReconcileTvResults(pending, results);
                }
            }
        }

        return new ImportBatchResult(results);
    }

    /// <summary>
    /// The single Add Media eligibility rule, shared by every surface so they agree end to end: the review
    /// preview (which drives the displayed status, "Select ready to add", and the per-item metadata gate)
    /// and the bulk-add metadata application. A user-initiated import accepts the top candidate now instead
    /// of routing it to manual review when there is a clear 0.80+ winner, or any 0.75-0.80 best candidate —
    /// including a close/ambiguous tie, because the scorer already ranks the tied group by TMDB popularity,
    /// so the top candidate is the most-popular of equally-strong options. Popularity only breaks ties
    /// between strong candidates; it never rescues a below-0.75 match.
    /// </summary>
    private static bool ShouldBulkAccept(MatchStatus status, double confidence) =>
        status == MatchStatus.Matched
        || (status == MatchStatus.ReviewRequired && confidence >= MetadataScorer.BulkAcceptThreshold);

    /// <summary>
    /// The Add Media review status for a scored decision, derived from the same <see cref="ShouldBulkAccept" />
    /// rule. A bulk-acceptable candidate is surfaced as <see cref="ImportItemStatus.Matched" /> so it is
    /// offered as "ready to add" and reaches the metadata-application gate; the preview still carries the
    /// original <see cref="MatchStatus" />, so a 0.75-0.80 acceptance is later persisted with a Review Match
    /// flag. A below-0.75 review candidate stays <see cref="ImportItemStatus.NeedsMatch" /> for the user.
    /// </summary>
    private static ImportItemStatus PreviewStatusFor(MatchStatus status, double confidence, bool hasCandidate) =>
        hasCandidate && ShouldBulkAccept(status, confidence) ? ImportItemStatus.Matched
        : status == MatchStatus.ReviewRequired ? ImportItemStatus.NeedsMatch
        : ImportItemStatus.NoMatchFound;

    /// <summary>
    /// The single credible sole candidate to accept as a review match when a user-initiated Add has a
    /// below-0.75 decision, or null. It requires EXACTLY ONE candidate (no competitor) and delegates the
    /// title/year corroboration to <see cref="MetadataScorer.IsCorroboratedSoleCandidate" />, so a
    /// conflicting year or an unrelated title never qualifies. The caller persists the Review Match flag.
    /// </summary>
    private static MovieCandidate? SoleCredibleFallback(MatchDecision decision, string title, int? year)
    {
        if (decision.RankedCandidates.Length != 1)
        {
            return null;
        }

        var only = decision.RankedCandidates[0].Candidate;
        return MetadataScorer.IsCorroboratedSoleCandidate(title, year, only.Title, only.OriginalTitle, only.Year)
            ? only
            : null;
    }

    private static bool IsBestEffortMetadataFailure(Exception error) =>
        error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private static MetadataFailureKind GetMetadataFailureKind(Exception error) =>
        error is MetadataServiceException metadataError ? metadataError.Kind : MetadataFailureKind.Api;

    private static string SafeMetadataFailure(MetadataFailureKind kind) =>
        $"Metadata request failed ({kind}).";

    private static ImportMetadataPreview? PreferStagedPreview(
        ImportMetadataPreview? current,
        ImportMetadataPreview? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        if (current is null)
        {
            return incoming;
        }

        // A user-selected candidate is authoritative for the grouped show. Otherwise a safe matched
        // preview outranks an ambiguous/empty/error outcome from another episode in the same group.
        if (incoming.IsManual && incoming.Status == ImportItemStatus.Matched)
        {
            return incoming;
        }

        if (StagedPreviewPriority(incoming) > StagedPreviewPriority(current))
        {
            return incoming;
        }

        return current;
    }

    private static bool HasConflictingManualMatches(
        ImportMetadataPreview? current,
        ImportMetadataPreview? incoming) =>
        current is { IsManual: true, TvCandidate: not null }
        && incoming is { IsManual: true, TvCandidate: not null }
        && (!string.Equals(
                current.TvCandidate.Provider,
                incoming.TvCandidate.Provider,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                current.TvCandidate.ExternalId,
                incoming.TvCandidate.ExternalId,
                StringComparison.OrdinalIgnoreCase));

    private static int StagedPreviewPriority(ImportMetadataPreview preview) =>
        preview.IsManual && preview.Status == ImportItemStatus.Matched ? 5
        : preview.Status == ImportItemStatus.Matched ? 4
        : preview.Status is ImportItemStatus.MetadataError or ImportItemStatus.MetadataUnavailable ? 3
        : preview.Status is ImportItemStatus.NeedsMatch or ImportItemStatus.NoMatchFound ? 2
        : 1;

    private bool TrySetMovieStatus(int movieId, MetadataStatus status, MetadataStatus expectedStatus)
    {
        if (_matching is null)
        {
            return false;
        }

        try
        {
            _matching.SetMovieMetadataStatus(movieId, status, expectedStatus);
            return true;
        }
        catch (Exception error) when (IsBestEffortMetadataFailure(error))
        {
            return false;
        }
    }

    private bool TrySetMovieStatus(MovieImportWork pending, MetadataStatus status) =>
        TrySetMovieStatus(pending.MovieId, status, pending.ExpectedMetadataStatus);

    /// <summary>
    /// If a different movie already owns the confidently matched TMDB identity, attach this import's
    /// file(s) to that existing movie and delete the just-created placeholder movie, so the same movie in
    /// two folders becomes one Movie with multiple media files — never a second empty card. Returns true
    /// when the association happened. The existing movie keeps its single metadata record, poster, watch
    /// state and list membership: those are never touched, so they survive the association. The
    /// placeholder movie was created moments earlier in this batch and carries no user state, so removing
    /// it loses nothing. Only a confident (Matched) identity reaches here; uncertain matches never merge.
    /// </summary>
    private bool TryAssociateWithExistingMovie(MovieImportWork pending, MovieCandidate candidate, List<ImportItemResult> results)
    {
        if (string.IsNullOrWhiteSpace(candidate.Provider) || string.IsNullOrWhiteSpace(candidate.ExternalId))
        {
            return false;
        }

        using var uow = _uowFactory.Begin();
        var existing = uow.Movies.GetByExternalId(candidate.Provider, candidate.ExternalId);
        if (existing is null || existing.Id == pending.MovieId)
        {
            return false;
        }

        // Move every media file of the placeholder movie onto the existing logical movie. LinkToMovie
        // only re-points an unlinked file, so first delete the placeholder movie — media_files.movie_id is
        // ON DELETE SET NULL, which orphans (never deletes) its files — then link each orphaned file to the
        // existing movie. Each file stays a distinct media-file row (different paths/extensions remain
        // separate versions); only its owning movie changes. All of this is one transaction.
        var fileIds = uow.MediaFiles.GetByMovieId(pending.MovieId).Select(file => file.Id).ToList();
        uow.Movies.Delete(pending.MovieId);
        foreach (var fileId in fileIds)
        {
            uow.MediaFiles.LinkToMovie(fileId, existing.Id);
        }
        uow.Commit();

        results[pending.ResultIndex] = results[pending.ResultIndex] with
        {
            MatchStatus = MatchStatus.Matched,
            AutoMatched = pending.StagedPreview?.IsManual != true,
            Status = ImportItemStatus.Matched,
            Registered = true
        };
        return true;
    }

    private bool TrySetTvStatus(int showId, MetadataStatus status, MetadataStatus expectedStatus)
    {
        if (_matching is null)
        {
            return false;
        }

        try
        {
            _matching.SetTvShowMetadataStatus(showId, status, expectedStatus);
            return true;
        }
        catch (Exception error) when (IsBestEffortMetadataFailure(error))
        {
            return false;
        }
    }

    private bool TrySetTvStatus(TvImportGroup pending, MetadataStatus status) =>
        TrySetTvStatus(pending.Show.Id, status, pending.ExpectedMetadataStatus);

    private void ReconcileMovieResult(int resultIndex, int movieId, IList<ImportItemResult> results)
    {
        try
        {
            using var uow = _uowFactory.Begin();
            if (uow.Movies.GetById(movieId)?.MetadataStatus == MetadataStatus.Ready)
            {
                results[resultIndex] = results[resultIndex] with
                {
                    Status = ImportItemStatus.Matched,
                    Failure = null,
                    MetadataFailureKind = null
                };
            }
        }
        catch (Exception error) when (IsBestEffortMetadataFailure(error))
        {
        }
    }

    private void ReconcileTvResults(TvImportGroup pending, IList<ImportItemResult> results)
    {
        // A refresh for an already matched show is best-effort enrichment for the new
        // episode; a service failure there must remain visible as an error. Reconciliation
        // is only meaningful for a show that was expected to be unmatched and may have
        // been manually matched while the automatic request was in flight.
        if (pending.ExpectedMetadataStatus == MetadataStatus.Ready)
        {
            return;
        }

        try
        {
            using var uow = _uowFactory.Begin();
            if (uow.TvShows.GetById(pending.Show.Id)?.MetadataStatus == MetadataStatus.Ready)
            {
                foreach (var index in pending.EnrichmentResultIndexes)
                {
                    results[index] = results[index] with
                    {
                        Status = ImportItemStatus.Matched,
                        Failure = null,
                        MetadataFailureKind = null
                    };
                }
            }
        }
        catch (Exception error) when (IsBestEffortMetadataFailure(error))
        {
        }
    }

    private sealed record MovieImportWork(
        int ResultIndex,
        int MovieId,
        string Title,
        int? Year,
        MetadataStatus ExpectedMetadataStatus,
        ImportMetadataPreview? StagedPreview = null);

    private sealed class TvImportGroup
    {
        public TvImportGroup(
            TvShow show,
            int? year,
            bool shouldMatch,
            bool shouldRefresh,
            MetadataStatus expectedMetadataStatus,
            ImportMetadataPreview? stagedPreview = null)
        {
            Show = show;
            Year = year;
            ShouldMatch = shouldMatch;
            ShouldRefresh = shouldRefresh;
            ExpectedMetadataStatus = expectedMetadataStatus;
            StagedPreview = stagedPreview;
        }

        public TvShow Show { get; }
        public int? Year { get; }
        public List<int> ResultIndexes { get; } = [];
        public List<int> EnrichmentResultIndexes { get; } = [];
        public bool ShouldMatch { get; set; }
        public bool ShouldRefresh { get; set; }
        public ImportMetadataPreview? StagedPreview { get; set; }
        public bool HasConflictingManualMatches { get; set; }
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
            return new MovieFileIngestionResult(existingMovie, existingFile, AlreadyRegistered: true);
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

                // Compare the show by its immutable local identity, not by its display sort_title.
                // Metadata matching rewrites sort_title, so a re-import of the same file must resolve the
                // filename alias to the owning show id rather than string-match the (now canonical) title.
                var aliasShow = uow.TvShows.GetByAlias(showData.SortTitle)
                    ?? uow.TvShows.GetBySortTitle(showData.SortTitle);
                var isSameEpisode = ownerSeason.Number == seasonNumber
                    && ownerEpisode.Number == episodeNumber
                    && aliasShow is not null
                    && aliasShow.Id == ownerShow.Id;

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

        // Resolve the logical show by its immutable local import alias first, then by the current
        // sort_title (for shows created before aliases existed), and create it only when neither
        // matches. The filename-derived alias is then recorded idempotently so a later provider match
        // that rewrites title/sort_title cannot orphan the next episode arriving under this filename.
        var show = uow.TvShows.GetByAlias(showData.SortTitle)
            ?? uow.TvShows.GetBySortTitle(showData.SortTitle);
        if (show is null)
        {
            show = uow.TvShows.Create(showData, now);
        }

        uow.TvShows.RegisterAlias(show.Id, showData.SortTitle, now);

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
