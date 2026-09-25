using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;
using DropSort.FileSystem.Inspection;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// Drive-aware availability. Storage that is unavailable (a disconnected or unmounted drive) must not be
/// treated as a deleted file. Reconciliation must leave such registrations intact, must not rewrite
/// their paths, must probe each root only once, and must restore availability automatically when the
/// storage returns. Only a reachable root with an absent file is "missing".
/// </summary>
public sealed class DriveAwareAvailabilityTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dropsort_avail_{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"dropsort_avail_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public DriveAwareAvailabilityTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); } catch { }
    }

    // ---- Real inspector classification ----

    [Fact]
    public void Inspector_present_missing_and_storage_unavailable_are_distinct()
    {
        var inspector = new AvailabilityInspector();

        var presentPath = Path.Combine(_workspace, "present.mkv");
        File.WriteAllBytes(presentPath, new byte[2048]);
        Assert.Equal(AvailabilityInspectionStatus.Present, inspector.Inspect(presentPath).Status);

        // Absent file on an available (existing) root is genuinely missing.
        var absentPath = Path.Combine(_workspace, "absent.mkv");
        Assert.Equal(AvailabilityInspectionStatus.Missing, inspector.Inspect(absentPath).Status);

        // A path on a drive letter that is not present is StorageUnavailable, not Missing.
        var offlinePath = Path.Combine(FreeDriveRoot(), "dropsort", "offline.mkv");
        Assert.Equal(AvailabilityInspectionStatus.StorageUnavailable, inspector.Inspect(offlinePath).Status);
        Assert.False(inspector.IsStorageAvailable(offlinePath));
        Assert.True(inspector.IsStorageAvailable(presentPath));
    }

    [Fact]
    public void Inspector_reports_missing_when_parent_absent_or_a_directory_on_an_available_root()
    {
        var inspector = new AvailabilityInspector();

        // Parent directory does not exist, but the drive root (the temp volume) is available.
        var absent = Path.Combine(_workspace, "no-such-dir", "file.mkv");
        Assert.Equal(AvailabilityInspectionStatus.Missing, inspector.Inspect(absent).Status);

        // A directory where a media file is expected means the file itself is not present.
        var directoryPath = Path.Combine(_workspace, "a-directory.mkv");
        Directory.CreateDirectory(directoryPath);
        Assert.Equal(AvailabilityInspectionStatus.Missing, inspector.Inspect(directoryPath).Status);
    }

    // ---- Reconciliation contract ----

    [Fact]
    public void UnavailableStorage_LeavesRegistrationsPresent_AndProbesEachRootOnce()
    {
        var offline = FreeDriveRoot();
        SeedMedia(1, offline + @"show\a.mkv");
        SeedMedia(2, offline + @"show\b.mkv");
        SeedMedia(3, offline + @"show\c.mkv");
        var inspector = new FakeInspector(unavailableRoots: [offline]);
        var reconciliation = new ReconciliationService(Media(), inspector);

        var progress = reconciliation.ReconcileLibraryFiles();

        Assert.Equal(3, progress.CheckedFiles);
        Assert.Equal(0, progress.MissingFiles);            // storage unavailable is never "missing"
        Assert.Equal(3, progress.StorageUnavailableFiles); // reported honestly, so a check is not "clean"
        Assert.Equal(1, inspector.RootProbes[offline]);    // one probe shared across the three files
        foreach (var id in new[] { 1, 2, 3 })
        {
            var media = Media().GetById(id)!;
            Assert.Equal(MediaFileStatus.Present, media.Status);           // registration preserved
            Assert.Equal(offline + @"show\" + (char)('a' + id - 1) + ".mkv", media.CurrentPath); // path not rewritten
        }
    }

    [Fact]
    public void AbsentFileOnAvailableStorage_IsMarkedMissing()
    {
        SeedMedia(1, Path.Combine(_workspace, "gone.mkv"));
        var inspector = new FakeInspector(absentPaths: [Path.Combine(_workspace, "gone.mkv")]);
        var reconciliation = new ReconciliationService(Media(), inspector);

        var progress = reconciliation.ReconcileLibraryFiles();

        Assert.Equal(1, progress.MissingFiles);
        Assert.Equal(MediaFileStatus.Missing, Media().GetById(1)!.Status);
    }

    [Fact]
    public void StorageReturns_RestoresAvailability_WithoutReregistration()
    {
        var offline = FreeDriveRoot();
        SeedMedia(1, offline + @"show\a.mkv");

        // Drive disconnected: stays Present, not missing.
        new ReconciliationService(Media(), new FakeInspector(unavailableRoots: [offline])).ReconcileLibraryFiles();
        Assert.Equal(MediaFileStatus.Present, Media().GetById(1)!.Status);

        // Drive reconnected and file present again: still Present, same registration.
        new ReconciliationService(Media(), new FakeInspector()).ReconcileLibraryFiles();
        Assert.Equal(MediaFileStatus.Present, Media().GetById(1)!.Status);
    }

    [Fact]
    public void AccessError_DoesNotBecomeMissing()
    {
        SeedMedia(1, Path.Combine(_workspace, "locked.mkv"));
        var inspector = new FakeInspector(errorPaths: [Path.Combine(_workspace, "locked.mkv")]);
        var reconciliation = new ReconciliationService(Media(), inspector);

        var progress = reconciliation.ReconcileLibraryFiles();

        Assert.Equal(0, progress.MissingFiles);
        Assert.Equal(MediaFileStatus.Present, Media().GetById(1)!.Status);
    }

    private IMediaFileRepository Media() => new PerCallMediaFileRepository(_factory);

    private void SeedMedia(int id, string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, 'Seed', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at)
            VALUES ($id, $id, $path, $pathKey, 2048, 'PRESENT', '2026-01-01', '2026-01-01');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", id.ToString());
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$pathKey", Path.GetFullPath(path).ToUpperInvariant());
        command.ExecuteNonQuery();
    }

    private static string FreeDriveRoot()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Z'; c >= 'D'; c--)
            if (!used.Contains(c)) return c + @":\";
        return @"Z:\";
    }

    private sealed class FakeInspector(
        IEnumerable<string>? unavailableRoots = null,
        IEnumerable<string>? absentPaths = null,
        IEnumerable<string>? errorPaths = null) : IAvailabilityInspector
    {
        private readonly HashSet<string> _unavailableRoots = new(unavailableRoots ?? [], StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _absentPaths = new(absentPaths ?? [], StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _errorPaths = new(errorPaths ?? [], StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> RootProbes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsStorageAvailable(string path)
        {
            var root = RootOf(path);
            RootProbes[root] = RootProbes.GetValueOrDefault(root) + 1;
            return !_unavailableRoots.Contains(root);
        }

        public AvailabilityInspection Inspect(string path)
        {
            if (_unavailableRoots.Contains(RootOf(path)))
                return new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable);
            if (_errorPaths.Contains(path))
                return new AvailabilityInspection(path, AvailabilityInspectionStatus.Error, ErrorCode: "IOException");
            if (_absentPaths.Contains(path))
                return new AvailabilityInspection(path, AvailabilityInspectionStatus.Missing);
            return new AvailabilityInspection(path, AvailabilityInspectionStatus.Present, new MediaFileIdentity(2048, 0, 0, 0, 0));
        }

        private static string RootOf(string path)
        {
            try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? path; }
            catch { return path; }
        }
    }
}
