CREATE TEMP TABLE movie_catalog_downgrade_guard (
    safe_to_remove_genres INTEGER NOT NULL CHECK(safe_to_remove_genres = 1)
);

INSERT INTO movie_catalog_downgrade_guard(safe_to_remove_genres)
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM movies WHERE genres != '[]'
) THEN 0 ELSE 1 END;

DROP INDEX idx_media_files_movie_id;

ALTER TABLE movies DROP COLUMN genres;

DROP TABLE movie_catalog_downgrade_guard;
