using System;
using System.Collections.Immutable;

namespace DropSort.Domain.Metadata.Contracts;

public record MovieSearchQuery(string Title, int? Year = null, string? OriginalTitle = null, string? Provider = null);

public record MovieCandidate
{
    public string Provider { get; }
    public string ExternalId { get; }
    public string Title { get; }
    public string? OriginalTitle { get; }
    public int? Year { get; }
    public string? Overview { get; }
    public double? Rating { get; }
    public string? PosterReference { get; }
    public string? BackdropReference { get; }
    public string? Tagline { get; }

    public MovieCandidate(
        string provider,
        string externalId,
        string title,
        string? originalTitle,
        int? year,
        string? overview,
        double? rating,
        string? posterReference,
        string? backdropReference = null,
        string? tagline = null)
    {
        Provider = provider;
        ExternalId = externalId;
        Title = title;
        OriginalTitle = originalTitle;
        Year = year;
        Overview = overview;
        Rating = rating;
        PosterReference = posterReference;
        BackdropReference = backdropReference;
        Tagline = tagline;
    }
}

public record MovieMetadata
{
    public string Provider { get; }
    public string ExternalId { get; }
    public string Title { get; }
    public string? OriginalTitle { get; }
    public int? Year { get; }
    public string? Overview { get; }
    public ImmutableArray<string> Genres { get; }
    public int? RuntimeMinutes { get; }
    public double? Rating { get; }
    public string? PosterReference { get; }
    public string? BackdropReference { get; }
    public string? Tagline { get; }

    public MovieMetadata(
        string provider,
        string externalId,
        string title,
        string? originalTitle,
        int? year,
        string? overview,
        ImmutableArray<string> genres,
        int? runtimeMinutes,
        double? rating,
        string? posterReference,
        string? backdropReference = null,
        string? tagline = null)
    {
        Provider = provider;
        ExternalId = externalId;
        Title = title;
        OriginalTitle = originalTitle;
        Year = year;
        Overview = overview;
        Genres = genres;
        RuntimeMinutes = runtimeMinutes;
        Rating = rating;
        PosterReference = posterReference;
        BackdropReference = backdropReference;
        Tagline = tagline;
    }
}

public record TvSearchQuery(string Title, int? FirstAirYear = null, string? OriginalTitle = null, string? Provider = null);

public record TvCandidate(
    string Provider,
    string ExternalId,
    string Title,
    string? OriginalTitle,
    int? FirstAirYear,
    string? Overview,
    double? Rating,
    string? PosterReference,
    string? BackdropReference = null);

public record TvShowMetadata(
    string Provider,
    string ExternalId,
    string Title,
    string? OriginalTitle,
    int? FirstAirYear,
    string? Overview,
    ImmutableArray<string> Genres,
    double? Rating,
    string? PosterReference,
    string? BackdropReference,
    ImmutableArray<TvSeasonMetadata> Seasons,
    string? Tagline = null);

public record TvSeasonMetadata(
    int SeasonNumber,
    string? Title,
    string? Overview,
    string? PosterReference,
    string? AirDate,
    ImmutableArray<TvEpisodeMetadata> Episodes,
    string? ExternalId = null);

public record TvEpisodeMetadata(
    int EpisodeNumber,
    string? Title,
    string? Overview,
    int? RuntimeMinutes,
    string? AirDate,
    double? Rating,
    string? StillReference,
    string? ExternalId = null);
