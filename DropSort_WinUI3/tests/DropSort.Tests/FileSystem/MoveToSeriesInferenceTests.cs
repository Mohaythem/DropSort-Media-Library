using DropSort.FileSystem.Discovery;
using Xunit;

namespace DropSort.Tests.FileSystem;

/// <summary>
/// Season/episode inference for the manual "Add / Move to series" repair, driven from the ORIGINAL local
/// filename and reusing the discovery parser (no second parser). Confident cases pre-fill the dialog;
/// genuinely missing/ambiguous numbering returns null so the user is asked instead of a wrong guess.
/// </summary>
public sealed class MoveToSeriesInferenceTests
{
    [Theory]
    [InlineData("الرجل العناب الحلقة 29.mkv", 1, 29)]   // Arabic "الحلقة N" -> flat season 1
    [InlineData("الرجل العناب الحلقة ٢٩.mkv", 1, 29)]   // Arabic-Indic digits
    [InlineData("الرجل العناب 30.mkv", 1, 30)]          // bare trailing number, no keyword
    [InlineData("الرجل العناب ٣٠.mkv", 1, 30)]          // bare trailing Arabic-Indic number
    [InlineData("Show.S02E05.1080p.mkv", 2, 5)]         // SxxEyy
    [InlineData("Show.1x04.mkv", 1, 4)]                 // 1x02 form
    [InlineData("Show Episode 12.mkv", 1, 12)]          // English "Episode N"
    [InlineData("Some Show Ep.7.mkv", 1, 7)]            // "Ep N"
    public void Filenames_with_episode_evidence_infer_season_and_episode(string fileName, int season, int episode)
    {
        var inferred = MediaDiscoveryService.InferSeasonEpisode(fileName);

        Assert.NotNull(inferred);
        Assert.Equal((season, episode), inferred!.Value);
    }

    [Theory]
    [InlineData("Some Movie (2020).mkv")]   // a year in parens is not an episode
    [InlineData("Blade Runner 2049 (2017).mkv")] // a 4-digit number in the title is never an episode
    [InlineData("Interstellar.mkv")]        // no number at all
    [InlineData("")]
    [InlineData(null)]
    public void Filenames_without_confident_numbering_return_null(string? fileName)
    {
        Assert.Null(MediaDiscoveryService.InferSeasonEpisode(fileName));
    }
}
