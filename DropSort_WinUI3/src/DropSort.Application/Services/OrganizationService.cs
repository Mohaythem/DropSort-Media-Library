using System.Collections.Concurrent;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;

namespace DropSort.Application.Services;

public sealed class OrganizationService : IOrganizationUiActions
{
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    private readonly IMediaFileRepository _mediaFiles;
    private readonly IFileOperationCoordinator _coordinator;
    private readonly ConcurrentDictionary<string, PreparedOrganization> _previews = new(StringComparer.Ordinal);

    public OrganizationService(IMediaFileRepository mediaFiles, IFileOperationCoordinator coordinator)
    {
        _mediaFiles = mediaFiles;
        _coordinator = coordinator;
    }

    public OrganizationPreview PrepareOrganization(int mediaFileId, string destinationRoot, string destinationFilename)
    {
        var media = _mediaFiles.GetById(mediaFileId) ?? throw new KeyNotFoundException($"Media file {mediaFileId} was not found.");
        ValidateFilename(destinationFilename, Path.GetExtension(media.CurrentPath));
        if (string.IsNullOrWhiteSpace(destinationRoot) || !Path.IsPathFullyQualified(destinationRoot))
            throw new ArgumentException("Destination root must be absolute.", nameof(destinationRoot));
        var destination = Path.Combine(Path.GetFullPath(destinationRoot), destinationFilename);
        var operationType = StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetDirectoryName(media.CurrentPath), Path.GetDirectoryName(destination))
            ? OperationType.Rename
            : OperationType.Move;
        var roots = new[] { Path.GetDirectoryName(media.CurrentPath)!, Path.GetFullPath(destinationRoot) }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = _mediaFiles.GetByPath(destination);
        if (existing is not null && existing.Id != media.Id)
            throw new InvalidOperationException("Another media record owns the destination path.");
        var (plan, identity) = _coordinator.Preview(
            operationType, media.CurrentPath, destination, roots, media.Id);
        var previewId = Guid.NewGuid().ToString("D");
        _previews[previewId] = new PreparedOrganization(media.Id, plan, roots, identity);
        return new OrganizationPreview(previewId, plan);
    }

    public OrganizationResult ConfirmOrganization(string previewId)
    {
        if (!_previews.TryRemove(previewId, out var prepared))
            throw new KeyNotFoundException("Organization preview is no longer available.");
        var media = _mediaFiles.GetById(prepared.MediaFileId) ?? throw new KeyNotFoundException("Media file was removed.");
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(media.CurrentPath), prepared.PreviewPlan.Source))
            throw new InvalidOperationException("Catalog path changed after preview.");
        var owner = _mediaFiles.GetByPath(prepared.PreviewPlan.Destination);
        if (owner is not null && owner.Id != media.Id)
            throw new InvalidOperationException("Destination was claimed after preview.");
        var (plan, _) = _coordinator.Plan(
            prepared.PreviewPlan.OperationType,
            prepared.PreviewPlan.Source,
            prepared.PreviewPlan.Destination,
            prepared.Roots,
            prepared.MediaFileId,
            expectedSourceIdentity: prepared.Identity);
        return new OrganizationResult(_coordinator.Execute(plan.OperationId, prepared.Roots));
    }

    public void DiscardOrganizationPreview(string previewId) => _previews.TryRemove(previewId, out _);

    private static void ValidateFilename(string value, string sourceExtension)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new ArgumentException("Destination filename must be non-empty without edge whitespace.", nameof(value));
        if (value.Length > 255 || value.Any(character => character < 32 || "<>:\"/\\|?*".Contains(character)))
            throw new ArgumentException("Destination filename contains invalid Windows characters.", nameof(value));
        if (value.EndsWith('.') || value.EndsWith(' '))
            throw new ArgumentException("Destination filename cannot end with a dot or space.", nameof(value));
        if (ReservedNames.Contains(value.Split('.', 2)[0].TrimEnd(' ', '.')))
            throw new ArgumentException("Destination filename is a reserved Windows device name.", nameof(value));
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(value), sourceExtension))
            throw new ArgumentException("Destination filename must preserve the media extension.", nameof(value));
    }

    private sealed record PreparedOrganization(
        int MediaFileId,
        FileOperationPlan PreviewPlan,
        IReadOnlyList<string> Roots,
        SourceIdentity Identity);
}
