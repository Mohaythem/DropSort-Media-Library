using System;
using System.IO;
using DropSort.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>
/// Single-instance ownership of a library database. The first holder takes an exclusive OS lock; a
/// second acquisition against the same database is refused with exactly the same semantics a second
/// process sees (a Windows share-mode lock blocks every other handle, in this process or another). This
/// is what stops a second DropSort process from running migration or recovery - and thus from marking an
/// in-flight file operation Failed - while the first process owns the library.
/// </summary>
public sealed class LibraryInstanceGuardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_guard_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public LibraryInstanceGuardTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "library.db")}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public void SecondOwner_IsRefusedWhileFirstHeld_ThenGrantedAfterRelease()
    {
        var first = LibraryInstanceGuard.TryAcquire(_connectionString);
        Assert.NotNull(first);

        // A second owner of the SAME library is refused - the exact guarantee that stops a second
        // process from migrating/recovering the database the first process owns.
        Assert.Null(LibraryInstanceGuard.TryAcquire(_connectionString));

        first!.Dispose();

        // Once the owner releases (or the process exits), the library can be owned again.
        var second = LibraryInstanceGuard.TryAcquire(_connectionString);
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public void DifferentDatabases_AreIndependentlyOwnable()
    {
        var otherConnection = $"Data Source={Path.Combine(_directory, "other.db")}";
        using var first = LibraryInstanceGuard.TryAcquire(_connectionString);
        using var other = LibraryInstanceGuard.TryAcquire(otherConnection);
        Assert.NotNull(first);
        Assert.NotNull(other);
    }

    [Fact]
    public void InMemoryDatabase_IsAlwaysGranted()
    {
        using var first = LibraryInstanceGuard.TryAcquire("Data Source=:memory:");
        Assert.NotNull(first);
    }
}
