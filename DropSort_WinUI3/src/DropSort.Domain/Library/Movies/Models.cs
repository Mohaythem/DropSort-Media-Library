using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DropSort.Domain.Library.Movies;

public enum MediaFileStatus
{
    Present,
    Missing
}

public enum MetadataStatus
{
    Pending,
    Ready,
    Failed,
    NeedsMatch
}

public record MediaFileStatusUpdate
{
    public int MediaFileId { get; }
    public string ExpectedPath { get; }
    public MediaFileStatus Status { get; }
    public DateTimeOffset ObservedAt { get; }

    public MediaFileStatusUpdate(int mediaFileId, string expectedPath, MediaFileStatus status, DateTimeOffset observedAt)
    {
        if (mediaFileId <= 0) throw new ArgumentOutOfRangeException(nameof(mediaFileId), "mediaFileId must be a positive integer");
        if (string.IsNullOrWhiteSpace(expectedPath)) throw new ArgumentException("expectedPath must be a non-empty string", nameof(expectedPath));
        if (System.IO.Path.IsPathRooted(expectedPath) == false) throw new ArgumentException("expectedPath must be an absolute path", nameof(expectedPath));
        if (!Enum.IsDefined(typeof(MediaFileStatus), status)) throw new ArgumentException("Invalid status", nameof(status));

        MediaFileId = mediaFileId;
        ExpectedPath = expectedPath;
        Status = status;
        ObservedAt = observedAt;
    }
}

public record MovieCatalogData
{
    public string? Provider { get; }
    public string? ExternalId { get; }
    public string Title { get; }
    public string? OriginalTitle { get; }
    public int? Year { get; }
    public string? Overview { get; }
    public ImmutableArray<string> Genres { get; }
    public int? RuntimeMinutes { get; }
    public double? Rating { get; }
    public string? PosterReference { get; }
    public MetadataStatus MetadataStatus { get; }
    public string? BackdropReference { get; }
    public string? Tagline { get; }

    public MovieCatalogData(
        string? provider,
        string? externalId,
        string title,
        string? originalTitle,
        int? year,
        string? overview,
        ImmutableArray<string> genres,
        int? runtimeMinutes,
        double? rating,
        string? posterReference,
        MetadataStatus metadataStatus = MetadataStatus.Ready,
        string? backdropReference = null,
        string? tagline = null)
    {
        if ((provider == null) != (externalId == null))
            throw new ArgumentException("provider and external_id must both be populated or both be null");

        if (provider != null)
        {
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("provider must be a non-empty string");
            if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("external_id must be a non-empty string");
            Provider = provider.Trim();
            ExternalId = externalId.Trim();
        }

        if (!Enum.IsDefined(typeof(MetadataStatus), metadataStatus))
            throw new ArgumentException("metadata_status must be a valid MetadataStatus");

        if (metadataStatus == MetadataStatus.Ready && provider == null)
            throw new ArgumentException("READY metadata requires a populated external identity");
        
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title must be a non-empty string");
        
        if (originalTitle != null && string.IsNullOrWhiteSpace(originalTitle))
            throw new ArgumentException("original_title must be null or a non-empty string");

        if (year != null && (year < 1 || year > 9999))
            throw new ArgumentOutOfRangeException(nameof(year), "year must be an integer from 1 through 9999");
            
        if (overview != null && string.IsNullOrWhiteSpace(overview))
            throw new ArgumentException("overview must be null or a non-empty string");

        if (genres.IsDefault || genres.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("genres must contain non-empty strings");

        if (runtimeMinutes != null && runtimeMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeMinutes), "runtime_minutes must be a positive integer");

        if (rating != null && (double.IsNaN(rating.Value) || double.IsInfinity(rating.Value) || rating < 0.0 || rating > 10.0))
            throw new ArgumentOutOfRangeException(nameof(rating), "rating must be a finite number from 0 through 10");

        if (posterReference != null && string.IsNullOrWhiteSpace(posterReference))
            throw new ArgumentException("poster_reference must be null or a non-empty string");

        if (backdropReference != null && string.IsNullOrWhiteSpace(backdropReference))
            throw new ArgumentException("backdrop_reference must be null or a non-empty string");

        if (tagline != null && string.IsNullOrWhiteSpace(tagline))
            throw new ArgumentException("tagline must be null or a non-empty string");

        Title = title.Trim();
        OriginalTitle = originalTitle?.Trim();
        Year = year;
        Overview = overview?.Trim();
        Genres = genres.Select(g => g.Trim()).ToImmutableArray();
        RuntimeMinutes = runtimeMinutes;
        Rating = rating;
        PosterReference = posterReference?.Trim();
        MetadataStatus = metadataStatus;
        BackdropReference = backdropReference?.Trim();
        Tagline = tagline?.Trim();
    }
}

public record Movie
{
    public int Id { get; }
    public MovieCatalogData Data { get; }
    public DateTimeOffset DateAdded { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public Movie(int id, MovieCatalogData data, DateTimeOffset dateAdded, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "id must be a positive integer");
        
        Id = id;
        Data = data ?? throw new ArgumentNullException(nameof(data));
        DateAdded = dateAdded;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string? Provider => Data.Provider;
    public string? ExternalId => Data.ExternalId;
    public MetadataStatus MetadataStatus => Data.MetadataStatus;
    public string Title => Data.Title;
    public string? OriginalTitle => Data.OriginalTitle;
    public int? Year => Data.Year;
    public string? Overview => Data.Overview;
    public ImmutableArray<string> Genres => Data.Genres;
    public int? RuntimeMinutes => Data.RuntimeMinutes;
    public double? Rating => Data.Rating;
    public string? PosterReference => Data.PosterReference;
    public string? BackdropReference => Data.BackdropReference;
    public string? Tagline => Data.Tagline;
}

public record VerifiedMediaFileFacts
{
    public string CurrentPath { get; }
    public long FileSize { get; }
    public string Extension { get; }
    public string? Resolution { get; }
    public string? Codec { get; }
    public string? Source { get; }
    public DateTimeOffset ObservedAt { get; }

    public VerifiedMediaFileFacts(
        string currentPath,
        long fileSize,
        string extension,
        string? resolution,
        string? codec,
        string? source,
        DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || !System.IO.Path.IsPathRooted(currentPath))
            throw new ArgumentException("currentPath must be an absolute path");
        if (fileSize < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSize), "fileSize must be a non-negative integer");
        if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith('.') || extension.Length < 2)
            throw new ArgumentException("extension must include a leading dot");

        if (resolution != null && string.IsNullOrWhiteSpace(resolution)) throw new ArgumentException("resolution must be non-empty");
        if (codec != null && string.IsNullOrWhiteSpace(codec)) throw new ArgumentException("codec must be non-empty");
        if (source != null && string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source must be non-empty");

        CurrentPath = currentPath;
        FileSize = fileSize;
        Extension = extension;
        Resolution = resolution?.Trim();
        Codec = codec?.Trim();
        Source = source?.Trim();
        ObservedAt = observedAt;
    }
}

public record MediaFile
{
    public int Id { get; }
    public int? MovieId { get; }
    public string CurrentPath { get; }
    public long FileSize { get; }
    public string? Extension { get; }
    public string? Resolution { get; }
    public string? Codec { get; }
    public string? Source { get; }
    public MediaFileStatus Status { get; }
    public DateTimeOffset DiscoveredAt { get; }
    public DateTimeOffset LastSeenAt { get; }

    public MediaFile(
        int id,
        int? movieId,
        string currentPath,
        long fileSize,
        string? extension,
        string? resolution,
        string? codec,
        string? source,
        MediaFileStatus status,
        DateTimeOffset discoveredAt,
        DateTimeOffset lastSeenAt)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "id must be positive");
        if (movieId != null && movieId <= 0) throw new ArgumentOutOfRangeException(nameof(movieId), "movieId must be positive");
        if (string.IsNullOrWhiteSpace(currentPath) || !System.IO.Path.IsPathRooted(currentPath))
            throw new ArgumentException("currentPath must be an absolute path");
        if (fileSize < 0) throw new ArgumentOutOfRangeException(nameof(fileSize), "fileSize must be non-negative");
        
        if (extension != null && (!extension.StartsWith('.') || extension.Length < 2))
            throw new ArgumentException("extension must include a leading dot");

        if (resolution != null && string.IsNullOrWhiteSpace(resolution)) throw new ArgumentException("resolution must be non-empty");
        if (codec != null && string.IsNullOrWhiteSpace(codec)) throw new ArgumentException("codec must be non-empty");
        if (source != null && string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source must be non-empty");

        if (!Enum.IsDefined(typeof(MediaFileStatus), status)) throw new ArgumentException("Invalid status");

        Id = id;
        MovieId = movieId;
        CurrentPath = currentPath;
        FileSize = fileSize;
        Extension = extension;
        Resolution = resolution?.Trim();
        Codec = codec?.Trim();
        Source = source?.Trim();
        Status = status;
        DiscoveredAt = discoveredAt;
        LastSeenAt = lastSeenAt;
    }
}
