using System;
using System.Collections.Immutable;
using System.Linq;
using DropSort.Domain.Library.Movies;

namespace DropSort.Domain.Library.Shows;

/// <summary>
/// What the catalog can say about an episode's local media, derived only from the files registered for
/// it. A registered file that is gone from disk keeps its registration, so an episode is Missing rather
/// than unregistered - that is the product contract, and it is what Check Library reports.
/// </summary>
public enum EpisodeAvailability
{
    /// <summary>No media file has been registered for this episode yet.</summary>
    NoFile,

    /// <summary>At least one registered file is present on disk.</summary>
    Present,

    /// <summary>Every registered file for this episode is missing from disk.</summary>
    Missing,
}

/// <summary>
/// The catalog facts of a TV show. A show registered from file names carries a title and nothing else:
/// there is no metadata provider in this build, so every optional field stays null instead of being
/// guessed.
/// </summary>
public record TvShowCatalogData
{
    public string? Provider { get; }

    public string? ExternalId { get; }

    public string Title { get; }

    /// <summary>
    /// The normalized title the catalog matches on. Registration uses it to decide whether a file
    /// belongs to a show that already exists, so it must be derived the same way every time; see
    /// <see cref="NormalizeTitle" />.
    /// </summary>
    public string SortTitle { get; }

    public string? OriginalTitle { get; }

    public int? Year { get; }

    public string? Overview { get; }

    public ImmutableArray<string> Genres { get; }

    public string? PosterReference { get; }

    public MetadataStatus MetadataStatus { get; }

    public TvShowCatalogData(
        string? provider,
        string? externalId,
        string title,
        string? originalTitle = null,
        int? year = null,
        string? overview = null,
        ImmutableArray<string>? genres = null,
        string? posterReference = null,
        MetadataStatus metadataStatus = MetadataStatus.Pending)
    {
        if ((provider == null) != (externalId == null))
        {
            throw new ArgumentException("provider and external_id must both be populated or both be null");
        }

        if (provider != null)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentException("provider must be a non-empty string");
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                throw new ArgumentException("external_id must be a non-empty string");
            }

            Provider = provider.Trim();
            ExternalId = externalId.Trim();
        }

        if (!Enum.IsDefined(typeof(MetadataStatus), metadataStatus))
        {
            throw new ArgumentException("metadata_status must be a valid MetadataStatus");
        }

        if (metadataStatus == MetadataStatus.Ready && provider == null)
        {
            throw new ArgumentException("READY metadata requires a populated external identity");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title must be a non-empty string");
        }

        if (originalTitle != null && string.IsNullOrWhiteSpace(originalTitle))
        {
            throw new ArgumentException("original_title must be null or a non-empty string");
        }

        if (year != null && (year < 1 || year > 9999))
        {
            throw new ArgumentOutOfRangeException(nameof(year), "year must be an integer from 1 through 9999");
        }

        if (overview != null && string.IsNullOrWhiteSpace(overview))
        {
            throw new ArgumentException("overview must be null or a non-empty string");
        }

        var resolvedGenres = genres ?? ImmutableArray<string>.Empty;

        if (resolvedGenres.IsDefault || resolvedGenres.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("genres must contain non-empty strings");
        }

        if (posterReference != null && string.IsNullOrWhiteSpace(posterReference))
        {
            throw new ArgumentException("poster_reference must be null or a non-empty string");
        }

        Title = title.Trim();
        SortTitle = NormalizeTitle(Title);
        OriginalTitle = originalTitle?.Trim();
        Year = year;
        Overview = overview?.Trim();
        Genres = [.. resolvedGenres.Select(genre => genre.Trim())];
        PosterReference = posterReference?.Trim();
        MetadataStatus = metadataStatus;

        if (SortTitle.Length == 0)
        {
            throw new ArgumentException("title must contain at least one letter or digit", nameof(title));
        }
    }

    /// <summary>
    /// Folds a show title down to the key the catalog matches on: lower case, letters and digits only.
    /// "Breaking Bad", "breaking.bad" and "Breaking  Bad" are the same show; anything that differs by a
    /// real word is not.
    /// </summary>
    public static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(title.Length);

        foreach (var character in title)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

/// <summary>A registered TV show. <see cref="Id" /> is the catalog's stable identity.</summary>
public record TvShow
{
    public int Id { get; }

    public TvShowCatalogData Data { get; }

    public DateTimeOffset DateAdded { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public TvShow(
        int id,
        TvShowCatalogData data,
        DateTimeOffset dateAdded,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "id must be a positive integer");
        }

        Id = id;
        Data = data ?? throw new ArgumentNullException(nameof(data));
        DateAdded = dateAdded;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string Title => Data.Title;

    public string SortTitle => Data.SortTitle;

    public int? Year => Data.Year;

    public string? Overview => Data.Overview;

    public ImmutableArray<string> Genres => Data.Genres;

    public string? PosterReference => Data.PosterReference;

    public MetadataStatus MetadataStatus => Data.MetadataStatus;
}
