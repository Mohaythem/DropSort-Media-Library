using System;
using System.Collections.Immutable;
using DropSort.Domain.Media.Matcher;
using DropSort.Domain.Metadata.Contracts;
using Xunit;

namespace DropSort.Tests.Domain.Media.Matcher;

public class MatcherModelsTests
{
    private MovieCandidate CreateCandidate() => new MovieCandidate(
        provider: "provider-a",
        externalId: "1",
        title: "Movie",
        originalTitle: null,
        year: 2024,
        overview: null,
        rating: null,
        posterReference: null);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CandidateScore_RejectsInvalidConfidence(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CandidateScore(CreateCandidate(), value, ImmutableArray<MatchReason>.Empty, ImmutableArray<MatchReason>.Empty));
    }

    [Fact]
    public void CandidateScore_RejectsNonCandidate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CandidateScore(null!, 0.0, ImmutableArray<MatchReason>.Empty, ImmutableArray<MatchReason>.Empty));
    }

    [Fact]
    public void Decision_RejectsNonEnumStatus()
    {
        var candidate = CreateCandidate();
        var score = new CandidateScore(candidate, 1.0, ImmutableArray.Create(MatchReason.TitleExact), ImmutableArray<MatchReason>.Empty);

        Assert.Throws<ArgumentException>(() =>
            new MatchDecision((MatchStatus)999, candidate, 1.0, ImmutableArray.Create(MatchReason.TitleExact), ImmutableArray.Create(score)));
    }

    [Fact]
    public void Decision_RejectsNonScoreRankings()
    {
        var candidate = CreateCandidate();
        Assert.Throws<ArgumentException>(() =>
            new MatchDecision(MatchStatus.Matched, candidate, 1.0, ImmutableArray<MatchReason>.Empty, default));
    }

    [Fact]
    public void MatchedDecision_RequiresCandidate()
    {
        Assert.Throws<ArgumentException>(() =>
            new MatchDecision(MatchStatus.Matched, null, 0.9, ImmutableArray<MatchReason>.Empty, ImmutableArray<CandidateScore>.Empty));
    }

    [Fact]
    public void NoMatch_CannotSelectCandidate()
    {
        var candidate = CreateCandidate();
        var score = new CandidateScore(candidate, 0.5, ImmutableArray<MatchReason>.Empty, ImmutableArray<MatchReason>.Empty);

        Assert.Throws<ArgumentException>(() =>
            new MatchDecision(MatchStatus.NoMatch, candidate, 0.5, ImmutableArray<MatchReason>.Empty, ImmutableArray.Create(score)));
    }

    [Fact]
    public void Review_RequiresTopRankedCandidate()
    {
        var candidate = CreateCandidate();
        var score = new CandidateScore(candidate, 0.7, ImmutableArray<MatchReason>.Empty, ImmutableArray<MatchReason>.Empty);
        var other = new MovieCandidate("provider-a", "2", "Other", null, 2024, null, null, null);

        Assert.Throws<ArgumentException>(() =>
            new MatchDecision(MatchStatus.ReviewRequired, other, 0.7, ImmutableArray<MatchReason>.Empty, ImmutableArray.Create(score)));
    }
}
