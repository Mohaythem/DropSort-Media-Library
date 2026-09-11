using System;
using DropSort.Domain.Media.Parser;

namespace DropSort.Domain.Media.Discovery;

public enum DiscoveryClassification
{
    MovieCandidate,

    /// <summary>
    /// An episode file whose name carries a season and episode the parser recognized, so it can be
    /// registered into the TV hierarchy.
    /// </summary>
    TvEpisodeCandidate,

    /// <summary>
    /// An episode file the parser could not resolve - no recognizable season / episode marker, or no
    /// show name left once the marker was removed. It is reported, never guessed at.
    /// </summary>
    TvEpisodeSkipped,
    UnknownMedia,
    Error
}

public enum DiscoveryErrorCode
{
    RootMissing,
    RootNotDirectory,
    RootLinkNotAllowed,
    PermissionDenied,
    Disappeared,
    DirectoryReadFailed,
    StatFailed,
    LinkSkipped,
    LoopSkipped,
    ParseFailed
}

public record DiscoveryProgress(int TotalFiles, int ProcessedFiles, int Errors, string? CurrentPath);

public record DiscoveryIssue
{
    public DiscoveryErrorCode Code { get; }
    public string Message { get; }

    public DiscoveryIssue(DiscoveryErrorCode code, string message)
    {
        if (!Enum.IsDefined(typeof(DiscoveryErrorCode), code)) throw new ArgumentException("Invalid code", nameof(code));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("message must not be empty", nameof(message));

        Code = code;
        Message = message;
    }
}

public record DiscoveredMedia
{
    public string Path { get; }
    public long? FileSize { get; }
    public ParsedMedia? ParsedMedia { get; }
    public DiscoveryClassification Classification { get; }
    public DiscoveryIssue? Issue { get; }

    public DiscoveredMedia(
        string path,
        long? fileSize,
        ParsedMedia? parsedMedia,
        DiscoveryClassification classification,
        DiscoveryIssue? issue)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
            throw new ArgumentException("path must be an absolute path");
        if (!Enum.IsDefined(typeof(DiscoveryClassification), classification))
            throw new ArgumentException("Invalid classification", nameof(classification));
        
        if (classification == DiscoveryClassification.Error)
        {
            if (issue == null) throw new ArgumentException("ERROR items require an issue");
            if (fileSize != null || parsedMedia != null) throw new ArgumentException("ERROR items cannot contain file facts");
        }
        else
        {
            if (issue != null) throw new ArgumentException("Non-error items cannot contain an issue");
            if (fileSize == null) throw new ArgumentException("file_size is required for non-error items");
            if (parsedMedia == null) throw new ArgumentException("parsed_media is required for non-error items");
            
            if (classification == DiscoveryClassification.MovieCandidate && parsedMedia.MediaType != MediaType.Movie)
                throw new ArgumentException("MOVIE_CANDIDATE requires media type Movie");

            if (classification == DiscoveryClassification.TvEpisodeCandidate)
            {
                if (parsedMedia.MediaType != MediaType.TvEpisode)
                    throw new ArgumentException("TV_EPISODE_CANDIDATE requires media type TvEpisode");
                if (parsedMedia.SeasonNumber is null || parsedMedia.EpisodeNumber is null)
                    throw new ArgumentException("TV_EPISODE_CANDIDATE requires a season and an episode number");
                if (string.IsNullOrWhiteSpace(parsedMedia.Title))
                    throw new ArgumentException("TV_EPISODE_CANDIDATE requires a show title");
            }
        }

        Path = path;
        FileSize = fileSize;
        ParsedMedia = parsedMedia;
        Classification = classification;
        Issue = issue;
    }

    public static DiscoveredMedia Error(string path, DiscoveryIssue issue)
    {
        return new DiscoveredMedia(path, null, null, DiscoveryClassification.Error, issue);
    }
}
