using System.Globalization;

namespace DropSort.UI.Models;

public static class DemoData
{
    public static IReadOnlyList<MovieRecord> Movies { get; } = CreateMovies();

    public static IReadOnlyList<TVShowRecord> Shows { get; } = CreateShows();

    /// <summary>
    /// The four lists the design ships: three system lists plus one user list. The user list is the
    /// only one that can be renamed or deleted, which is what drives those two buttons.
    /// </summary>
    public static IReadOnlyList<MediaListDefinition> Lists { get; } =
    [
        new("watchlist", "Watchlist", true),
        new("favorites", "Favorites", true),
        new("watch-later", "WatchLater", true),
        new("thrillers", "BestThrillers", false),
    ];

    public static IReadOnlyList<DetectedMovieRecord> DetectedMovies { get; } =
    [
        new("Dune: Part Two", 2024, "1080p", "Dune.Part.Two.2024.1080p.mkv", true),
        new("Oppenheimer", 2023, "4K", "Oppenheimer.2023.2160p.mp4", true),
        new("Poor Things", 2023, "SD", "Poor_Things_CAM.avi", false),
    ];

    public static IReadOnlyList<DetectedEpisodeRecord> DetectedEpisodes { get; } =
    [
        new("S01E01", "Pilot", "Breaking.Bad.S01E01.1080p.mkv", true),
        new("S01E02", "Cat's in the Bag...", "Breaking.Bad.S01E02.1080p.mkv", true),
        new("S01E03", "...And the Bag's in the River", "Breaking.Bad.1x03.mkv", false),
    ];

    /// <summary>Totals reported by the Check Library page.</summary>
    public static int CheckTotal => 428;

    public static int CheckPassed => 425;

    public static IReadOnlyList<LibraryIssueRecord> Issues { get; } =
    [
        new("Breaking Bad · S01E04", "Episode file is missing"),
        new("Synecdoche, New York", "Movie file is missing"),
        new("The Turin Horse", "Metadata needs review"),
    ];

    public static IReadOnlyList<OperationLogRecord> Operations { get; } =
    [
        new("2026-08-31 14:32:07", "14:32", "Episode Added", "Breaking Bad · S01E01", "success", "Matched TMDB episode 349232"),
        new("2026-08-31 14:31:52", "14:31", "Movie Added", "Dune: Part Two", "success", "Matched TMDB ID 693134"),
        new("2026-08-31 14:20:11", "14:20", "Check Library", "3 items need attention", "warning", "425 passed, 3 need attention"),
        new("2026-08-31 13:54:38", "13:54", "File Organized", "Oppenheimer.2023.4K.mkv", "success", "Moved to the approved Movies root"),
    ];

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

    private static IReadOnlyList<MovieRecord> CreateMovies()
    {
        var seed = new (string Title, int Year, string Runtime, double Rating, string[] Genres, string Overview, string? Original, string? Preference, bool Watchlist, bool File, string? Facts)[]
        {
            ("The Wind Rises", 2013, "2h 6m", 7.8, ["Animation", "Drama", "History", "Romance"], "A look at the life of Jiro Horikoshi, the engineer whose imagination changed aviation.", "Kaze Tachinu", "liked", false, true, "1080p · BluRay · x265 · 1.4 GB"),
            ("Dune: Part Two", 2024, "2h 46m", 8.0, ["Science Fiction", "Adventure", "Drama"], "Paul Atreides unites with Chani and the Fremen while seeking justice for his family.", null, null, true, true, "4K HDR · x265 · 38 GB"),
            ("Oppenheimer", 2023, "3h", 8.3, ["Drama", "History", "Thriller"], "The story of J. Robert Oppenheimer and his role in the development of the atomic bomb.", null, "liked", false, true, "4K UHD · BluRay · x265 · 55 GB"),
            ("Past Lives", 2023, "1h 46m", 7.9, ["Drama", "Romance"], "Two childhood friends reconnect decades after one family emigrates from South Korea.", null, null, true, false, null),
            ("Poor Things", 2023, "2h 21m", 7.9, ["Science Fiction", "Comedy", "Drama"], "A young woman embarks on a sweeping adventure across Europe.", null, "liked", false, true, "1080p · BluRay · x265 · 9 GB"),
            ("The Zone of Interest", 2023, "1h 45m", 7.3, ["Drama", "History", "War"], "A family strives to build a dream life beside an unthinkable reality.", null, "blacklisted", false, false, null),
            ("Killers of the Flower Moon", 2023, "3h 26m", 7.5, ["Crime", "Drama", "History"], "Members of the Osage Nation are murdered under mysterious circumstances.", null, null, true, true, "1080p · WEBRip · x265 · 12 GB"),
            ("Perfect Days", 2023, "2h 4m", 7.9, ["Drama"], "A Tokyo cleaner finds contentment through routine, music, and nature.", "パーフェクト デイズ", null, true, false, null),
            ("Anatomy of a Fall", 2023, "2h 32m", 7.7, ["Drama", "Mystery", "Thriller"], "A woman is suspected of murdering her husband after his unexplained death.", "Anatomie d'une chute", null, false, true, "1080p · BluRay · x265 · 11 GB"),
            ("All of Us Strangers", 2023, "1h 45m", 7.8, ["Drama", "Fantasy", "Romance"], "A screenwriter begins a relationship that draws him back toward memory.", null, "liked", false, false, null),
            ("May December", 2023, "1h 57m", 6.8, ["Drama"], "An actress researches a couple and finds reality becoming difficult to separate from performance.", null, null, false, false, null),
            ("Society of the Snow", 2023, "2h 24m", 7.8, ["Drama", "History", "Thriller"], "Survivors of a plane crash in the Andes face an agonizing fight to stay alive.", "La Sociedad de la Nieve", null, false, true, "1080p · WEBRip · x265 · 8 GB"),
            ("Fallen Leaves", 2023, "1h 21m", 7.5, ["Comedy", "Drama", "Romance"], "Two lonely Helsinki residents meet by chance and are drawn to one another.", "Kuolleet lehdet", null, false, false, null),
            ("Showing Up", 2022, "1h 48m", 6.8, ["Drama", "Comedy"], "A sculptor preparing for a show navigates relationships and everyday life.", null, null, false, false, null),
            ("Tár", 2022, "2h 38m", 7.5, ["Drama", "Music"], "A celebrated conductor faces the consequences of power and self-mythology.", null, "liked", false, false, null),
            ("The Banshees of Inisherin", 2022, "1h 54m", 7.7, ["Comedy", "Drama"], "Two lifelong friends reach an impasse when one abruptly ends their friendship.", null, null, true, true, "1080p · BluRay · x265 · 7 GB"),
        };

        return seed.Select((item, index) =>
        {
            var fileName = item.File
                ? $"{item.Title.Replace(':', ' ').Replace(' ', '.')}.{item.Year.ToString(CultureInfo.InvariantCulture)}.1080p.x265.mkv"
                : null;
            var filePath = fileName is null ? null : $@"D:\Movies\{item.Title} ({item.Year})\{fileName}";
            IReadOnlyList<WatchHistoryRecord> history = index switch
            {
                0 => [new WatchHistoryRecord("Aug 14, 2026", "Rewatch"), new WatchHistoryRecord("Mar 02, 2024", "First watch")],
                2 => [new WatchHistoryRecord("Jul 21, 2023", "First watch")],
                4 => [new WatchHistoryRecord("Jan 12, 2024", "First watch")],
                _ => [],
            };

            return new MovieRecord(
                index,
                item.Title,
                item.Year,
                item.Runtime,
                item.Rating,
                item.Genres,
                item.Overview,
                item.Original,
                item.Preference,
                item.Watchlist,
                item.File,
                fileName,
                filePath,
                item.Facts,
                history);
        }).ToArray();
    }

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
