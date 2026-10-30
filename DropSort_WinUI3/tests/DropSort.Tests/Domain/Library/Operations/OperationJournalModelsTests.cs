using System;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Operations;
using Xunit;

namespace DropSort.Tests.Domain.Library.Operations;

public class OperationJournalModelsTests
{
    private FileOperationRecord CreateRecord()
    {
        return new FileOperationRecord(
            Id: "operation-1",
            OperationType: OperationType.Move,
            Source: "source.mkv",
            Destination: "destination.mkv",
            State: OperationState.Committed,
            MediaFileId: 1,
            ReversesOperationId: null,
            SourceSize: 1,
            SourceMTimeNs: 1,
            SourceDev: 1,
            SourceIno: 1,
            DestinationSize: 1,
            DestinationMTimeNs: 1,
            DestinationDev: 1,
            DestinationIno: 1,
            DestinationSha256: null,
            Strategy: "hardlink-unlink",
            ErrorCode: null,
            ErrorMessage: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow
        );
    }

    [Fact]
    public void SnapshotValidatesCatalogContext()
    {
        var path = System.IO.Path.GetFullPath("current.mkv");
        var snapshot = new OperationJournalSnapshot(CreateRecord(), "Movie", path, "reverse-1");
        
        Assert.Equal("Movie", snapshot.MovieTitle);

        Assert.Throws<ArgumentNullException>(() => new OperationJournalSnapshot(null!, "Movie", path, null));
        Assert.Throws<ArgumentException>(() => new OperationJournalSnapshot(CreateRecord(), "", null, null));
        Assert.Throws<ArgumentException>(() => new OperationJournalSnapshot(CreateRecord(), "Movie", "not-path", null));
        Assert.Throws<ArgumentException>(() => new OperationJournalSnapshot(CreateRecord(), "Movie", path, ""));
    }
}
