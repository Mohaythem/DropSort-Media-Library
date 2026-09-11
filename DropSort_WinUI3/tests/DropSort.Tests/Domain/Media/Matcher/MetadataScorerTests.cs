using System;
using System.Collections.Immutable;
using System.Linq;
using DropSort.Domain.Media.Matcher;
using DropSort.Domain.Metadata.Contracts;
using Xunit;

namespace DropSort.Tests.Domain.Media.Matcher;

public class MetadataScorerTests
{
    [Theory]
    [InlineData("Spider-Man: No Way Home", "spider man no way home")]
    [InlineData("Breaking   Bad!", "breaking bad")]
    [InlineData("  The Dark   Knight  ", "the dark knight")]
    [InlineData("Alien / Aliens", "alien aliens")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeTitle_StripsPunctuationAndCollapsesSpaces(string? input, string expected)
    {
        var result = MetadataScorer.NormalizeTitle(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Amélie", "amelie")]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("WALL·E", "wall e")]
    public void NormalizeTitle_DecomposesAccentsAndSpecialCharacters(string input, string expected)
    {
        var result = MetadataScorer.NormalizeTitle(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Score_AccentedCandidate_MatchesUnaccentedQuery()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "100", "Amélie", null, 2001, "Overview", 8.3, "/poster.jpg")
        };

        var decision = MetadataScorer.Score("Amelie", 2001, candidates);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.NotNull(decision.Candidate);
        Assert.Equal("100", decision.Candidate.ExternalId);
        Assert.Contains(MatchReason.TitleExact, decision.Reasons);
    }

    [Fact]
    public void Score_ExactTitleAndYear_ProducesMatchedWithClearWinner()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "155", "The Dark Knight", null, 2008, "Overview", 9.0, "/poster.jpg"),
            new MovieCandidate("tmdb", "999", "Other Movie", null, 2008, "Overview", 7.0, "/poster2.jpg")
        };

        var decision = MetadataScorer.Score("The Dark Knight", 2008, candidates);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.NotNull(decision.Candidate);
        Assert.Equal("155", decision.Candidate.ExternalId);
        Assert.Equal(1.0, decision.Confidence);
        Assert.Contains(MatchReason.ClearWinner, decision.Reasons);
        Assert.Contains(MatchReason.TitleExact, decision.Reasons);
        Assert.Contains(MatchReason.YearExact, decision.Reasons);
        Assert.Equal(1.0, decision.RankedCandidates[0].Score);
    }

    [Fact]
    public void Score_OneYearDifference_ReceivesSmallerBonus()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Inception", null, 2011, "Overview", 8.8, "/p.jpg")
        };

        var decision = MetadataScorer.Score("Inception", 2010, candidates);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.Equal(0.85, decision.Confidence);
        Assert.Single(decision.RankedCandidates);
        Assert.Contains(MatchReason.TitleExact, decision.RankedCandidates[0].MatchReasons);
    }

    [Fact]
    public void Score_YearConflict_AppliesPenalty()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Dune", null, 1984, "Overview", 6.5, "/p.jpg")
        };

        // 2021 vs 1984 is > 1 year diff -> penalty -0.30
        var decision = MetadataScorer.Score("Dune", 2021, candidates);

        Assert.Equal(0.45, decision.RankedCandidates[0].Score);
        Assert.Contains(MatchReason.YearConflict, decision.RankedCandidates[0].PenaltyReasons);
    }

    [Fact]
    public void Score_AmbiguousTopCandidates_ProducesReviewRequired()
    {
        // Both candidates match title strongly and have close scores
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Robin Hood", null, 2010, "Overview", 6.7, "/p1.jpg"),
            new MovieCandidate("tmdb", "2", "Robin Hood", null, 2018, "Overview", 5.3, "/p2.jpg")
        };

        // Query year is null, so both get title exact (0.75) and year missing
        var decision = MetadataScorer.Score("Robin Hood", null, candidates);

        Assert.Equal(MatchStatus.ReviewRequired, decision.Status);
        Assert.NotNull(decision.Candidate);
        Assert.Contains(MatchReason.AmbiguousTopCandidates, decision.Reasons);
    }

    [Fact]
    public void Score_BelowThreshold_ProducesNoMatch()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Completely Unrelated Title", null, 1990, "Overview", 5.0, "/p.jpg")
        };

        var decision = MetadataScorer.Score("The Matrix", 1999, candidates);

        Assert.Equal(MatchStatus.NoMatch, decision.Status);
        Assert.Null(decision.Candidate);
        Assert.Contains(MatchReason.BelowAutoMatchThreshold, decision.Reasons);
    }

    [Fact]
    public void Score_EmptyCandidates_ProducesNoMatchWithNoCandidatesReason()
    {
        var decision = MetadataScorer.Score("Any Movie", 2020, Array.Empty<MovieCandidate>());

        Assert.Equal(MatchStatus.NoMatch, decision.Status);
        Assert.Null(decision.Candidate);
        Assert.Equal(0.0, decision.Confidence);
        Assert.Contains(MatchReason.NoCandidates, decision.Reasons);
        Assert.Empty(decision.RankedCandidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Score_InvalidQueryTitle_ProducesNoMatchWithInvalidParsedTitleReason(string? title)
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Movie", null, 2020, null, null, null)
        };

        var decision = MetadataScorer.Score(title!, 2020, candidates);

        Assert.Equal(MatchStatus.NoMatch, decision.Status);
        Assert.Null(decision.Candidate);
        Assert.Contains(MatchReason.InvalidParsedTitle, decision.Reasons);
    }

    [Fact]
    public void Score_TvCandidates_ProducesTvMatchDecisionWithParity()
    {
        var tvCandidates = new[]
        {
            new TvCandidate("tmdb", "1396", "Breaking Bad", null, 2008, "Overview", 9.5, "/bb.jpg", "/bb_bg.jpg"),
            new TvCandidate("tmdb", "9999", "Better Call Saul", null, 2015, "Overview", 8.9, "/bcs.jpg", "/bcs_bg.jpg")
        };

        var decision = MetadataScorer.Score("Breaking Bad", 2008, tvCandidates);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.NotNull(decision.Candidate);
        Assert.Equal("1396", decision.Candidate.ExternalId);
        Assert.Equal(1.0, decision.Confidence);
        Assert.Contains(MatchReason.ClearWinner, decision.Reasons);
        Assert.Contains(MatchReason.TitleExact, decision.Reasons);
        Assert.Contains(MatchReason.YearExact, decision.Reasons);

        var scoreTvDecision = MetadataScorer.ScoreTv("Breaking Bad", 2008, tvCandidates);
        Assert.Equal(decision.Status, scoreTvDecision.Status);
        Assert.Equal(decision.Candidate.ExternalId, scoreTvDecision.Candidate!.ExternalId);
    }

    [Fact]
    public void Score_OriginalTitle_MatchesWhenPrimaryTitleDiffers()
    {
        var candidates = new[]
        {
            new MovieCandidate("tmdb", "1", "Spirited Away", "Sen to Chihiro no Kamikakushi", 2001, "Overview", 8.6, "/p.jpg")
        };

        var decision = MetadataScorer.Score("Sen to Chihiro no Kamikakushi", 2001, candidates);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.Contains(MatchReason.OriginalTitleExact, decision.RankedCandidates[0].MatchReasons);
    }
}
