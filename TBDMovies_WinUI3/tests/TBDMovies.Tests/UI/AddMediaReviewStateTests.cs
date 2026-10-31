using TBDMovies.Application.Dto;
using TBDMovies.UI.Services;
using Xunit;

namespace TBDMovies.Tests.UI;

public sealed class AddMediaReviewStateTests
{
    [Fact]
    public void Excluding_an_eligible_file_is_not_a_review_failure_but_unresolved_files_still_need_review()
    {
        var excluded = new AddMediaReviewFacts(true, ImportItemStatus.SkippedByUser, false);
        var unresolved = new AddMediaReviewFacts(false, ImportItemStatus.SkippedByUser, false);
        Assert.True(excluded.CanAdd);
        Assert.True(excluded.Matches(AddMediaReviewFilter.AllNew));
        Assert.False(excluded.NeedsAttention);
        Assert.True(unresolved.NeedsAttention);
        var counts = new AddMediaReviewCounts<int>();
        counts.Update(1, excluded); counts.Update(2, unresolved);
        Assert.Equal(1, counts.Attention); Assert.Equal(0, counts.Selected);
    }

    [Theory]
    [InlineData(ImportItemStatus.MetadataUnavailable)]
    [InlineData(ImportItemStatus.MetadataError)]
    [InlineData(ImportItemStatus.MetadataCancelled)]
    [InlineData(ImportItemStatus.NoMatchFound)]
    [InlineData(ImportItemStatus.NeedsMatch)]
    [InlineData(ImportItemStatus.RegistrationFailed)]
    public void Metadata_attention_does_not_block_local_selection(ImportItemStatus status)
    {
        var facts = new AddMediaReviewFacts(true, status, true);
        Assert.True(facts.CanAdd);
        Assert.False(facts.Ready);
        Assert.True(facts.Matches(AddMediaReviewFilter.AllNew));
        Assert.True(facts.Matches(AddMediaReviewFilter.NeedsAttention));
        Assert.False(facts.Matches(AddMediaReviewFilter.Ready));
    }

    [Fact]
    public void Exact_registered_path_is_excluded_while_another_path_with_same_title_remains_new()
    {
        var counts = new AddMediaReviewCounts<string>();
        var registered = new AddMediaReviewFacts(true, ImportItemStatus.AlreadyInLibrary, true);
        var anotherPath = new AddMediaReviewFacts(true, ImportItemStatus.Matched, true);
        counts.Update("C:\\qa\\first\\Film.mp4", registered);
        counts.Update("C:\\qa\\second\\Film.mp4", anotherPath);
        Assert.All(Enum.GetValues<AddMediaReviewFilter>(), filter => Assert.False(registered.Matches(filter)));
        Assert.True(anotherPath.Matches(AddMediaReviewFilter.Ready));
        Assert.Equal(1, counts.Registered);
        Assert.Equal(1, counts.NewFiles);
        Assert.Equal(1, counts.Selected);
    }

    [Fact]
    public void Global_selection_survives_filter_queries_and_incremental_status_updates()
    {
        var counts = new AddMediaReviewCounts<int>();
        var ready = new AddMediaReviewFacts(true, ImportItemStatus.Matched, true);
        var offline = new AddMediaReviewFacts(true, ImportItemStatus.MetadataUnavailable, true);
        counts.Update(1, ready);
        counts.Update(2, offline);
        Assert.False(offline.Matches(AddMediaReviewFilter.Ready));
        Assert.Equal(2, counts.Selected);
        counts.Update(2, offline with { Status = ImportItemStatus.Matched });
        Assert.Equal(2, counts.Ready);
        Assert.Equal(0, counts.Attention);
        Assert.Equal(2, counts.NewFiles);
        counts.Remove(1); // A successful add leaves the remaining selected queue intact.
        Assert.Equal(1, counts.Selected);
        Assert.Equal(1, counts.NewFiles);
        counts.Remove(1); // Unsubscription/removal is safe to repeat.
        Assert.Equal(1, counts.Selected);
        counts.Clear();
        Assert.Equal(0, counts.Selected);
        Assert.Equal(0, counts.NewFiles);
    }

    [Fact]
    public void Discovery_issue_and_in_progress_review_are_distinct_from_ready()
    {
        // A directory/storage discovery issue is a review row, not a newly discovered physical file.
        var issue = new AddMediaReviewFacts(false, ImportItemStatus.ReadyToAdd, false, DiscoveredFile: false);
        var reviewing = new AddMediaReviewFacts(true, ImportItemStatus.Matching, true);
        Assert.True(issue.NeedsAttention);
        Assert.False(issue.CanAdd);
        Assert.False(reviewing.Ready);
        Assert.False(reviewing.NeedsAttention);
        var counts = new AddMediaReviewCounts<int>();
        counts.Update(1, issue);
        counts.Update(2, reviewing);
        Assert.Equal(1, counts.NewFiles);
        Assert.Equal(1, counts.Eligible);
        Assert.Equal(1, counts.Attention);
        Assert.Equal(1, counts.Selected);
    }

    [Fact]
    public void Large_batch_counts_do_not_double_count_repeated_notifications()
    {
        var counts = new AddMediaReviewCounts<int>();
        for (var i = 0; i < 10000; i++)
        {
            counts.Update(i, new(true, ImportItemStatus.Matching, true));
            counts.Update(i, new(true, ImportItemStatus.MetadataUnavailable, true));
            counts.Update(i, new(true, ImportItemStatus.MetadataUnavailable, true));
        }
        Assert.Equal(10000, counts.NewFiles);
        Assert.Equal(10000, counts.Selected);
        Assert.Equal(10000, counts.Attention);
        Assert.Equal(0, counts.Ready);
    }
}
