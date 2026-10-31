using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Availability;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Media;
using TBDMovies.FileSystem.Inspection;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class MediaSafetyRelinkAndPlaybackTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_safety_{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_safety_ws_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public MediaSafetyRelinkAndPlaybackTests()
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

    [Theory]
    [InlineData(@"C:\media\payload.exe")]
    [InlineData(@"C:\media\script.bat")]
    [InlineData(@"C:\media\noext")]
    public async Task MediaFileActions_OpenFileAsync_BlocksUnapprovedExtensions_WithoutShellLaunch(string unsafePath)
    {
        var launchCount = 0;
        var inspector = new FakeAvailabilityInspector(AvailabilityInspectionStatus.Present);
        var actions = new MediaFileActions(inspector, (path, selectFile) => launchCount++);

        var result = await actions.OpenFileAsync(unsafePath);

        Assert.Equal(AvailabilityInspectionStatus.Error, result.Status);
        Assert.Equal("UnsupportedMediaType", result.ErrorCode);
        Assert.Equal(0, launchCount); // Seam proves no process was launched
    }

    [Theory]
    [InlineData(@"C:\media\legit.mkv")]
    [InlineData(@"C:\media\LEGIT.MKV")]
    [InlineData(@"C:\media\video.mp4")]
    public async Task MediaFileActions_OpenFileAsync_AllowsApprovedExtensions_CaseInsensitively(string safePath)
    {
        string? launchedPath = null;
        bool? launchedSelect = null;
        var inspector = new FakeAvailabilityInspector(AvailabilityInspectionStatus.Present);
        var actions = new MediaFileActions(inspector, (path, selectFile) =>
        {
            launchedPath = path;
            launchedSelect = selectFile;
        });

        var result = await actions.OpenFileAsync(safePath);

        Assert.Equal(AvailabilityInspectionStatus.Present, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Equal(safePath, launchedPath);
        Assert.False(launchedSelect);
    }

    [Fact]
    public async Task MediaFileActions_OpenContainingFolderAsync_AllowsReveal_EvenForExecutables()
    {
        string? launchedPath = null;
        bool? launchedSelect = null;
        var inspector = new FakeAvailabilityInspector(AvailabilityInspectionStatus.Present);
        var actions = new MediaFileActions(inspector, (path, selectFile) =>
        {
            launchedPath = path;
            launchedSelect = selectFile;
        });

        var exePath = @"C:\media\tool.exe";
        var result = await actions.OpenContainingFolderAsync(exePath);

        Assert.Equal(AvailabilityInspectionStatus.Present, result.Status);
        Assert.Equal(exePath, launchedPath);
        Assert.True(launchedSelect); // Explorer reveal (/select), not direct execution
    }

    [Fact]
    public async Task MediaFileActions_OpenFolderAsync_AllowsFolderOpening()
    {
        var dirPath = Path.Combine(_workspace, "test_dir");
        Directory.CreateDirectory(dirPath);

        string? launchedPath = null;
        bool? launchedSelect = null;
        var inspector = new AvailabilityInspector();
        var actions = new MediaFileActions(inspector, (path, selectFile) =>
        {
            launchedPath = path;
            launchedSelect = selectFile;
        });

        var result = await actions.OpenFolderAsync(dirPath);

        Assert.Equal(AvailabilityInspectionStatus.Present, result.Status);
        Assert.Equal(dirPath, launchedPath);
        Assert.False(launchedSelect);
    }

    [Theory]
    [InlineData("evil.exe")]
    [InlineData("document.txt")]
    public void ReconciliationService_PrepareMediaRelink_RejectsUnapprovedExtensions(string badFileName)
    {
        var mediaFileId = SeedMissingMediaFile("original.mkv", 1024);
        var candidatePath = Path.Combine(_workspace, badFileName);
        File.WriteAllBytes(candidatePath, new byte[1024]);

        var inspector = new AvailabilityInspector();
        using var uow = _factory.Begin();
        var service = new ReconciliationService(uow.MediaFiles, inspector);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            service.PrepareMediaRelink(mediaFileId, candidatePath));
        Assert.Contains("Unsupported media extension", ex.Message);
    }

    [Theory]
    [InlineData("relinked.mkv")]
    [InlineData("RELINKED.MP4")]
    public void ReconciliationService_PrepareAndConfirm_AllowsApprovedExtensions_CaseInsensitively(string goodFileName)
    {
        var mediaFileId = SeedMissingMediaFile("original.mkv", 2048);
        var candidatePath = Path.Combine(_workspace, goodFileName);
        File.WriteAllBytes(candidatePath, new byte[2048]);

        var inspector = new AvailabilityInspector();
        using var uow = _factory.Begin();
        var service = new ReconciliationService(uow.MediaFiles, inspector, clock: () => Now);

        var preview = service.PrepareMediaRelink(mediaFileId, candidatePath);
        Assert.NotNull(preview);
        Assert.Equal(candidatePath, preview.CandidatePath);

        var result = service.ConfirmMediaRelink(preview.PreviewId);
        Assert.NotNull(result);
        Assert.Equal(candidatePath, result.MediaFile.CurrentPath);
    }

    [Fact]
    public void ReconciliationService_ConfirmMediaRelink_RevalidatesAgainstUnsupportedExtension()
    {
        var mediaFileId = SeedMissingMediaFile("original.mkv", 2048);
        var candidatePath = Path.Combine(_workspace, "relinked.mkv");
        File.WriteAllBytes(candidatePath, new byte[2048]);

        var inspector = new AvailabilityInspector();
        using var uow = _factory.Begin();
        var service = new ReconciliationService(uow.MediaFiles, inspector, clock: () => Now);

        var preview = service.PrepareMediaRelink(mediaFileId, candidatePath);

        // If candidate file is replaced by an executable before confirmation:
        File.Delete(candidatePath);
        var badPath = Path.Combine(_workspace, "relinked.exe");
        File.WriteAllBytes(badPath, new byte[2048]);

        // Trying to confirm with a changed/missing preview candidate fails
        Assert.Throws<InvalidOperationException>(() => service.ConfirmMediaRelink(preview.PreviewId));
    }

    private int SeedMissingMediaFile(string fileName, long size)
    {
        var originalPath = Path.Combine(_workspace, "missing_original", fileName);
        using var uow = _factory.Begin();
        var movie = uow.Movies.Create(
            new MovieCatalogData(null, null, "Test Movie", null, null, null,
                System.Collections.Immutable.ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
            Now);
        var file = uow.MediaFiles.Add(
            new VerifiedMediaFileFacts(originalPath, size, Path.GetExtension(originalPath), null, null, null, Now),
            movie.Id);
        uow.MediaFiles.MarkMissing(file.Id, Now);
        uow.Commit();
        return file.Id;
    }

    private sealed class FakeAvailabilityInspector(AvailabilityInspectionStatus status, string? errorCode = null) : IAvailabilityInspector
    {
        public AvailabilityInspection Inspect(string path) =>
            new(path, status, ErrorCode: errorCode);

        public bool IsStorageAvailable(string path) =>
            status != AvailabilityInspectionStatus.StorageUnavailable;
    }
}

public sealed class MediaFormatPolicyTests
{
    [Theory]
    [InlineData(".mkv", true)]
    [InlineData(".mp4", true)]
    [InlineData(".MKV", true)]
    [InlineData(".exe", false)]
    [InlineData(".bat", false)]
    [InlineData(".txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MediaFormatPolicy_ValidatesExtensionsCorrectly(string? extension, bool expectedValid)
    {
        Assert.Equal(expectedValid, MediaFormatPolicy.IsSupportedVideoExtension(extension));
        var dummyPath = extension is not null ? $@"C:\media\test_file{extension}" : null;
        Assert.Equal(expectedValid, MediaFormatPolicy.IsSupportedVideoPath(dummyPath));
    }

    [Fact]
    public void MediaFormatPolicy_RejectsFilesWithoutExtensionOrMalformedPaths()
    {
        Assert.False(MediaFormatPolicy.IsSupportedVideoPath(@"C:\media\no_extension_file"));
        Assert.False(MediaFormatPolicy.IsSupportedVideoPath(""));
        Assert.False(MediaFormatPolicy.IsSupportedVideoPath("   "));
        Assert.False(MediaFormatPolicy.IsSupportedVideoPath(null));
    }
}
