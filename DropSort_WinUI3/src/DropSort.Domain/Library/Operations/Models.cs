using System;
using DropSort.Domain.Core.Operations;

namespace DropSort.Domain.Library.Operations;

public record OperationJournalSnapshot
{
    public FileOperationRecord Record { get; }
    public string MovieTitle { get; }
    public string? CurrentCatalogPath { get; }
    public string? ReversedByOperationId { get; }

    public OperationJournalSnapshot(
        FileOperationRecord record,
        string movieTitle,
        string? currentCatalogPath,
        string? reversedByOperationId)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrWhiteSpace(movieTitle)) throw new ArgumentException("movieTitle cannot be empty");
        if (currentCatalogPath != null && !System.IO.Path.IsPathRooted(currentCatalogPath))
            throw new ArgumentException("currentCatalogPath must be absolute");
        if (reversedByOperationId != null && string.IsNullOrWhiteSpace(reversedByOperationId))
            throw new ArgumentException("reversedByOperationId cannot be empty");

        Record = record;
        MovieTitle = movieTitle;
        CurrentCatalogPath = currentCatalogPath;
        ReversedByOperationId = reversedByOperationId;
    }
}
