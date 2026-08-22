ALTER TABLE movies
    ADD COLUMN genres TEXT NOT NULL DEFAULT '[]';

CREATE INDEX idx_media_files_movie_id ON media_files(movie_id);
