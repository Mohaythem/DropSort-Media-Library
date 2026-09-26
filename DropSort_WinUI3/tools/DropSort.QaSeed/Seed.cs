using Microsoft.Data.Sqlite;

namespace DropSort.QaSeed;

/// <summary>Bulk synthetic seeding against the production v13 schema. QA only.</summary>
internal static class Seed
{
    private static string Ts(DateTimeOffset t) => t.ToString("O");

    public static void Movies(SqliteConnection c, int count, Random rng,
        Func<int, string> title, Func<int, string> longTitle, DateTimeOffset now)
    {
        using var tx = c.BeginTransaction();
        using var movie = c.CreateCommand();
        movie.Transaction = tx;
        movie.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, year, overview, poster_path,
                               date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id,'tmdb',$ext,$title,$year,$ov,$poster,$da,$ca,$ua,'["Drama"]',$ms);
            """;
        var pId = movie.Parameters.Add("$id", SqliteType.Integer);
        var pExt = movie.Parameters.Add("$ext", SqliteType.Text);
        var pTitle = movie.Parameters.Add("$title", SqliteType.Text);
        var pYear = movie.Parameters.Add("$year", SqliteType.Integer);
        var pOv = movie.Parameters.Add("$ov", SqliteType.Text);
        var pPoster = movie.Parameters.Add("$poster", SqliteType.Text);
        var pDa = movie.Parameters.Add("$da", SqliteType.Text);
        var pCa = movie.Parameters.Add("$ca", SqliteType.Text);
        var pUa = movie.Parameters.Add("$ua", SqliteType.Text);
        var pMs = movie.Parameters.Add("$ms", SqliteType.Text);

        using var file = c.CreateCommand();
        file.Transaction = tx;
        file.CommandText = """
            INSERT INTO media_files(movie_id, current_path, path_key, file_size, extension, status,
                                    discovered_at, last_seen_at)
            VALUES ($mid,$path,$key,$size,'.mkv',$status,$da,$da);
            """;
        var fMid = file.Parameters.Add("$mid", SqliteType.Integer);
        var fPath = file.Parameters.Add("$path", SqliteType.Text);
        var fKey = file.Parameters.Add("$key", SqliteType.Text);
        var fSize = file.Parameters.Add("$size", SqliteType.Integer);
        var fStatus = file.Parameters.Add("$status", SqliteType.Text);
        var fDa = file.Parameters.Add("$da", SqliteType.Text);

        for (var i = 1; i <= count; i++)
        {
            var added = now.AddMinutes(-i * 7); // spread date_added so Date-added sort is meaningful
            pId.Value = i;
            pExt.Value = "m" + i;
            pTitle.Value = i % 250 == 0 ? longTitle(i) : title(i);
            pYear.Value = 1970 + rng.Next(56);
            pOv.Value = i % 5 == 0 ? "" : "Synthetic overview for QA item " + i;
            pPoster.Value = i % 7 == 0 ? DBNull.Value : (object)("/qa/poster_" + (i % 400) + ".jpg"); // ~1/7 missing poster
            pDa.Value = Ts(added);
            pCa.Value = Ts(added);
            pUa.Value = Ts(added);
            pMs.Value = (i % 11) switch { 0 => "PENDING", 1 => "NEEDS_MATCH", _ => "READY" };
            movie.ExecuteNonQuery();

            var kind = i % 10; // 0-6 present, 7-8 missing, 9 registered-only (no file)
            if (kind <= 8)
            {
                fMid.Value = i;
                var folder = i % 3 == 0 ? "Films \u00c9t\u00e9 \u0645\u062c\u0644\u062f" : "Movies With Spaces";
                fPath.Value = $"Z:\\QA Media\\{folder}\\{i}\\Feature Prés #{i}.mkv";
                fKey.Value = "z:/qa/" + i;
                fSize.Value = 1024L + i;
                fStatus.Value = kind <= 6 ? "PRESENT" : "MISSING";
                fDa.Value = Ts(added);
                file.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    // APPEND_SEED
    public static void Shows(SqliteConnection c, int count, Random rng, Func<int, string> title, DateTimeOffset now)
    {
        using var tx = c.BeginTransaction();
        for (var s = 1; s <= count; s++)
        {
            var added = now.AddMinutes(-s * 11);
            using (var show = c.CreateCommand())
            {
                show.Transaction = tx;
                show.CommandText = """
                    INSERT INTO tv_shows(id, provider, external_id, title, sort_title, year, genres,
                                         metadata_status, date_added, created_at, updated_at, poster_path)
                    VALUES ($id,'tmdb',$ext,$title,$sort,$year,'["Drama"]',$ms,$da,$da,$da,$poster);
                    """;
                show.Parameters.AddWithValue("$id", s);
                show.Parameters.AddWithValue("$ext", "s" + s);
                show.Parameters.AddWithValue("$title", title(s));
                show.Parameters.AddWithValue("$sort", "qa show " + s); // unique
                show.Parameters.AddWithValue("$year", 1990 + rng.Next(36));
                show.Parameters.AddWithValue("$ms", s % 9 == 0 ? "PENDING" : "READY");
                show.Parameters.AddWithValue("$da", Ts(added));
                show.Parameters.AddWithValue("$poster", s % 6 == 0 ? DBNull.Value : (object)("/qa/show_" + (s % 200) + ".jpg"));
                show.ExecuteNonQuery();
            }

            var seasons = 1 + rng.Next(3);
            for (var sn = 1; sn <= seasons; sn++)
            {
                long seasonId;
                using (var season = c.CreateCommand())
                {
                    season.Transaction = tx;
                    season.CommandText = "INSERT INTO tv_seasons(show_id, season_number, created_at, updated_at) VALUES ($sid,$n,$da,$da); SELECT last_insert_rowid();";
                    season.Parameters.AddWithValue("$sid", s);
                    season.Parameters.AddWithValue("$n", sn);
                    season.Parameters.AddWithValue("$da", Ts(added));
                    seasonId = (long)season.ExecuteScalar()!;
                }
                var episodes = 2 + rng.Next(6);
                for (var en = 1; en <= episodes; en++)
                {
                    long episodeId;
                    using (var ep = c.CreateCommand())
                    {
                        ep.Transaction = tx;
                        ep.CommandText = "INSERT INTO tv_episodes(season_id, episode_number, title, created_at, updated_at) VALUES ($se,$n,$t,$da,$da); SELECT last_insert_rowid();";
                        ep.Parameters.AddWithValue("$se", seasonId);
                        ep.Parameters.AddWithValue("$n", en);
                        ep.Parameters.AddWithValue("$t", $"Episode {en}");
                        ep.Parameters.AddWithValue("$da", Ts(added));
                        episodeId = (long)ep.ExecuteScalar()!;
                    }
                    if ((en % 3) != 0) // ~2/3 episodes present on disk
                    {
                        long fileId;
                        using (var mf = c.CreateCommand())
                        {
                            mf.Transaction = tx;
                            mf.CommandText = "INSERT INTO media_files(current_path, path_key, file_size, extension, status, discovered_at, last_seen_at) VALUES ($p,$k,$sz,'.mkv','PRESENT',$da,$da); SELECT last_insert_rowid();";
                            mf.Parameters.AddWithValue("$p", $"Z:\\QA Media\\Shows\\{s}\\S{sn:00}E{en:00}.mkv");
                            mf.Parameters.AddWithValue("$k", $"z:/qa/show/{s}/{sn}/{en}");
                            mf.Parameters.AddWithValue("$sz", 2048L + episodeId);
                            mf.Parameters.AddWithValue("$da", Ts(added));
                            fileId = (long)mf.ExecuteScalar()!;
                        }
                        using var link = c.CreateCommand();
                        link.Transaction = tx;
                        link.CommandText = "INSERT INTO episode_media_files(media_file_id, episode_id, linked_at) VALUES ($f,$e,$da);";
                        link.Parameters.AddWithValue("$f", fileId);
                        link.Parameters.AddWithValue("$e", episodeId);
                        link.Parameters.AddWithValue("$da", Ts(added));
                        link.ExecuteNonQuery();
                    }
                }
            }
        }
        tx.Commit();
    }

    public static void CustomLists(SqliteConnection c, int movieCount, int showCount, Random rng, DateTimeOffset now)
    {
        using var tx = c.BeginTransaction();
        string[] names = ["Weekend", "Horror", "Family", "Favorites 2026", "قائمة العربية"];
        var listIds = new List<long>();
        foreach (var n in names)
        {
            using var l = c.CreateCommand();
            l.Transaction = tx;
            l.CommandText = "INSERT INTO custom_lists(display_name, created_at) VALUES ($n,$da); SELECT last_insert_rowid();";
            l.Parameters.AddWithValue("$n", n);
            l.Parameters.AddWithValue("$da", Ts(now));
            listIds.Add((long)l.ExecuteScalar()!);
        }
        // Movies into (possibly multiple) lists; some movies span 2-3 lists.
        var target = Math.Min(movieCount, 1200);
        for (var i = 1; i <= target; i++)
        {
            var span = 1 + (i % 3);
            for (var k = 0; k < span; k++)
            {
                var list = listIds[(i + k) % listIds.Count];
                using var m = c.CreateCommand();
                m.Transaction = tx;
                m.CommandText = "INSERT OR IGNORE INTO custom_list_members(list_id, media_kind, movie_id, added_at) VALUES ($l,'MOVIE',$m,$da);";
                m.Parameters.AddWithValue("$l", list);
                m.Parameters.AddWithValue("$m", i);
                m.Parameters.AddWithValue("$da", Ts(now));
                m.ExecuteNonQuery();
            }
        }
        // Shows into lists too (mixed-media lists).
        for (var s = 1; s <= Math.Min(showCount, 400); s++)
        {
            var list = listIds[s % listIds.Count];
            using var m = c.CreateCommand();
            m.Transaction = tx;
            m.CommandText = "INSERT OR IGNORE INTO custom_list_members(list_id, media_kind, show_id, added_at) VALUES ($l,'SHOW',$s,$da);";
            m.Parameters.AddWithValue("$l", list);
            m.Parameters.AddWithValue("$s", s);
            m.Parameters.AddWithValue("$da", Ts(now));
            m.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void WatchEvents(SqliteConnection c, int movieCount, int showCount, Random rng, DateTimeOffset now)
    {
        using var tx = c.BeginTransaction();
        var movieWatches = Math.Min(movieCount, 300);
        for (var i = 1; i <= movieWatches; i++)
        {
            using var w = c.CreateCommand();
            w.Transaction = tx;
            w.CommandText = """
                INSERT INTO watch_events(media_kind, movie_id, watched_at, created_at, title_snapshot, year_snapshot, poster_reference)
                SELECT 'MOVIE', id, $wa, $wa, title, year, poster_path FROM movies WHERE id=$id;
                """;
            w.Parameters.AddWithValue("$wa", Ts(now.AddHours(-i)));
            w.Parameters.AddWithValue("$id", i);
            w.ExecuteNonQuery();
        }
        // Episode watches spread across several shows (exercises Recently-Watched show aggregation).
        using var ep = c.CreateCommand();
        ep.Transaction = tx;
        ep.CommandText = "SELECT e.id, e.episode_number, se.season_number, s.title, s.year FROM tv_episodes e JOIN tv_seasons se ON se.id=e.season_id JOIN tv_shows s ON s.id=se.show_id LIMIT 400;";
        var rows = new List<(long id, int en, int sn, string t, object yr)>();
        using (var r = ep.ExecuteReader())
            while (r.Read())
                rows.Add((r.GetInt64(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.IsDBNull(4) ? DBNull.Value : r.GetInt32(4)));
        var e = 0;
        foreach (var row in rows)
        {
            using var w = c.CreateCommand();
            w.Transaction = tx;
            w.CommandText = """
                INSERT INTO watch_events(media_kind, episode_id, watched_at, created_at, title_snapshot, show_title_snapshot, season_number_snapshot, episode_number_snapshot, year_snapshot)
                VALUES ('EPISODE',$e,$wa,$wa,$t,$st,$sn,$en,$yr);
                """;
            w.Parameters.AddWithValue("$e", row.id);
            w.Parameters.AddWithValue("$wa", Ts(now.AddHours(-(++e))));
            w.Parameters.AddWithValue("$t", $"Episode {row.en}");
            w.Parameters.AddWithValue("$st", row.t);
            w.Parameters.AddWithValue("$sn", row.sn);
            w.Parameters.AddWithValue("$en", row.en);
            w.Parameters.AddWithValue("$yr", row.yr);
            w.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
