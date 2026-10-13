using DropSort.Application.Dto;
using DropSort.Application.Services;
using DropSort.Domain.Media.Discovery;
using DropSort.FileSystem.Discovery;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TvBatchAnalysisTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropsort-tvbatch-" + Guid.NewGuid().ToString("N"));
    private readonly TvBatchAnalysisService _analysis = new();

    private void FileAt(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Assert.StartsWith(_root + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic media");
    }

    private TvBatchAnalysisResult Scan(string relativeRoot = "")
    {
        var root = Path.Combine(_root, relativeRoot);
        var items = new MediaDiscoveryService().Discover(root, recursive: true);
        return _analysis.Analyze(root, items);
    }

    [Fact]
    public void Selected_series_root_supplies_missing_title_across_two_seasons()
    {
        FileAt("Breaking Bad/Season 01/Breaking.Bad.S01E01.mkv");
        FileAt("Breaking Bad/Season 01/Breaking.Bad.S01E02.mkv");
        FileAt("Breaking Bad/Season 02/S02E01.mkv");
        var result = Scan("Breaking Bad");
        var show = Assert.Single(result.Series);
        Assert.Equal("Breaking Bad", show.Title);
        Assert.Equal(2, show.Seasons.Count);
        Assert.Equal(3, result.CanImportCount);
        var folderNamed = Assert.Single(result.AllFiles, f => f.SeasonNumber == 2);
        Assert.Equal(TvEvidenceSource.Folder, folderNamed.TitleSource);
        Assert.Equal(1, folderNamed.EpisodeNumber);
        Assert.Empty(folderNamed.Warnings);
    }

    [Theory]
    [InlineData("Season 01", 1)]
    [InlineData("S02", 2)]
    [InlineData("الموسم ٠٢", 2)]
    [InlineData("موسم ۲", 2)]
    public void Season_folder_formats_are_unambiguous(string folder, int expected)
    {
        Assert.True(TvBatchAnalysisService.TryExtractSeasonFolder(folder, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Show.S01E01.mkv")]
    [InlineData("Show.1x01.mkv")]
    public void Existing_parser_patterns_remain_usable(string name)
    {
        FileAt("Show/Season 01/" + name);
        var candidate = Assert.Single(Scan().AllFiles);
        Assert.True(candidate.CanImport);
        Assert.Equal(TvEvidenceSource.Filename, candidate.SeasonSource);
        Assert.Equal(1, candidate.EpisodeNumber);
    }

    [Fact]
    public void Filename_season_wins_but_conflict_requires_explicit_full_identity_confirmation()
    {
        FileAt("Show/Season 02/Show.S01E03.mkv");
        var original = Assert.Single(Scan().AllFiles);
        Assert.Equal(1, original.SeasonNumber);
        Assert.False(original.CanImport);
        Assert.Contains(original.Warnings, w => w.Code == TvBatchWarningCode.FolderConflict);
        Assert.False(original.WithCorrection(newEpisode: 3).CanImport);
        var corrected = original.WithCorrection("Show", 1, 3);
        Assert.True(corrected.CanImport);
        Assert.Equal(TvEvidenceSource.UserCorrection, corrected.SeasonSource);
        Assert.Equal(1, original.CurrentItem.ParsedMedia!.SeasonNumber);
    }

    [Fact]
    public void Arabic_title_and_word_episode_use_folder_season()
    {
        FileAt("مسلسل عربي/الموسم ٠٢/مسلسل عربي الحلقة ٣.mkv");
        var item = Assert.Single(Scan("مسلسل عربي").AllFiles);
        Assert.True(item.CanImport);
        Assert.Equal("مسلسل عربي", item.Title);
        Assert.Equal(2, item.SeasonNumber);
        Assert.Equal(3, item.EpisodeNumber);
        Assert.Equal(TvEvidenceSource.Folder, item.SeasonSource);
    }

    [Fact]
    public void Unknown_episode_is_never_defaulted_and_can_be_corrected_explicitly()
    {
        FileAt("Show/Season 01/mystery.mkv");
        var item = Assert.Single(Scan("Show").AllFiles);
        Assert.Null(item.EpisodeNumber);
        Assert.False(item.CanImport);
        var corrected = item.WithCorrection("Show", 1, 4);
        Assert.True(corrected.CanImport);
        Assert.Equal(4, corrected.CurrentItem.ParsedMedia!.EpisodeNumber);
    }

    [Fact]
    public void Multi_episode_file_stays_unsupported_after_correction()
    {
        FileAt("Show/Season 01/Show.S01E01-E02.mkv");
        var item = Assert.Single(Scan("Show").AllFiles);
        Assert.False(item.WithCorrection("Show", 1, 1).CanImport);
        Assert.Equal(DiscoveryClassification.TvEpisodeUnsupported, item.CurrentItem.Classification);
    }

    [Fact]
    public void Additional_files_at_same_episode_warn_without_forcing_wrong_episode_numbers()
    {
        FileAt("Show/Season 01/Show.S01E01.720p.mkv");
        FileAt("Show/Season 01/Show.S01E01.1080p.mkv");
        var result = Scan("Show");
        Assert.Equal(2, result.AllFiles.Count);
        Assert.All(result.AllFiles, item =>
        {
            Assert.True(item.CanImport);
            Assert.True(item.RequiresReview);
            Assert.Contains(item.Warnings, w => w.Code == TvBatchWarningCode.DuplicateEpisodePosition);
        });
        Assert.Single(Assert.Single(Assert.Single(result.Series).Seasons).Episodes);
    }

    [Fact]
    public void Separate_folders_with_same_title_keep_separate_groups_and_consistent_review_guards()
    {
        FileAt("Edition A/Show/Season 01/Show.S01E01.mkv");
        FileAt("Edition B/Show/Season 01/Show.S01E02.mkv");
        var result = Scan();
        Assert.Equal(2, result.Series.Count);
        Assert.All(result.AllFiles, f => Assert.False(f.CanImport));
        Assert.All(result.AllFiles, f => Assert.True(f.WithCorrection("Show", 1, f.EpisodeNumber).CanImport));
    }

    [Fact]
    public void Generic_root_does_not_supply_series_title()
    {
        FileAt("Downloads/S01E01.mkv");
        var item = Assert.Single(Scan("Downloads").AllFiles);
        Assert.Null(item.Title);
        Assert.False(item.CanImport);
    }

    [Fact]
    public void Ordinary_movie_stays_in_movie_projection()
    {
        FileAt("Movies/Dune (2021).mkv");
        var result = Scan("Movies");
        Assert.Empty(result.AllFiles);
        Assert.Single(result.RemainingMovies);
    }

    [Fact]
    public void Discovery_error_remains_non_importable()
    {
        var item = DiscoveredMedia.Error(Path.Combine(_root, "missing.mkv"),
            new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Unavailable"));
        var candidate = Assert.Single(_analysis.Analyze(_root, [item]).AllFiles);
        Assert.False(candidate.WithCorrection("Show", 1, 1).CanImport);
        Assert.Same(item, candidate.CurrentItem);
    }

    public void Dispose()
    {
        var absolute = Path.GetFullPath(_root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), absolute, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
    }
}
