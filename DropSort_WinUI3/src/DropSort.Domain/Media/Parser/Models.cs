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

    public ParsedMedia(
        string originalName,
        MediaType mediaType,
        string? title,
        int? year,
        string? resolution,
        string? source,
        string? codec,
        string extension)
    {
        OriginalName = originalName;
        MediaType = mediaType;
        Title = title;
        Year = year;
        Resolution = resolution;
        Source = source;
        Codec = codec;
        Extension = extension;
    }
}
