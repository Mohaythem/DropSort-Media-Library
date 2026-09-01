using System.Globalization;

namespace DropSort.UI.Models;

/// <summary>
/// The bundled TV sample.
/// <para>
/// Movies, lists, detected media, library issues and the operations journal all come from the real
/// catalog now. What is left here is the TV show sample, because the catalog has no season or episode
/// tables yet - the TV pages are the one part of the shell that is still sample data, and this is the
/// single place it lives.
/// </para>
/// </summary>
public static class DemoData
{
    public static IReadOnlyList<TVShowRecord> Shows { get; } = CreateShows();

    public static IReadOnlyList<ContinueWatchingRecord> ContinueWatching { get; } = CreateContinueWatching();

    private static IReadOnlyList<ContinueWatchingRecord> CreateContinueWatching() =>
        Shows.Select(show => (Show: show, Next: show.NextPlayableEpisode))
            .Where(item => item.Next is not null)
            .Select(item =>
            {
                var next = item.Next!;
                var code = ShowFormatting.EpisodeCode(next.SeasonNumber, next.EpisodeNumber);
                return new ContinueWatchingRecord(
                    item.Show.Id,
                    item.Show.Title,
                    $"{code} · {next.Episode.Title}");
            })
            .ToArray();

    private static IReadOnlyList<TVShowRecord> CreateShows() =>
    [
        new(0, "Breaking Bad", 2008, ["Drama", "Crime"],
            "A chemistry teacher diagnosed with cancer partners with a former student to secure his family's future.",
            [
                new(1,
                [
                    new(1, "Pilot", "58 min", true, true),
                    new(2, "Cat's in the Bag...", "48 min", true, true),
                    new(3, "...And the Bag's in the River", "48 min", false, true),
                    new(4, "Cancer Man", "48 min", false, false),
                ]),
                new(2, GenericEpisodes(6, "47 min", 2)),
            ]),
        new(1, "Better Call Saul", 2015, ["Drama", "Crime"],
            "Jimmy McGill's transformation into the criminal lawyer Saul Goodman.",
            [
                new(1, GenericEpisodes(8, "49 min", 5)),
                new(2, GenericEpisodes(8, "50 min", 1)),
            ]),
        new(2, "Severance", 2022, ["Drama", "Mystery", "Science Fiction"],
            "Employees at Lumon Industries undergo a procedure that divides their work and personal memories.",
            [
                new(1, GenericEpisodes(9, "50 min", 4)),
            ]),
    ];

    private static IReadOnlyList<EpisodeRecord> GenericEpisodes(int count, string runtime, int watched) =>
        Enumerable.Range(1, count)
            .Select(number => new EpisodeRecord(
                number,
                $"Episode {number.ToString(CultureInfo.InvariantCulture)}",
                runtime,
                number <= watched,
                true))
            .ToArray();
}
