using System.Collections.Concurrent;
using System.Text;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;

namespace DropSort.Application.Services;

public sealed class OperationHistoryService : IOperationHistoryUiActions
{
    private readonly IFileOperationStore _store;
    private readonly IFileOperationCoordinator? _coordinator;
    private readonly IMediaFileRepository? _mediaFiles;
    private readonly IMovieRepository? _movies;
    private readonly ConcurrentDictionary<string, PreparedUndo> _undoPreviews = new(StringComparer.Ordinal);

    public OperationHistoryService(
        IFileOperationStore store,
        IFileOperationCoordinator? coordinator = null,
        IMediaFileRepository? mediaFiles = null,
        IMovieRepository? movies = null)
    {
        _store = store;
        _coordinator = coordinator;
        _mediaFiles = mediaFiles;
        _movies = movies;
    }

    public IReadOnlyList<OperationHistoryItem> ListOperationHistory(OperationHistoryQuery? query = null)
    {
        var request = query ?? new OperationHistoryQuery();
        if (request.Limit <= 0 || request.Offset < 0) throw new ArgumentOutOfRangeException(nameof(query));
        return _store.ListHistory(request.Limit, request.Offset).Select(MapHistory).ToList();
    }

    public void SaveOperationHistory(IReadOnlyList<OperationHistoryItem> items, string path)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Export path is required.", nameof(path));
        var builder = new StringBuilder("DropSort Operations Log").AppendLine().AppendLine();
        foreach (var item in items)
        {
            builder.Append(item.Type).Append(" - ").AppendLine(item.State.ToString());
            builder.AppendLine(string.IsNullOrWhiteSpace(item.MovieTitle) ? "Unlinked media operation" : item.MovieTitle);
            builder.Append("From: ").AppendLine(item.SourcePath ?? "Unknown");
            builder.Append("To: ").AppendLine(item.DestinationPath ?? "Unknown");
            builder.Append("Timestamp: ").AppendLine(item.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            builder.Append("Operation ID: ").AppendLine(item.Id).AppendLine();
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public OperationDetails GetOperationDetails(string operationId)
    {
        var record = Require(operationId);
        var reversedBy = _store.ListHistory(int.MaxValue, 0)
            .FirstOrDefault(item => item.ReversesOperationId == record.Id)?.Id;
        return new OperationDetails(record, ResolveMovieTitle(record), reversedBy);
    }

    public UndoPreview PrepareUndo(string operationId)
    {
        RequireCoordinator();
        var record = Require(operationId);
        if (record.State != OperationState.Committed) throw new InvalidOperationException("Only committed operations can be reversed.");
        if (record.MediaFileId is null) throw new InvalidOperationException("Operation is not linked to a media file.");
        if (_store.ListHistory(int.MaxValue, 0).Any(item => item.ReversesOperationId == record.Id))
            throw new InvalidOperationException("Operation has already been reversed.");
        var media = _mediaFiles?.GetById(record.MediaFileId.Value)
            ?? throw new InvalidOperationException("Current media-file state is unavailable.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(media.CurrentPath), Path.GetFullPath(record.Destination)))
            throw new InvalidOperationException("Catalog path no longer matches the committed destination.");
        var roots = HistoricalRoots(record);
        var identity = new SourceIdentity(
            record.DestinationSize ?? throw new InvalidOperationException("Destination verification is incomplete."),
            record.DestinationMTimeNs ?? throw new InvalidOperationException("Destination verification is incomplete."),
            record.DestinationDev ?? throw new InvalidOperationException("Destination verification is incomplete."),
            record.DestinationIno ?? throw new InvalidOperationException("Destination verification is incomplete."));
        var previewId = Guid.NewGuid().ToString("D");
        var previewPlan = new FileOperationPlan(
            $"preview-{previewId}", record.OperationType, record.Destination, record.Source,
            record.MediaFileId, record.Id);
        _undoPreviews[previewId] = new PreparedUndo(record.Id, previewPlan, roots, identity);
        return new UndoPreview(previewId, previewPlan);
    }

    public UndoResult ConfirmUndo(string previewId)
    {
        var coordinator = RequireCoordinator();
        if (!_undoPreviews.TryRemove(previewId, out var prepared))
            throw new KeyNotFoundException("Undo preview is no longer available.");
        var current = Require(prepared.OriginalOperationId);
        if (current.State != OperationState.Committed ||
            _store.ListHistory(int.MaxValue, 0).Any(item => item.ReversesOperationId == current.Id))
            throw new InvalidOperationException("Undo preview became stale.");
        var (plan, _) = coordinator.Plan(
            current.OperationType,
            prepared.Plan.Source,
            prepared.Plan.Destination,
            prepared.Roots,
            current.MediaFileId,
            current.Id,
            prepared.Identity);
        return new UndoResult(coordinator.Execute(plan.OperationId, prepared.Roots));
    }

    public void DiscardUndoPreview(string previewId) => _undoPreviews.TryRemove(previewId, out _);

    public RecoveryAssessment InspectRecovery(string operationId)
    {
        var coordinator = RequireCoordinator();
        var record = Require(operationId);
        var inspection = coordinator.InspectRecovery(operationId, HistoricalRoots(record));
        return new RecoveryAssessment(record, inspection.Situation, inspection.Message);
    }

    public RecoveryResult AttemptRecovery(string operationId)
    {
        var coordinator = RequireCoordinator();
        var record = Require(operationId);
        var recovered = coordinator.Recover(operationId, HistoricalRoots(record));
        return new RecoveryResult(recovered, recovered.State);
    }

    private OperationHistoryItem MapHistory(FileOperationRecord record) => new(
        record.Id,
        record.OperationType,
        ResolveMovieTitle(record),
        record.State,
        record.CreatedAt,
        record.Source,
        record.Destination,
        record.MediaFileId,
        record.UpdatedAt,
        record.ReversesOperationId);

    private string ResolveMovieTitle(FileOperationRecord record)
    {
        if (record.MediaFileId is null || _mediaFiles is null || _movies is null) return "Unlinked media operation";
        var media = _mediaFiles.GetById(record.MediaFileId.Value);
        return media?.MovieId is int movieId
            ? _movies.GetById(movieId)?.Title ?? "Unlinked media operation"
            : "Unlinked media operation";
    }

    private FileOperationRecord Require(string id) =>
        _store.GetById(id) ?? throw new KeyNotFoundException($"Operation {id} was not found.");

    private IFileOperationCoordinator RequireCoordinator() =>
        _coordinator ?? throw new InvalidOperationException("File-operation coordination is not configured.");

    private static IReadOnlyList<string> HistoricalRoots(FileOperationRecord record) =>
        new[] { Path.GetDirectoryName(record.Source)!, Path.GetDirectoryName(record.Destination)! }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private sealed record PreparedUndo(
        string OriginalOperationId,
        FileOperationPlan Plan,
        IReadOnlyList<string> Roots,
        SourceIdentity Identity);
}
