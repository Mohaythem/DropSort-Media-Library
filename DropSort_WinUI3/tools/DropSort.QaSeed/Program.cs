using System.Globalization;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.QaSeed;
using Microsoft.Data.Sqlite;

// Usage: DropSort.QaSeed <dataRoot> <movieCount> <showCount> [seed]
//        DropSort.QaSeed verify <dbPath>
if (args.Length >= 2 && args[0] == "verify")
{
    var vp = Path.GetFullPath(args[1]);
    using var vc = new SqliteConnection("Data Source=" + vp + ";Mode=ReadOnly");
    vc.Open();
    string Scalar(string sql) { using var q = vc.CreateCommand(); q.CommandText = sql; using var r = q.ExecuteReader(); r.Read(); return r.GetValue(0).ToString() ?? ""; }
    Console.WriteLine("integrity_check=" + Scalar("PRAGMA integrity_check;"));
    Console.WriteLine("user_version=" + Scalar("PRAGMA user_version;"));
    Console.WriteLine("fk_violations=" + Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    Console.WriteLine("dup_movie_members=" + Scalar("SELECT COUNT(*) FROM (SELECT list_id,movie_id,COUNT(*) c FROM custom_list_members WHERE movie_id IS NOT NULL GROUP BY list_id,movie_id HAVING c>1);"));
    Console.WriteLine("dup_show_members=" + Scalar("SELECT COUNT(*) FROM (SELECT list_id,show_id,COUNT(*) c FROM custom_list_members WHERE show_id IS NOT NULL GROUP BY list_id,show_id HAVING c>1);"));
    Console.WriteLine("bad_member_rows=" + Scalar("SELECT COUNT(*) FROM custom_list_members WHERE NOT ((media_kind='MOVIE' AND movie_id IS NOT NULL AND show_id IS NULL) OR (media_kind='SHOW' AND show_id IS NOT NULL AND movie_id IS NULL));"));
    Console.WriteLine("orphan_members=" + Scalar("SELECT COUNT(*) FROM custom_list_members m LEFT JOIN custom_lists l ON l.id=m.list_id WHERE l.id IS NULL;"));
    return 0;
}

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: DropSort.QaSeed <dataRoot> <movieCount> <showCount> [seed]");
    return 1;
}

var dataRoot = Path.GetFullPath(args[0]);
var movieCount = int.Parse(args[1], CultureInfo.InvariantCulture);
var showCount = int.Parse(args[2], CultureInfo.InvariantCulture);
var seed = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 20260925;
var rng = new Random(seed);

Directory.CreateDirectory(dataRoot);
var dbPath = Path.Combine(dataRoot, "library.db");
if (File.Exists(dbPath)) File.Delete(dbPath);
foreach (var side in new[] { "-wal", "-shm" })
    if (File.Exists(dbPath + side)) File.Delete(dbPath + side);

var cs = "Data Source=" + dbPath + ";Pooling=False";
new DatabaseMigrator(cs).Migrate();

// Realistic title fragments, including non-ASCII and long forms; duplicate-looking titles stay distinct
// because each row has its own provider/external_id identity.
string[] words = ["Shadow", "Empire", "Nébula", "الليل", "Horizon", "Café", "München", "Voyage",
    "Crimson", "Éclipse", "Titan", "Zürich", "Odyssey", "Warden", "Mirage", "Ω Protocol"];
string Title(int i) => words[rng.Next(words.Length)] + " " + words[rng.Next(words.Length)] + " #" + i;
string LongTitle(int i) => string.Join(" ", Enumerable.Range(0, 12).Select(_ => words[rng.Next(words.Length)])) + " " + i;
var now = DateTimeOffset.UtcNow;

using var connection = new SqliteConnection(cs);
connection.Open();
using (var pragma = connection.CreateCommand()) { pragma.CommandText = "PRAGMA foreign_keys=ON;"; pragma.ExecuteNonQuery(); }

Seed.Movies(connection, movieCount, rng, Title, LongTitle, now);
Seed.Shows(connection, showCount, rng, Title, now);
Seed.CustomLists(connection, movieCount, showCount, rng, now);
Seed.WatchEvents(connection, movieCount, showCount, rng, now);

using (var check = connection.CreateCommand())
{
    check.CommandText = "PRAGMA integrity_check; PRAGMA user_version;";
    // integrity_check
    using var r = check.ExecuteReader();
    r.Read();
    Console.WriteLine($"integrity_check={r.GetString(0)}");
    r.NextResult();
    r.Read();
    Console.WriteLine($"user_version={r.GetInt32(0)}");
}

using (var counts = connection.CreateCommand())
{
    counts.CommandText = "SELECT (SELECT COUNT(*) FROM movies),(SELECT COUNT(*) FROM tv_shows),(SELECT COUNT(*) FROM tv_episodes),(SELECT COUNT(*) FROM custom_lists),(SELECT COUNT(*) FROM custom_list_members),(SELECT COUNT(*) FROM watch_events);";
    using var r = counts.ExecuteReader();
    r.Read();
    Console.WriteLine($"movies={r.GetInt32(0)} shows={r.GetInt32(1)} episodes={r.GetInt32(2)} lists={r.GetInt32(3)} members={r.GetInt32(4)} watch_events={r.GetInt32(5)}");
}
Console.WriteLine($"seeded: {dbPath}");
return 0;
