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
    public void Bare_numeric_filename_in_season_folder_resolves_to_season_episode()
    {
        FileAt("Breaking Bad/Season 01/05.mkv");
        var result = Scan("Breaking Bad");
        Assert.Empty(result.RemainingMovies);
        var file = Assert.Single(result.AllFiles);
        Assert.True(file.IsResolved);
        Assert.True(file.CanImport);
        Assert.Equal("Breaking Bad", file.Title);
        Assert.Equal(1, file.SeasonNumber);
        Assert.Equal(5, file.EpisodeNumber);
    }

    [Fact]
    public void Infer_season_episode_uses_season_folder_context()
    {
        var infer1 = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Breaking Bad", "Season 02", "05.mkv"));
        Assert.NotNull(infer1);
        Assert.Equal(2, infer1.Value.Season);
        Assert.Equal(5, infer1.Value.Episode);

        var infer2 = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Breaking Bad", "Season 03", "Episode 12.mkv"));
        Assert.NotNull(infer2);
        Assert.Equal(3, infer2.Value.Season);
        Assert.Equal(12, infer2.Value.Episode);
    }

    [Fact]
    public void Explicit_filename_season_takes_precedence_over_conflicting_season_folder()
    {
        // Filename says S03E05, but parent folder says Season 02
        var infer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Breaking Bad", "Season 02", "Breaking.Bad.S03E05.mkv"));
        Assert.NotNull(infer);
        Assert.Equal(3, infer.Value.Season); // Explicit filename season must win!
        Assert.Equal(5, infer.Value.Episode);

        var inferAlt = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Breaking Bad", "Season 02", "Breaking.Bad.3x05.mkv"));
        Assert.NotNull(inferAlt);
        Assert.Equal(3, inferAlt.Value.Season);
        Assert.Equal(5, inferAlt.Value.Episode);
    }

    [Fact]
    public void Movie_sequels_in_season_folder_are_not_converted_to_tv_episodes()
    {
        // Movie sequels have titles like "Toy Story 2" or "The Godfather Part 2"
        var toyStory = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Animation", "Season 01", "Toy Story 2.mkv"));
        Assert.Null(toyStory);

        var godfather = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Classics", "Season 01", "The Godfather Part 2.mkv"));
        Assert.Null(godfather);

        var avatar = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "SciFi", "Season 01", "Avatar 2.mkv"));
        Assert.Null(avatar);
    }

    [Fact]
    public void Resolution_and_ambiguous_numbering_fixtures_return_null()
    {
        // 720 is a known resolution
        var resInfer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Movies", "Season 01", "Movie 720.mkv"));
        Assert.Null(resInfer);

        // Standalone BB - 105 without season folder is ambiguous (could be Anime absolute 105 or Western S01E05)
        var bbInfer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Anime", "BB - 105.mkv"));
        Assert.Null(bbInfer);
    }

    [Fact]
    public void Bare_numeric_and_explicit_keyword_fixtures_resolve_correctly()
    {
        var bracketInfer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Anime", "Season 02", "[05].mkv"));
        Assert.NotNull(bracketInfer);
        Assert.Equal(2, bracketInfer.Value.Season);
        Assert.Equal(5, bracketInfer.Value.Episode);

        var parenInfer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Anime", "Season 02", "(05).mkv"));
        Assert.NotNull(parenInfer);
        Assert.Equal(2, parenInfer.Value.Season);
        Assert.Equal(5, parenInfer.Value.Episode);

        var arabicInfer = MediaDiscoveryService.InferSeasonEpisode(Path.Combine(_root, "Series", "Season 04", "الحلقة 07.mkv"));
        Assert.NotNull(arabicInfer);
        Assert.Equal(4, arabicInfer.Value.Season);
        Assert.Equal(7, arabicInfer.Value.Episode);
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
