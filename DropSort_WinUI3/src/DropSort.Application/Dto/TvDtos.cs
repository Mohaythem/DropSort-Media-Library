using System.Collections.Generic;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.Application.Dto;

/// <summary>A show as the library grid needs it: identity, title and how much of it is on disk.</summary>
public record TvShowListItem(
    int Id,
    string Title,
    int? Year,
    string? PosterReference,
    int SeasonCount,
    int EpisodeCount,
    int EpisodesWithLocalFile,
    int MissingEpisodeCount);

/// <summary>The whole hierarchy of one show, read in one call.</summary>
public record TvShowDetails(
    int Id,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    IReadOnlyList<string> Genres,
    string? PosterReference,
    IReadOnlyList<TvSeasonDetails> Seasons,
    string? ExternalId = null,
    MetadataStatus MetadataStatus = MetadataStatus.Pending);

public record TvSeasonDetails(
    int Id,
    int Number,
    string? Title,
    IReadOnlyList<TvEpisodeDetails> Episodes);

/// <summary>
/// One episode plus the registered files behind it. <see cref="Availability" /> is derived from those
/// files only: an episode whose file has disappeared reports Missing and stays registered.
/// </summary>
public record TvEpisodeDetails(
    int Id,
    int Number,
    string? Title,
    int? RuntimeMinutes,
    EpisodeAvailability Availability,
    IReadOnlyList<MediaFile> MediaFiles);

/// <summary>
/// The outcome of registering one episode file. Show, season and episode are the rows the file was
/// registered into - created if they did not exist, reused if they did.
/// </summary>
public record EpisodeFileIngestionResult(
    TvShow Show,
    TvSeason Season,
    TvEpisode Episode,
    MediaFile MediaFile,
    bool AlreadyRegistered);

/// <summary>
/// A request to register one local episode file. The parsed media has to carry a show title and both
/// numbers; anything less is refused by <see cref="Contracts.IImportUiActions.RegisterEpisodeImport" />
/// rather than guessed at.
/// </summary>
public record ConfirmEpisodeImportCommand(
    string FilePath,
    long FileSize,
    DropSort.Domain.Media.Parser.ParsedMedia ParsedMedia,
    System.DateTimeOffset ObservedAt);

/// <summary>Why an episode file could not be registered. Reported to the user as-is, never worked around.</summary>
public enum EpisodeRegistrationRefusal
{
    /// <summary>The file name carries no season / episode marker the parser recognizes.</summary>
    NoEpisodeMarker,

    /// <summary>Nothing was left to name the show once the marker and the release tags were removed.</summary>
    NoShowTitle,

    /// <summary>The claimed season or episode number is outside the range the catalog accepts.</summary>
    NumbersOutOfRange,

    /// <summary>The file is already registered as a movie's media file.</summary>
    AlreadyAMovieFile,

    /// <summary>The file is already registered against a different episode.</summary>
    OwnedByAnotherEpisode,
}

/// <summary>Thrown when an episode file cannot be registered; carries the reason for the UI to show.</summary>
public sealed class EpisodeRegistrationException(EpisodeRegistrationRefusal refusal, string message)
    : System.InvalidOperationException(message)
{
    public EpisodeRegistrationRefusal Refusal { get; } = refusal;
}
