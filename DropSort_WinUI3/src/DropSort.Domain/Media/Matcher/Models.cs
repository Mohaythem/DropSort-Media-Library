using System;
using System.Collections.Immutable;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Domain.Media.Matcher;

public enum MatchStatus
{
    Matched,
    ReviewRequired,
    NoMatch
}

public enum MatchReason
{
    TitleExact,
    TitleStrong,
    OriginalTitleExact,
    OriginalTitleStrong,
    WeakTitleSimilarity,
    YearExact,
    YearConflict,
    ParsedYearMissing,
    CandidateYearMissing,
    AmbiguousTopCandidates,
    BelowAutoMatchThreshold,
    NoCandidates,
    InvalidParsedTitle,
    MediaTypeNotMovie,
    ClearWinner
}

public record CandidateScore
{
    public MovieCandidate Candidate { get; }
    public double Score { get; }
    public ImmutableArray<MatchReason> MatchReasons { get; }
    public ImmutableArray<MatchReason> PenaltyReasons { get; }

    public CandidateScore(MovieCandidate candidate, double score, ImmutableArray<MatchReason> matchReasons, ImmutableArray<MatchReason> penaltyReasons)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (double.IsNaN(score) || double.IsInfinity(score) || score < 0.0 || score > 1.0)
            throw new ArgumentOutOfRangeException(nameof(score), "score must be between 0.0 and 1.0");

        Candidate = candidate;
        Score = score;
        MatchReasons = matchReasons.IsDefault ? ImmutableArray<MatchReason>.Empty : matchReasons;
        PenaltyReasons = penaltyReasons.IsDefault ? ImmutableArray<MatchReason>.Empty : penaltyReasons;
    }
}

public record MatchDecision
{
    public MatchStatus Status { get; }
    public MovieCandidate? Candidate { get; }
    public double Confidence { get; }
    public ImmutableArray<MatchReason> Reasons { get; }
    public ImmutableArray<CandidateScore> RankedCandidates { get; }

    public MatchDecision(
        MatchStatus status,
        MovieCandidate? candidate,
        double confidence,
        ImmutableArray<MatchReason> reasons,
        ImmutableArray<CandidateScore> rankedCandidates)
    {
        if (!Enum.IsDefined(typeof(MatchStatus), status)) throw new ArgumentException("Invalid status", nameof(status));
        if (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidence), "confidence must be between 0.0 and 1.0");
        if (rankedCandidates.IsDefault) throw new ArgumentException("ranked_candidates cannot be default");

        if (status == MatchStatus.Matched && candidate == null)
            throw new ArgumentException("Matched decisions require a candidate");
        
        if (status == MatchStatus.NoMatch && candidate != null)
            throw new ArgumentException("NO_MATCH decisions cannot select a candidate");
        
        if (status == MatchStatus.ReviewRequired && candidate != null)
        {
            if (rankedCandidates.Length > 0 && rankedCandidates[0].Candidate != candidate)
                throw new ArgumentException("Candidate for review must be the top-ranked candidate");
        }

        Status = status;
        Candidate = candidate;
        Confidence = confidence;
        Reasons = reasons.IsDefault ? ImmutableArray<MatchReason>.Empty : reasons;
        RankedCandidates = rankedCandidates;
    }
}

public record TvCandidateScore
{
    public TvCandidate Candidate { get; }
    public double Score { get; }
    public ImmutableArray<MatchReason> MatchReasons { get; }
    public ImmutableArray<MatchReason> PenaltyReasons { get; }

    public TvCandidateScore(TvCandidate candidate, double score, ImmutableArray<MatchReason> matchReasons, ImmutableArray<MatchReason> penaltyReasons)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (double.IsNaN(score) || double.IsInfinity(score) || score < 0.0 || score > 1.0)
            throw new ArgumentOutOfRangeException(nameof(score), "score must be between 0.0 and 1.0");

        Candidate = candidate;
        Score = score;
        MatchReasons = matchReasons.IsDefault ? ImmutableArray<MatchReason>.Empty : matchReasons;
        PenaltyReasons = penaltyReasons.IsDefault ? ImmutableArray<MatchReason>.Empty : penaltyReasons;
    }
}

public record TvMatchDecision
{
    public MatchStatus Status { get; }
    public TvCandidate? Candidate { get; }
    public double Confidence { get; }
    public ImmutableArray<MatchReason> Reasons { get; }
    public ImmutableArray<TvCandidateScore> RankedCandidates { get; }

    public TvMatchDecision(
        MatchStatus status,
        TvCandidate? candidate,
        double confidence,
        ImmutableArray<MatchReason> reasons,
        ImmutableArray<TvCandidateScore> rankedCandidates)
    {
        if (!Enum.IsDefined(typeof(MatchStatus), status)) throw new ArgumentException("Invalid status", nameof(status));
        if (double.IsNaN(confidence) || double.IsInfinity(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidence), "confidence must be between 0.0 and 1.0");
        if (rankedCandidates.IsDefault) throw new ArgumentException("ranked_candidates cannot be default");

        if (status == MatchStatus.Matched && candidate == null)
            throw new ArgumentException("Matched decisions require a candidate");
        
        if (status == MatchStatus.NoMatch && candidate != null)
            throw new ArgumentException("NO_MATCH decisions cannot select a candidate");
        
        if (status == MatchStatus.ReviewRequired && candidate != null)
        {
            if (rankedCandidates.Length > 0 && rankedCandidates[0].Candidate != candidate)
                throw new ArgumentException("Candidate for review must be the top-ranked candidate");
        }

        Status = status;
        Candidate = candidate;
        Confidence = confidence;
        Reasons = reasons.IsDefault ? ImmutableArray<MatchReason>.Empty : reasons;
        RankedCandidates = rankedCandidates;
    }
}
