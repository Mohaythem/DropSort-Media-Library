CREATE TABLE movie_personal_state (
    movie_id INTEGER PRIMARY KEY REFERENCES movies(id) ON DELETE CASCADE,
    preference TEXT NOT NULL DEFAULT 'NO_OPINION'
        CHECK(preference IN ('NO_OPINION', 'LIKED', 'BLACKLISTED')),
    watchlist_added_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX idx_movie_personal_state_preference
    ON movie_personal_state(preference);

CREATE INDEX idx_movie_personal_state_watchlist
    ON movie_personal_state(watchlist_added_at);

CREATE TABLE watch_events (
    id INTEGER PRIMARY KEY,
    movie_id INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
    watched_at TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE INDEX idx_watch_events_movie_watched
    ON watch_events(movie_id, watched_at, id);
