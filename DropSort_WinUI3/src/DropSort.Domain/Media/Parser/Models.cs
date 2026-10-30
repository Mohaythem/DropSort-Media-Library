using System;

namespace DropSort.Domain.Media.Parser;

public enum MediaType
{
    Movie,
    TvEpisode,
    Unknown
}

public record ParsedMedia
{
    public string OriginalName { get; }
    public MediaType MediaType { get; }
    public string? Title { get; }
    public int? Year { get; }
    public string? Resolution { get; }
    public string? Source { get; }
    public string? Codec { get; }
    public string Extension { get; }

    /// <summary>
    /// The season the file name claims, for an episode. Null when the name carries no season / episode
    /// marker the parser recognizes - which is what makes the item unresolved rather than registerable.
    /// </summary>
    public int? SeasonNumber { get; }

    /// <summary>The episode the file name claims; see <see cref="SeasonNumber" />.</summary>
    public int? EpisodeNumber { get; }

    public ParsedMedia(
        string originalName,
        MediaType mediaType,
        string? title,
        int? year,
        string? resolution,
        string? source,
        string? codec,
        string extension,
        int? seasonNumber = null,
        int? episodeNumber = null)
    {
        if (seasonNumber is not null && seasonNumber is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonNumber), "season number must be from 0 through 999");
        }

        if (episodeNumber is not null && episodeNumber is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(episodeNumber), "episode number must be from 0 through 999");
        }

        if (mediaType == MediaType.TvEpisode && (seasonNumber is null) != (episodeNumber is null))
        {
            throw new ArgumentException("an episode carries both a season and an episode number, or neither");
        }

        OriginalName = originalName;
        MediaType = mediaType;
        Title = title;
        Year = year;
        Resolution = resolution;
        Source = source;
        Codec = codec;
        Extension = extension;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
    }
}
