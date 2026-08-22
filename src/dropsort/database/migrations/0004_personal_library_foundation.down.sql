CREATE TEMP TABLE personal_library_downgrade_guard (
    safe_to_remove_personal_data INTEGER NOT NULL
        CHECK(safe_to_remove_personal_data = 1)
);

INSERT INTO personal_library_downgrade_guard(safe_to_remove_personal_data)
SELECT CASE WHEN EXISTS (SELECT 1 FROM watch_events)
                 OR EXISTS (SELECT 1 FROM movie_personal_state)
            THEN 0 ELSE 1 END;

DROP INDEX idx_watch_events_movie_watched;
DROP TABLE watch_events;
DROP INDEX idx_movie_personal_state_preference;
DROP INDEX idx_movie_personal_state_watchlist;
DROP TABLE movie_personal_state;

DROP TABLE personal_library_downgrade_guard;
