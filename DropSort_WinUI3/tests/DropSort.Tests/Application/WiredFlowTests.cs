using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Inspection;
using DropSort.FileSystem.Operations;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// The flows the shell drives, over a real SQLite catalog and real files: registering detected media,
/// organizing a registered file, and the reconciliation pass behind Check Library. These cover the
/// behaviour the UI depends on but could not assert from the view layer.
/// </summary>
public sealed class WiredFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly string _workspace;
    private readonly CatalogUnitOfWorkFactory _factory;

    public WiredFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dropsort_flow_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);

        _workspace = Path.Combine(Path.GetTempPath(), $"dropsort_flow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void Registering_the_same_file_twice_keeps_one_movie_and_one_media_file()
    {
        var import = NewImportService();
        var path = WriteMedia("source", "Dune Part Two (2024) 1080p x265.mkv");

        var first = import.RegisterMovieImport(Command(path));
        var second = import.RegisterMovieImport(Command(path));

        Assert.Equal(first.Movie.Id, second.Movie.Id);
        Assert.Equal(first.MediaFile.Id, second.MediaFile.Id);

        using var work = _factory.Begin();
        Assert.Equal(1, work.Movies.CountAll());
        Assert.Single(work.MediaFiles.ListAll());
    }

    [Fact]
    public void Organizing_a_registered_file_moves_it_and_repoints_the_catalog()
    {
        var import = NewImportService();
        var source = WriteMedia("source", "Past Lives (2023) 1080p.mkv");
        var destinationRoot = Path.Combine(_workspace, "library");
        Directory.CreateDirectory(destinationRoot);

        var registered = import.RegisterMovieImport(Command(source));

        using var store = new FileOperationStore(_connectionString);
        var coordinator = new FileOperationCoordinator(store);
        var organization = new OrganizationService(MediaFiles(), coordinator);

        var preview = organization.PrepareOrganization(
            registered.MediaFile.Id,
            destinationRoot,
            Path.GetFileName(source));

        Assert.Equal(source, preview.Plan.Source);
        Assert.Equal(Path.Combine(destinationRoot, Path.GetFileName(source)), preview.Plan.Destination);

        organization.ConfirmOrganization(preview.PreviewId);

        var destination = Path.Combine(destinationRoot, Path.GetFileName(source));
        Assert.True(File.Exists(destination), "the file should be at the destination");
        Assert.False(File.Exists(source), "the source should be gone after a move");

        var stored = MediaFiles().GetById(registered.MediaFile.Id);
        Assert.NotNull(stored);
        Assert.Equal(destination, stored!.CurrentPath);
        Assert.Equal(MediaFileStatus.Present, stored.Status);

        var history = new OperationHistoryService(store, coordinator, MediaFiles(), Movies())
            .ListOperationHistory();
        Assert.Single(history);
        Assert.Equal(DropSort.Domain.Core.Operations.OperationState.Committed, history[0].State);
    }

    [Fact]
    public void Discarding_an_organization_preview_leaves_the_file_and_the_catalog_alone()
    {
        var import = NewImportService();
        var source = WriteMedia("source", "Perfect Days (2023) 720p.mkv");
        var destinationRoot = Path.Combine(_workspace, "library");
        Directory.CreateDirectory(destinationRoot);

        var registered = import.RegisterMovieImport(Command(source));

        using var store = new FileOperationStore(_connectionString);
        var organization = new OrganizationService(MediaFiles(), new FileOperationCoordinator(store));
        var preview = organization.PrepareOrganization(
            registered.MediaFile.Id,
            destinationRoot,
            Path.GetFileName(source));

        organization.DiscardOrganizationPreview(preview.PreviewId);

        Assert.True(File.Exists(source));
        Assert.Equal(source, MediaFiles().GetById(registered.MediaFile.Id)!.CurrentPath);
        Assert.Throws<KeyNotFoundException>(() => organization.ConfirmOrganization(preview.PreviewId));
    }

    [Fact]
    public void A_deleted_file_is_reported_missing_once_per_movie_and_stays_registered()
    {
        var import = NewImportService();
        var present = WriteMedia("source", "Oppenheimer (2023) 2160p.mkv");
        var vanishing = WriteMedia("source", "Tar (2022) 1080p.mkv");

        import.RegisterMovieImport(Command(present));
        var removed = import.RegisterMovieImport(Command(vanishing));
        File.Delete(vanishing);

        var reconciliation = new ReconciliationService(MediaFiles(), new AvailabilityInspector(), Movies());
        var result = reconciliation.CheckLibrary();

        Assert.Equal(2, result.FileProgress.CheckedFiles);
        Assert.Equal(1, result.FileProgress.MissingFiles);

        var missing = MediaFiles().ListMissing();
        Assert.Single(missing);
        Assert.Equal(removed.MediaFile.Id, missing[0].Id);
        Assert.Equal(removed.Movie.Id, missing[0].MovieId);

        // The record survives the pass: a missing file is an issue to report, never a deletion.
        Assert.NotNull(MediaFiles().GetById(removed.MediaFile.Id));

        // Both movies are metadata-incomplete, and each appears exactly once, so a page that unions
        // the missing-file movies with these has one row per movie rather than one per fact.
        Assert.Equal(2, result.CurrentIssues.Count);
        Assert.Equal(result.CurrentIssues.Select(issue => issue.MovieId).Distinct().Count(), result.CurrentIssues.Count);
    }

    [Fact]
    public void A_second_pass_over_a_healthy_library_reports_nothing_missing()
    {
        var import = NewImportService();
        import.RegisterMovieImport(Command(WriteMedia("source", "Anatomy of a Fall (2023) 1080p.mkv")));

        var reconciliation = new ReconciliationService(MediaFiles(), new AvailabilityInspector(), Movies());
        reconciliation.CheckLibrary();
        var second = reconciliation.CheckLibrary();

        Assert.Equal(1, second.FileProgress.CheckedFiles);
        Assert.Equal(0, second.FileProgress.MissingFiles);
        Assert.Empty(MediaFiles().ListMissing());
    }

    [Fact]
    public void A_unit_of_work_commits_a_write_and_rolls_an_uncommitted_one_back()
    {
        using (var committed = _factory.Begin())
        {
            committed.Movies.Create(CatalogData("Committed"), DateTimeOffset.UtcNow);
            committed.Commit();
        }

        using (var abandoned = _factory.Begin())
        {
            abandoned.Movies.Create(CatalogData("Abandoned"), DateTimeOffset.UtcNow);
        }

        using var read = _factory.Begin();
        var titles = read.Movies.ListAll().Select(movie => movie.Title).ToArray();

        Assert.Contains("Committed", titles);
        Assert.DoesNotContain("Abandoned", titles);
    }

    [Fact]
    public void Organizing_across_volumes_copies_verifies_and_repoints_the_catalog()
    {
        var otherVolume = SecondVolumeWorkspace();

        if (otherVolume is null)
        {
            // A single-volume machine cannot exercise the copy-and-verify strategy; the same-volume
            // test above covers the rename path.
            return;
        }

        try
        {
            var import = NewImportService();
            var source = WriteMedia("source", "Society of the Snow (2023) 1080p.mkv");
            var registered = import.RegisterMovieImport(Command(source));

            using var store = new FileOperationStore(_connectionString);
            var coordinator = new FileOperationCoordinator(store);
            var organization = new OrganizationService(MediaFiles(), coordinator);

            var preview = organization.PrepareOrganization(
                registered.MediaFile.Id,
                otherVolume,
                Path.GetFileName(source));

            organization.ConfirmOrganization(preview.PreviewId);

            var destination = Path.Combine(otherVolume, Path.GetFileName(source));
            Assert.True(File.Exists(destination), "the file should be on the other volume");
            Assert.False(File.Exists(source), "the source should be gone after a cross-volume move");
            Assert.Equal(destination, MediaFiles().GetById(registered.MediaFile.Id)!.CurrentPath);

            var record = new OperationHistoryService(store, coordinator, MediaFiles(), Movies())
                .ListOperationHistory()[0];
            Assert.Equal(DropSort.Domain.Core.Operations.OperationState.Committed, record.State);
        }
        finally
        {
            if (Directory.Exists(otherVolume))
            {
                Directory.Delete(otherVolume, recursive: true);
            }
        }
    }

    [Fact]
    public void Relink_lifecycle_present_to_missing_to_relinked_and_verified_on_restart()
    {
        var import = NewImportService();
        var source = WriteMedia("source", "Oppenheimer (2023) 1080p.mkv");
        var registered = import.RegisterMovieImport(Command(source));

        var inspector = new AvailabilityInspector();
        var reconciliation = new ReconciliationService(MediaFiles(), inspector, Movies());

        // Initial state: 1 present, 0 missing
        var initialProgress = reconciliation.CheckLibrary();
        Assert.Equal(1, initialProgress.TotalMovies);
        Assert.Equal(0, initialProgress.FileProgress.MissingFiles);

        // Delete original file on disk
        File.Delete(source);

        // Run reconciliation: file becomes missing
        var missingProgress = reconciliation.CheckLibrary();
        Assert.Equal(1, missingProgress.FileProgress.MissingFiles);

        var missingFiles = MediaFiles().ListMissing();
        Assert.Single(missingFiles);
        Assert.Equal(registered.MediaFile.Id, missingFiles[0].Id);
        Assert.Equal(MediaFileStatus.Missing, missingFiles[0].Status);

        // Prepare replacement file in a different directory with matching size (2048 bytes)
        var replacement = WriteMedia("replacement", "Oppenheimer.2023.Repaired.mkv");

        // Prepare relink and verify candidate is not mutated
        var preview = reconciliation.PrepareMediaRelink(registered.MediaFile.Id, replacement);
        Assert.NotNull(preview);
        Assert.Equal(replacement, preview.CandidatePath);
        Assert.True(File.Exists(replacement), "Candidate file must not be moved or deleted during prepare.");

        // Confirm relink
        var result = reconciliation.ConfirmMediaRelink(preview.PreviewId);
        Assert.NotNull(result);
        Assert.Equal(replacement, result.MediaFile.CurrentPath);
        Assert.Equal(MediaFileStatus.Present, result.MediaFile.Status);
        Assert.True(File.Exists(replacement), "Candidate file must not be moved or deleted during confirm.");

        // Restart simulation: fresh service instances reading from the database
        var freshReconciliation = new ReconciliationService(MediaFiles(), inspector, Movies());
        var restartProgress = freshReconciliation.CheckLibrary();
        Assert.Equal(0, restartProgress.FileProgress.MissingFiles);
        Assert.Equal(1, restartProgress.FileProgress.CheckedFiles);
        Assert.Empty(MediaFiles().ListMissing());

        var updatedMedia = MediaFiles().GetById(registered.MediaFile.Id);
        Assert.NotNull(updatedMedia);
        Assert.Equal(replacement, updatedMedia.CurrentPath);
        Assert.Equal(MediaFileStatus.Present, updatedMedia.Status);
    }

    [Fact]
    public void Relink_validates_candidate_existence_size_and_collisions_before_mutation()
    {
        var import = NewImportService();
        var fileA = WriteMedia("sourceA", "Movie A (2024).mkv");
        var fileB = WriteMedia("sourceB", "Movie B (2024).mkv");

        var regA = import.RegisterMovieImport(Command(fileA));
        var regB = import.RegisterMovieImport(Command(fileB));

        var inspector = new AvailabilityInspector();
        var reconciliation = new ReconciliationService(MediaFiles(), inspector, Movies());

        // Attempting to relink a file that is still PRESENT must fail
        Assert.Throws<InvalidOperationException>(() =>
            reconciliation.PrepareMediaRelink(regA.MediaFile.Id, fileA));

        // Delete fileA to make it missing
        File.Delete(fileA);
        reconciliation.ReconcileLibraryFiles();

        // 1. Candidate does not exist
        Assert.Throws<InvalidOperationException>(() =>
            reconciliation.PrepareMediaRelink(regA.MediaFile.Id, Path.Combine(_workspace, "does_not_exist.mkv")));

        // 2. Candidate size mismatch (file is 4096 bytes, media A was 2048 bytes)
        var wrongSize = Path.Combine(_workspace, "wrong_size.mkv");
        File.WriteAllBytes(wrongSize, new byte[4096]);
        var sizeEx = Assert.Throws<InvalidOperationException>(() =>
            reconciliation.PrepareMediaRelink(regA.MediaFile.Id, wrongSize));
        Assert.Contains("does not match expected size", sizeEx.Message);

        // 3. Collision: candidate path already belongs to active media file B
        var collisionEx = Assert.Throws<InvalidOperationException>(() =>
            reconciliation.PrepareMediaRelink(regA.MediaFile.Id, fileB));
        Assert.Contains("already owns", collisionEx.Message);
    }

    /// <summary>
    /// A writable folder on a volume other than the workspace's, or null when there is only one. The
    /// transfer engine picks copy-and-verify instead of rename when the destination is another volume,
    /// which is the slow path the UI now runs off the UI thread.
    /// </summary>
    private string? SecondVolumeWorkspace()
    {
        var workspaceRoot = Path.GetPathRoot(Path.GetFullPath(_workspace));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady
                || drive.DriveType != DriveType.Fixed
                || string.Equals(drive.RootDirectory.FullName, workspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = Path.Combine(drive.RootDirectory.FullName, $"dropsort_xvol_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (Exception)
            {
                // Not writable: try the next volume.
            }
        }

        return null;
    }

    private ImportService NewImportService() =>
        new(_factory, new NoMetadataProvider(), new DropSort.FileSystem.Discovery.MediaDiscoveryService());

    private IMediaFileRepository MediaFiles() => new PerCallMediaFileRepository(_factory);

    private IMovieRepository Movies() => new PerCallMovieRepository(_factory);

    private static MovieCatalogData CatalogData(string title) => new(
        provider: null,
        externalId: null,
        title: title,
        originalTitle: null,
        year: 2024,
        overview: null,
        genres: System.Collections.Immutable.ImmutableArray<string>.Empty,
        runtimeMinutes: null,
        rating: null,
        posterReference: null,
        metadataStatus: MetadataStatus.Pending);

    private string WriteMedia(string folder, string fileName)
    {
        var directory = Path.Combine(_workspace, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, new byte[2048]);
        return path;
    }

    private static ConfirmMovieImportCommand Command(string path) => new(
        path,
        new FileInfo(path).Length,
        new ParsedMedia(
            Path.GetFileName(path),
            MediaType.Movie,
            Path.GetFileNameWithoutExtension(path),
            2024,
            "1080p",
            null,
            "x265",
            Path.GetExtension(path)),
        DateTimeOffset.UtcNow);

    private sealed class NoMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "none";

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];

        public MovieMetadata? GetMovie(string externalId) => null;
    }
}
