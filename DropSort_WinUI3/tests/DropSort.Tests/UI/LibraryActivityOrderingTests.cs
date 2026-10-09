using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class LibraryActivityOrderingTests
{
    private sealed record Row(int Id, DateTimeOffset DateAdded, DateTimeOffset? LastOpenedAt);

    [Fact]
    public void ByRecent_UsesLastOpenedWhenPresent_AndDateAddedAsFallback()
    {
        var rows = new[]
        {
            new Row(1, At(30), null),
            new Row(2, At(10), At(40)),
            new Row(3, At(20), null),
        };

        var ordered = LibraryActivityOrdering.ByRecent(
            rows,
            row => row.LastOpenedAt,
            row => row.DateAdded,
            row => row.Id).Select(row => row.Id).ToArray();

        Assert.Equal([2, 1, 3], ordered);
    }

    [Fact]
    public void ByRecent_UsesIdAsDeterministicTieBreaker_AndDoesNotGroupMetadata()
    {
        var rows = new[]
        {
            new Row(1, At(10), At(20)),
            new Row(2, At(10), At(20)),
            new Row(3, At(30), At(5)),
        };

        var ordered = LibraryActivityOrdering.ByRecent(
            rows,
            row => row.LastOpenedAt,
            row => row.DateAdded,
            row => row.Id).Select(row => row.Id).ToArray();

        Assert.Equal([2, 1, 3], ordered);
    }

    [Fact]
    public void ByDateAdded_UsesCatalogDateAndIdTieBreaker_RegardlessOfOpenActivity()
    {
        var rows = new[]
        {
            new Row(1, At(10), At(90)),
            new Row(2, At(30), At(1)),
            new Row(3, At(30), null),
        };

        var ordered = LibraryActivityOrdering.ByDateAdded(
            rows,
            row => row.DateAdded,
            row => row.Id).Select(row => row.Id).ToArray();

        Assert.Equal([3, 2, 1], ordered);
    }

    private static DateTimeOffset At(int minute) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute);
}
