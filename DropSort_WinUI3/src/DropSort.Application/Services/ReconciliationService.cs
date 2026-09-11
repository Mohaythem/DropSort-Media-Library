using System.Collections.Concurrent;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;

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
        foreach (var file in files)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var inspection = _inspector.Inspect(file.CurrentPath);
            checkedCount++;
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
            else
            {
                errors++;
            }
            progress?.Invoke(new LibraryReconciliationProgress(checkedCount, missingCount, 0, errors, file.CurrentPath));
        }
        return new LibraryReconciliationProgress(checkedCount, missingCount, 0, errors, null);
    }

    public RelinkPreview PrepareMediaRelink(int mediaFileId, string candidatePath)
    {
        var media = _mediaFiles.GetById(mediaFileId) ?? throw new KeyNotFoundException($"Media file {mediaFileId} was not found.");
        if (media.Status != MediaFileStatus.Missing)
            throw new InvalidOperationException("Only a missing media file can be relinked.");
        if (string.IsNullOrWhiteSpace(candidatePath) || !Path.IsPathFullyQualified(candidatePath))
            throw new ArgumentException("Candidate path must be absolute.", nameof(candidatePath));
        var canonical = Path.GetFullPath(candidatePath);
        var inspection = _inspector.Inspect(canonical);
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

    public RelinkResult ConfirmMediaRelink(string previewId)
    {
        if (!_relinks.TryRemove(previewId, out var prepared))
            throw new KeyNotFoundException("Relink preview is no longer available.");
        var current = _mediaFiles.GetById(prepared.MediaFileId) ?? throw new KeyNotFoundException("Media file was removed.");
        if (current.Status != MediaFileStatus.Missing ||
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(current.CurrentPath), prepared.ExpectedPath))
            throw new InvalidOperationException("Media record changed after relink preview.");
        var inspection = _inspector.Inspect(prepared.CandidatePath);
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
