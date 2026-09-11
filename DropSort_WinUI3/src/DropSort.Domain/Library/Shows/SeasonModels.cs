using System;

namespace DropSort.Domain.Library.Shows;

/// <summary>One season of a show. Season 0 is the specials slot, which is why zero is allowed.</summary>
public record TvSeason
{
    public int Id { get; }

    public int ShowId { get; }

    public int Number { get; }

    public string? Title { get; }

    public string? Overview { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string? PosterReference { get; }

    public string? AirDate { get; }

    public string? ExternalId { get; }

    public TvSeason(
        int id,
        int showId,
        int number,
        string? title,
        string? overview,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? posterReference = null,
        string? airDate = null,
        string? externalId = null)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "id must be a positive integer");
        }

        if (showId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(showId), "showId must be a positive integer");
        }

        ValidateSeasonNumber(number);

        if (title != null && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title must be null or a non-empty string", nameof(title));
        }

        if (overview != null && string.IsNullOrWhiteSpace(overview))
        {
            throw new ArgumentException("overview must be null or a non-empty string", nameof(overview));
        }

        if (posterReference != null && string.IsNullOrWhiteSpace(posterReference))
        {
            throw new ArgumentException("posterReference must be null or a non-empty string", nameof(posterReference));
        }

        if (airDate != null && string.IsNullOrWhiteSpace(airDate))
        {
            throw new ArgumentException("airDate must be null or a non-empty string", nameof(airDate));
        }

        if (externalId != null && string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("externalId must be null or a non-empty string", nameof(externalId));
        }

        Id = id;
        ShowId = showId;
        Number = number;
        Title = title?.Trim();
        Overview = overview?.Trim();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        PosterReference = posterReference?.Trim();
        AirDate = airDate?.Trim();
        ExternalId = externalId?.Trim();
    }

    /// <summary>
    /// The range the catalog accepts. A file name that claims a season outside it is treated as a parse
    /// failure rather than being registered against an invented season.
    /// </summary>
    public static void ValidateSeasonNumber(int number)
    {
        if (number is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "season number must be from 0 through 999");
        }
    }
}

/// <summary>One episode of a season.</summary>
public record TvEpisode
{
    public int Id { get; }

    public int SeasonId { get; }

    public int Number { get; }

    public string? Title { get; }

    public string? Overview { get; }

    public int? RuntimeMinutes { get; }

    public DateTimeOffset? AirDate { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string? StillReference { get; }

    public double? Rating { get; }

    public string? ExternalId { get; }

    public TvEpisode(
        int id,
        int seasonId,
        int number,
        string? title,
        string? overview,
        int? runtimeMinutes,
        DateTimeOffset? airDate,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? stillReference = null,
        double? rating = null,
        string? externalId = null)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "id must be a positive integer");
        }

        if (seasonId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonId), "seasonId must be a positive integer");
        }

        ValidateEpisodeNumber(number);

        if (title != null && string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title must be null or a non-empty string", nameof(title));
        }

        if (overview != null && string.IsNullOrWhiteSpace(overview))
        {
            throw new ArgumentException("overview must be null or a non-empty string", nameof(overview));
        }

        if (runtimeMinutes != null && runtimeMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeMinutes), "runtime_minutes must be positive");
        }

        if (stillReference != null && string.IsNullOrWhiteSpace(stillReference))
        {
            throw new ArgumentException("stillReference must be null or a non-empty string", nameof(stillReference));
        }

        if (rating != null && (double.IsNaN(rating.Value) || double.IsInfinity(rating.Value) || rating < 0.0 || rating > 10.0))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "rating must be a finite number from 0 through 10");
        }

        if (externalId != null && string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("externalId must be null or a non-empty string", nameof(externalId));
        }

        Id = id;
        SeasonId = seasonId;
        Number = number;
        Title = title?.Trim();
        Overview = overview?.Trim();
        RuntimeMinutes = runtimeMinutes;
        AirDate = airDate;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        StillReference = stillReference?.Trim();
        Rating = rating;
        ExternalId = externalId?.Trim();
    }

    /// <summary>The range the catalog accepts; see <see cref="TvSeason.ValidateSeasonNumber" />.</summary>
    public static void ValidateEpisodeNumber(int number)
    {
        if (number is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "episode number must be from 0 through 999");
        }
    }
}
