using System;
using System.Collections.Generic;
using System.Linq;

namespace DropSort.UI.Services;

/// <summary>Pure ordering policy shared by the movie and TV library projections.</summary>
public static class LibraryActivityOrdering
{
    /// <summary>
    /// Orders a type's rows by recent activity. Rows without an open event use their catalog date,
    /// so a newly added row remains visible in Recent until it is opened. Metadata does not affect
    /// this ordering; the id is the deterministic final tie-breaker.
    /// </summary>
    public static IOrderedEnumerable<T> ByRecent<T>(
        IEnumerable<T> source,
        Func<T, DateTimeOffset?> lastOpenedAt,
        Func<T, DateTimeOffset> dateAdded,
        Func<T, int> id,
        bool descending = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lastOpenedAt);
        ArgumentNullException.ThrowIfNull(dateAdded);
        ArgumentNullException.ThrowIfNull(id);

        return descending
            ? source.OrderByDescending(row => lastOpenedAt(row) ?? dateAdded(row))
                .ThenByDescending(id)
            : source.OrderBy(row => lastOpenedAt(row) ?? dateAdded(row))
                .ThenBy(id);
    }

    /// <summary>Orders rows by their true catalog date, with the same stable tie-break policy.</summary>
    public static IOrderedEnumerable<T> ByDateAdded<T>(
        IEnumerable<T> source,
        Func<T, DateTimeOffset> dateAdded,
        Func<T, int> id,
        bool descending = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dateAdded);
        ArgumentNullException.ThrowIfNull(id);

        return descending
            ? source.OrderByDescending(dateAdded).ThenByDescending(id)
            : source.OrderBy(dateAdded).ThenBy(id);
    }
}
