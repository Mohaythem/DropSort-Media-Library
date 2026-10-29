using System.Collections.Concurrent;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Media;

namespace DropSort.Application.Services;

public sealed class ReconciliationService : IReconciliationUiActions
{
    private readonly IMediaFileRepository _mediaFiles;
    private readonly IAvailabilityInspector _inspector;
    private readonly IMovieRepository? _movies;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, PreparedRelink> _relinks = new(StringComparer.Ordinal);

    public ReconciliationService(
        IMediaFileRepository mediaFiles,
        IAvailabilityInspector inspector,
        IMovieRepository? movies = null,
        Func<DateTimeOffset>? clock = null)
    {
        _mediaFiles = mediaFiles;
        _inspector = inspector;
        _movies = movies;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public LibraryReconciliationProgress ReconcileLibraryFiles(
        Action<LibraryReconciliationProgress>? progress = null,
        Func<bool>? isCancelled = null)
    {
        var files = _mediaFiles.ListAll();
        var checkedCount = 0;
        var missingCount = 0;
        var errors = 0;
        var storageUnavailableCount = 0;

        // Evaluate storage availability once per distinct root, not once per file. A disconnected drive
        // with thousands of files is then a single cheap probe, and no file on it is inspected or written.
        var rootAvailable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.OrderBy(file => IsNetworkPath(file.CurrentPath)))
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            checkedCount++;

            var rootKey = StorageRootKey(file.CurrentPath);
            if (!rootAvailable.TryGetValue(rootKey, out var available))
            {
                available = _inspector.IsStorageAvailable(file.CurrentPath);
                rootAvailable[rootKey] = available;
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            }

            if (!available)
            {
                // Storage unavailable: not deleted media. Do not mark missing and do not rewrite the
                // path. The registration and its last-known state are preserved; reconnecting restores
                // availability on the next pass without re-registration. It is reported separately so a
                // completed check never implies an offline drive's files are available.
                storageUnavailableCount++;
                progress?.Invoke(new LibraryReconciliationProgress(checkedCount, missingCount, 0, errors, file.CurrentPath, storageUnavailableCount));
                continue;
            }

            var inspection = _inspector.Inspect(file.CurrentPath);
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            if (inspection.Status == AvailabilityInspectionStatus.Missing)
            {
                missingCount++;
                if (file.Status != MediaFileStatus.Missing) _mediaFiles.MarkMissing(file.Id, _clock());
            }
            else if (inspection.Status == AvailabilityInspectionStatus.Present)
            {
                if (inspection.Size is null) throw new InvalidDataException("Present inspection omitted file size.");
                _mediaFiles.RefreshVerifiedFacts(
                    file.Id,
                    new VerifiedMediaFileFacts(
                        file.CurrentPath,
                        inspection.Size.Value,
                        file.Extension ?? Path.GetExtension(file.CurrentPath),
                        file.Resolution,
                        file.Codec,
                        file.Source,
                        _clock()));
            }
            else if (inspection.Status == AvailabilityInspectionStatus.StorageUnavailable)
            {
                // A root that became unreachable between the batch probe and the per-file inspection.
                storageUnavailableCount++;
                rootAvailable[rootKey] = false;
            }
            else
            {
                // Error: inspection failed for an access/I/O reason. Non-destructive: leave the row as is.
                errors++;
            }
            progress?.Invoke(new LibraryReconciliationProgress(checkedCount, missingCount, 0, errors, file.CurrentPath, storageUnavailableCount));
        }
        return new LibraryReconciliationProgress(checkedCount, missingCount, 0, errors, null, storageUnavailableCount);
    }

    /// <summary>The storage root used to share one availability probe across every file on it.</summary>
    private static string StorageRootKey(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? path : root;
        }
        catch
        {
            return path;
        }
    }

    private static bool IsNetworkPath(string path) =>
        (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)) ||
        path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

    public RelinkPreview PrepareMediaRelink(int mediaFileId, string candidatePath) =>
        PrepareMediaRelink(mediaFileId, candidatePath, () => false);

    public RelinkPreview PrepareMediaRelink(int mediaFileId, string candidatePath, Func<bool> isCancelled)
    {
        if (isCancelled()) throw new OperationCanceledException();
        var media = _mediaFiles.GetById(mediaFileId) ?? throw new KeyNotFoundException($"Media file {mediaFileId} was not found.");
        if (media.Status != MediaFileStatus.Missing)
            throw new InvalidOperationException("Only a missing media file can be relinked.");
        if (string.IsNullOrWhiteSpace(candidatePath) || !Path.IsPathFullyQualified(candidatePath))
            throw new ArgumentException("Candidate path must be absolute.", nameof(candidatePath));
        var canonical = Path.GetFullPath(candidatePath);
        if (!MediaFormatPolicy.IsSupportedVideoPath(canonical))
            throw new InvalidOperationException($"Unsupported media extension: '{Path.GetExtension(canonical)}'.");
        var inspection = _inspector.Inspect(canonical);
        if (isCancelled()) throw new OperationCanceledException();
        if (inspection.Status != AvailabilityInspectionStatus.Present)
            throw new InvalidOperationException("Candidate file does not exist or is not accessible.");
        if (inspection.Size != media.FileSize)
            throw new InvalidOperationException($"Candidate file size ({inspection.Size:N0} bytes) does not match expected size ({media.FileSize:N0} bytes).");
        var owner = _mediaFiles.GetByPath(canonical);
        if (owner is not null && owner.Id != mediaFileId)
            throw new InvalidOperationException("Another media record already owns the candidate path.");
        var previewId = Guid.NewGuid().ToString("D");
        _relinks[previewId] = new PreparedRelink(media.Id, media.CurrentPath, canonical, media.FileSize);
        return new RelinkPreview(previewId, media.Id, canonical, true);
    }

    public RelinkResult ConfirmMediaRelink(string previewId) => ConfirmMediaRelink(previewId, () => false);

    public RelinkResult ConfirmMediaRelink(string previewId, Func<bool> isCancelled)
    {
        if (isCancelled()) throw new OperationCanceledException();
        if (!_relinks.TryRemove(previewId, out var prepared))
            throw new KeyNotFoundException("Relink preview is no longer available.");
        var current = _mediaFiles.GetById(prepared.MediaFileId) ?? throw new KeyNotFoundException("Media file was removed.");
        if (current.Status != MediaFileStatus.Missing ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(current.CurrentPath), prepared.ExpectedPath))
            throw new InvalidOperationException("Media record changed after relink preview.");
        if (!MediaFormatPolicy.IsSupportedVideoPath(prepared.CandidatePath))
            throw new InvalidOperationException($"Candidate file changed after relink preview: unsupported extension '{Path.GetExtension(prepared.CandidatePath)}'.");
        var inspection = _inspector.Inspect(prepared.CandidatePath);
        if (isCancelled()) throw new OperationCanceledException();
        if (inspection.Status != AvailabilityInspectionStatus.Present || inspection.Size != prepared.FileSize)
            throw new InvalidOperationException("Candidate file changed after relink preview.");
        var updated = _mediaFiles.Relink(
            current.Id,
            current.CurrentPath,
            new VerifiedMediaFileFacts(
                prepared.CandidatePath,
                prepared.FileSize,
                current.Extension ?? Path.GetExtension(prepared.CandidatePath),
                current.Resolution,
                current.Codec,
                current.Source,
                _clock()));
        return new RelinkResult(updated, null);
    }

    public void DiscardMediaRelinkPreview(string previewId) => _relinks.TryRemove(previewId, out _);

    public LibraryHealthProgress CheckLibrary(
        Action<LibraryHealthProgress>? progress = null,
        Func<bool>? isCancelled = null)
    {
        // The file stage is the slow one - it stats every registered path - so its progress is
        // forwarded as it happens. Reporting only the final result would leave a caller's progress
        // callback silent for the whole scan.
        var fileProgress = ReconcileLibraryFiles(
            progress is null
                ? null
                : files => progress(new LibraryHealthProgress(files, 0, 0, 0, 0, 0, 0, 0, [], [])),
            isCancelled);
        var movies = _movies?.ListAll() ?? [];
        var issues = new List<MetadataHealthItem>();
        var complete = 0;
        foreach (var movie in movies)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var missing = new List<MetadataHealthIssue>();
            if (string.IsNullOrWhiteSpace(movie.Overview)) missing.Add(MetadataHealthIssue.Overview);
            if (movie.RuntimeMinutes is null) missing.Add(MetadataHealthIssue.Runtime);
            if (movie.Genres.Length == 0) missing.Add(MetadataHealthIssue.Genres);
            if (movie.Year is null) missing.Add(MetadataHealthIssue.Year);
            if (string.IsNullOrWhiteSpace(movie.PosterReference)) missing.Add(MetadataHealthIssue.Poster);
            if (movie.MetadataStatus == MetadataStatus.NeedsMatch) missing.Add(MetadataHealthIssue.NeedsMatch);
            if (missing.Count == 0) complete++;
            else issues.Add(new MetadataHealthItem(movie.Id, movie.Title, MetadataHealthStatus.Incomplete, missing, [], null));
        }
        var result = new LibraryHealthProgress(
            fileProgress, movies.Count, movies.Count, complete, issues.Count, 0,
            issues.Count(item => item.MissingFields.Contains(MetadataHealthIssue.NeedsMatch)), 0,
            issues, []);
        progress?.Invoke(result);
        return result;
    }

    private sealed record PreparedRelink(int MediaFileId, string ExpectedPath, string CandidatePath, long FileSize);
}
