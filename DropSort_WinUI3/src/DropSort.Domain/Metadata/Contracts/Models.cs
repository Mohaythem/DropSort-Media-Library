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

    public MovieCandidate(
        string provider,
        string externalId,
        string title,
        string? originalTitle,
        int? year,
        string? overview,
        double? rating,
        string? posterReference)
    {
        Provider = provider;
        ExternalId = externalId;
        Title = title;
        OriginalTitle = originalTitle;
        Year = year;
        Overview = overview;
        Rating = rating;
        PosterReference = posterReference;
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
        string? posterReference)
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
    }
}
