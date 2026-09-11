using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Domain.Media.Matcher;

public static class MetadataScorer
{
    public const double AutoMatchThreshold = 0.80;
    public const double ReviewThreshold = 0.50;
    public const double AmbiguityScoreThreshold = 0.65;
    public const double AmbiguityScoreDelta = 0.15;

    public static string NormalizeTitle(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(' ');
            }
        }

        var parts = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    public static double WordOverlap(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        var words1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (words1.Count == 0 && words2.Count == 0) return 1.0;
        if (words1.Count == 0 || words2.Count == 0) return 0.0;

        var intersectionCount = words1.Count(w => words2.Contains(w));
        var unionCount = words1.Union(words2, StringComparer.OrdinalIgnoreCase).Count();

        return unionCount == 0 ? 1.0 : (double)intersectionCount / unionCount;
    }

    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[] d0 = new int[m + 1];
        int[] d1 = new int[m + 1];

        for (int j = 0; j <= m; j++) d0[j] = j;

        for (int i = 0; i < n; i++)
        {
            d1[0] = i + 1;
            for (int j = 0; j < m; j++)
            {
                int cost = s[i] == t[j] ? 0 : 1;
                d1[j + 1] = Math.Min(Math.Min(d1[j] + 1, d0[j + 1] + 1), d0[j] + cost);
            }
            for (int j = 0; j <= m; j++) d0[j] = d1[j];
        }

        return d0[m];
    }

    public static double LevenshteinRatio(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 1.0;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

        int maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0) return 1.0;

        int dist = LevenshteinDistance(s1, s2);
        return Math.Max(0.0, 1.0 - ((double)dist / maxLen));
    }

    public static double CalculateTitleSimilarity(string queryNormalized, string candidateNormalized)
    {
        if (queryNormalized == candidateNormalized) return 1.0;
        if (string.IsNullOrEmpty(queryNormalized) || string.IsNullOrEmpty(candidateNormalized)) return 0.0;

        double overlap = WordOverlap(queryNormalized, candidateNormalized);
        double lev = LevenshteinRatio(queryNormalized, candidateNormalized);
        return (overlap + lev) / 2.0;
    }

    public static MatchDecision Score(string queryTitle, int? queryYear, IEnumerable<MovieCandidate>? candidates)
    {
        var list = candidates?.ToList() ?? [];
        var normalizedQuery = NormalizeTitle(queryTitle);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new MatchDecision(
                MatchStatus.NoMatch,
                null,
                0.0,
                ImmutableArray.Create(MatchReason.InvalidParsedTitle),
                ImmutableArray<CandidateScore>.Empty);
        }

        if (list.Count == 0)
        {
            return new MatchDecision(
                MatchStatus.NoMatch,
                null,
                0.0,
                ImmutableArray.Create(MatchReason.NoCandidates),
                ImmutableArray<CandidateScore>.Empty);
        }

        var scoredList = list.Select(c =>
        {
            var (score, matchReasons, penaltyReasons) = ScoreSingleCandidate(
                normalizedQuery, queryYear, c.Title, c.OriginalTitle, c.Year);
            return new ScoredResult<MovieCandidate>(c, score, matchReasons, penaltyReasons);
        }).OrderByDescending(s => s.Score).ToList();

        var rankedCandidates = scoredList
            .Select(s => new CandidateScore(s.Candidate, s.Score, s.MatchReasons, s.PenaltyReasons))
            .ToImmutableArray();

        var (status, winner, confidence, reasons) = DetermineDecision(scoredList, normalizedQuery);

        return new MatchDecision(status, winner, confidence, reasons, rankedCandidates);
    }

    public static TvMatchDecision Score(string queryTitle, int? queryYear, IEnumerable<TvCandidate>? candidates)
    {
        var list = candidates?.ToList() ?? [];
        var normalizedQuery = NormalizeTitle(queryTitle);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new TvMatchDecision(
                MatchStatus.NoMatch,
                null,
                0.0,
                ImmutableArray.Create(MatchReason.InvalidParsedTitle),
                ImmutableArray<TvCandidateScore>.Empty);
        }

        if (list.Count == 0)
        {
            return new TvMatchDecision(
                MatchStatus.NoMatch,
                null,
                0.0,
                ImmutableArray.Create(MatchReason.NoCandidates),
                ImmutableArray<TvCandidateScore>.Empty);
        }

        var scoredList = list.Select(c =>
        {
            var (score, matchReasons, penaltyReasons) = ScoreSingleCandidate(
                normalizedQuery, queryYear, c.Title, c.OriginalTitle, c.FirstAirYear);
            return new ScoredResult<TvCandidate>(c, score, matchReasons, penaltyReasons);
        }).OrderByDescending(s => s.Score).ToList();

        var rankedCandidates = scoredList
            .Select(s => new TvCandidateScore(s.Candidate, s.Score, s.MatchReasons, s.PenaltyReasons))
            .ToImmutableArray();

        var (status, winner, confidence, reasons) = DetermineDecision(scoredList, normalizedQuery);

        return new TvMatchDecision(status, winner, confidence, reasons, rankedCandidates);
    }

    public static TvMatchDecision ScoreTv(string queryTitle, int? queryYear, IEnumerable<TvCandidate>? candidates) =>
        Score(queryTitle, queryYear, candidates);

    private sealed record ScoredResult<T>(
        T Candidate,
        double Score,
        ImmutableArray<MatchReason> MatchReasons,
        ImmutableArray<MatchReason> PenaltyReasons);

    private static (double Score, ImmutableArray<MatchReason> MatchReasons, ImmutableArray<MatchReason> PenaltyReasons) ScoreSingleCandidate(
        string normalizedQuery,
        int? queryYear,
        string candidateTitle,
        string? candidateOriginalTitle,
        int? candidateYear)
    {
        var matchReasons = ImmutableArray.CreateBuilder<MatchReason>();
        var penaltyReasons = ImmutableArray.CreateBuilder<MatchReason>();

        var normalizedCandidateTitle = NormalizeTitle(candidateTitle);
        var titleSim = CalculateTitleSimilarity(normalizedQuery, normalizedCandidateTitle);

        var isExactTitle = string.Equals(normalizedQuery, normalizedCandidateTitle, StringComparison.Ordinal);
        var isStrongTitle = titleSim >= 0.80;

        if (isExactTitle)
        {
            matchReasons.Add(MatchReason.TitleExact);
        }
        else if (isStrongTitle)
        {
            matchReasons.Add(MatchReason.TitleStrong);
        }
        else if (titleSim < 0.50)
        {
            penaltyReasons.Add(MatchReason.WeakTitleSimilarity);
        }

        if (!string.IsNullOrWhiteSpace(candidateOriginalTitle))
        {
            var normalizedOriginal = NormalizeTitle(candidateOriginalTitle);
            var origSim = CalculateTitleSimilarity(normalizedQuery, normalizedOriginal);
            if (string.Equals(normalizedQuery, normalizedOriginal, StringComparison.Ordinal))
            {
                matchReasons.Add(MatchReason.OriginalTitleExact);
                if (origSim > titleSim) titleSim = origSim;
            }
            else if (origSim >= 0.80)
            {
                matchReasons.Add(MatchReason.OriginalTitleStrong);
                if (origSim > titleSim) titleSim = origSim;
            }
        }

        double yearAdjustment = 0.0;
        if (queryYear.HasValue && candidateYear.HasValue)
        {
            int diff = Math.Abs(queryYear.Value - candidateYear.Value);
            if (diff == 0)
            {
                yearAdjustment = 0.25;
                matchReasons.Add(MatchReason.YearExact);
            }
            else if (diff == 1)
            {
                yearAdjustment = 0.10;
            }
            else
            {
                yearAdjustment = -0.30;
                penaltyReasons.Add(MatchReason.YearConflict);
            }
        }
        else if (!queryYear.HasValue)
        {
            matchReasons.Add(MatchReason.ParsedYearMissing);
        }
        else
        {
            matchReasons.Add(MatchReason.CandidateYearMissing);
        }

        double rawScore = (titleSim * 0.75) + yearAdjustment;
        double clampedScore = Math.Round(Math.Clamp(rawScore, 0.0, 1.0), 4);

        return (clampedScore, matchReasons.ToImmutable(), penaltyReasons.ToImmutable());
    }

    private static (MatchStatus Status, T? Winner, double Confidence, ImmutableArray<MatchReason> Reasons) DetermineDecision<T>(
        IReadOnlyList<ScoredResult<T>> ranked,
        string normalizedQuery) where T : class
    {
        var decisionReasons = ImmutableArray.CreateBuilder<MatchReason>();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            decisionReasons.Add(MatchReason.InvalidParsedTitle);
            return (MatchStatus.NoMatch, null, 0.0, decisionReasons.ToImmutable());
        }

        if (ranked.Count == 0)
        {
            decisionReasons.Add(MatchReason.NoCandidates);
            return (MatchStatus.NoMatch, null, 0.0, decisionReasons.ToImmutable());
        }

        var top = ranked[0];

        bool isAmbiguous = ranked.Count >= 2
            && (top.Score - ranked[1].Score) < AmbiguityScoreDelta
            && top.Score >= AmbiguityScoreThreshold;

        if (isAmbiguous)
        {
            decisionReasons.Add(MatchReason.AmbiguousTopCandidates);
            decisionReasons.AddRange(top.MatchReasons);
            return (MatchStatus.ReviewRequired, top.Candidate, top.Score, decisionReasons.ToImmutable());
        }

        if (top.Score >= AutoMatchThreshold)
        {
            decisionReasons.Add(MatchReason.ClearWinner);
            decisionReasons.AddRange(top.MatchReasons);
            return (MatchStatus.Matched, top.Candidate, top.Score, decisionReasons.ToImmutable());
        }

        if (top.Score >= ReviewThreshold)
        {
            decisionReasons.Add(MatchReason.BelowAutoMatchThreshold);
            decisionReasons.AddRange(top.MatchReasons);
            return (MatchStatus.ReviewRequired, top.Candidate, top.Score, decisionReasons.ToImmutable());
        }

        decisionReasons.Add(MatchReason.BelowAutoMatchThreshold);
        if (top.PenaltyReasons.Contains(MatchReason.WeakTitleSimilarity))
        {
            decisionReasons.Add(MatchReason.WeakTitleSimilarity);
        }
        return (MatchStatus.NoMatch, null, 0.0, decisionReasons.ToImmutable());
    }
}
