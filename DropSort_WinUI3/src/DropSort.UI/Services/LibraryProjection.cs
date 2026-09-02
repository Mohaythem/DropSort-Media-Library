using System.Globalization;
using DropSort.Application.Dto;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;

namespace DropSort.UI.Services;

/// <summary>
/// Maps the application layer's DTOs onto the records the views already bind to.
/// <para>
/// The pages keep their existing templates and bindings; only the source of the data changes, from
/// the demo tables to the SQLite catalog. Anything the backend cannot answer yet stays empty rather
/// than being invented - a movie registered from a file name has no runtime, rating or genres until
/// metadata enrichment exists, and <see cref="MovieRecord.MetaLine" /> simply omits those parts.
/// </para>
/// </summary>
internal static class LibraryProjection
{
    /// <summary>A card in the Library grid: identity, title and availability only.</summary>
    public static MovieRecord ToCard(MovieListItem item) => new(
        item.Id,
        item.Title,
        item.Year ?? 0,
        string.Empty,
        0,
        [],
        string.Empty,
        HasLocalFile: !item.IsMissing);

    /// <summary>
    /// The details hero, file block, personal state and watch history for one movie. The watch
    /// history keeps the event id in its label-free record so the remove button can call
    /// RemoveWatchEvent with the real id.
    /// </summary>
    public static MovieRecord ToDetails(
        MovieDetails details,
        PersonalMovieSnapshot personal,
        IReadOnlyList<WatchEvent> watchEvents)
    {
        var file = details.MediaFiles
            .OrderByDescending(candidate => candidate.Status == MediaFileStatus.Present)
            .FirstOrDefault();

        return new MovieRecord(
            details.Id,
            details.Title,
            details.Year ?? 0,
            details.RuntimeMinutes is { } minutes
                ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
                : string.Empty,
            details.Rating ?? 0,
            details.Genres,
            details.Overview ?? string.Empty,
            OriginalTitle: details.OriginalTitle,
            Preference: PreferenceKey(personal.Preference),
            InWatchlist: personal.Watchlisted,
            HasLocalFile: file is { Status: MediaFileStatus.Present },
            FileName: file is null ? null : Path.GetFileName(file.CurrentPath),
            FilePath: file?.CurrentPath,
            FileFacts: file is null ? null : FileFacts(file),
            MediaFileId: file?.Id,
            WatchHistory: ToWatchHistory(watchEvents));
    }

    /// <summary>
    /// The watch rows for one movie. <c>ListWatchEvents</c> answers newest first, so the oldest row is
    /// the first watch and every row above it is a rewatch. Both the initial load and every reload go
    /// through here, so the label cannot disagree between them.
    /// </summary>
    public static IReadOnlyList<WatchHistoryRecord> ToWatchHistory(IReadOnlyList<WatchEvent> events) =>
    [
        .. events.Select((watch, index) => new WatchHistoryRecord(
            LocalizationService.Digits(
                watch.WatchedAt.ToLocalTime().ToString("MMM d, yyyy", LocalizationService.Culture)),
            LocalizationService.Text(watch.Rewatch || index < events.Count - 1 ? "Rewatch" : "FirstWatch"),
            watch.Id)),
    ];

    /// <summary>"1080p · x264 · 4.2 GB" - only the facts the record actually carries.</summary>
    private static string FileFacts(MediaFile file)
    {
        var parts = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(file.Resolution))
        {
            parts.Add(file.Resolution);
        }

        if (!string.IsNullOrWhiteSpace(file.Codec))
        {
            parts.Add(file.Codec);
        }

        if (file.FileSize > 0)
        {
            parts.Add(FormatSize(file.FileSize));
        }

        return LocalizationService.Digits(string.Join(" · ", parts));
    }

    private static string FormatSize(long bytes)
    {
        const double gigabyte = 1024d * 1024d * 1024d;
        const double megabyte = 1024d * 1024d;

        return bytes >= gigabyte
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / gigabyte:0.0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / megabyte:0} MB");
    }

    private static WatchHistoryRecord ToWatchHistory(WatchEvent watch, int index) => new(
        LocalizationService.Digits(watch.WatchedAt.ToLocalTime().ToString("MMM d, yyyy", LocalizationService.Culture)),
        LocalizationService.Text(watch.Rewatch || index > 0 ? "Rewatch" : "FirstWatch"),
        watch.Id);

    private static string? PreferenceKey(PersonalPreference preference) => preference switch
    {
        PersonalPreference.Liked => "liked",
        PersonalPreference.Blacklisted => "blacklisted",
        _ => null,
    };
}
