from __future__ import annotations

from datetime import datetime, timedelta, timezone
from hashlib import sha256
from pathlib import Path
import threading

import pytest

from dropsort.application.use_cases import (
    AddToWatchlist,
    ClearPersonalPreference,
    EnsureLogicalMovie,
    GetPersonalMovieState,
    GetWatchHistory,
    QueryReadyToWatch,
    RecordWatch,
    RemoveFromWatchlist,
    RemoveWatchEvent,
    SetPersonalPreference,
)
from dropsort.application.use_cases.clear_library_data import ClearLibraryData
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteLibraryMaintenanceRepository,
    SqliteMovieRepository,
    SqlitePersonalLibraryRepository,
)
from dropsort.library.movies import (
    MediaFileStatus,
    MovieCatalogData,
    MovieIdentityConflictError,
    VerifiedMediaFileFacts,
)
from dropsort.library.personal import (
    PersonalMovieNotFoundError,
    PersonalLibrarySection,
    PersonalPreference,
    WatchEventNotFoundError,
)


NOW = datetime(2026, 8, 16, 12, 0, tzinfo=timezone.utc)


class PosterCache:
    def clear(self) -> int:
        return 0


def _data(external_id: str, title: str | None = None) -> MovieCatalogData:
    return MovieCatalogData(
        provider="tmdb",
        external_id=external_id,
        title=title or f"Movie {external_id}",
        original_title=None,
        year=None,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=8.0,
        poster_reference=None,
    )


def _movie(harness, external_id: str):
    return SqliteMovieRepository(harness.database).create(_data(external_id), now=NOW)


def _file(harness, movie_id: int, path: Path, *, status: MediaFileStatus = MediaFileStatus.PRESENT):
    path.write_bytes(b"test media")
    media = MediaFileRepository(harness.database).add(
        VerifiedMediaFileFacts(
            current_path=path.absolute(),
            file_size=path.stat().st_size,
            extension=".mkv",
            resolution="1080p",
            codec="x264",
            source="test",
            observed_at=NOW,
        ),
        movie_id,
    )
    if status is MediaFileStatus.MISSING:
        MediaFileRepository(harness.database).mark_missing(media.id)
    return media.id


def test_logical_movie_is_fileless_and_reuses_provider_identity(harness) -> None:
    movies = SqliteMovieRepository(harness.database)
    ensure = EnsureLogicalMovie(movies)

    first = ensure.execute(_data("157336", "Interstellar"))
    second = ensure.execute(_data("157336", "A different title"))

    assert first == second
    assert movies.list_all() == (first,)
    with harness.database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0] == 0


def test_logical_movie_recovers_from_provider_identity_race(harness) -> None:
    movie = _movie(harness, "race")

    class RacingRepository:
        def __init__(self) -> None:
            self.lookups = 0

        def get_by_external_id(self, provider: str, external_id: str):
            self.lookups += 1
            return None if self.lookups == 1 else movie

        def create(self, data, *, now):
            raise MovieIdentityConflictError("concurrent identity insert")

    result = EnsureLogicalMovie(RacingRepository()).execute(_data("race"))
    assert result == movie


def test_preference_is_exclusive_and_clear_returns_to_no_opinion(harness) -> None:
    movie = _movie(harness, "preference")
    repository = SqlitePersonalLibraryRepository(harness.database)
    set_preference = SetPersonalPreference(repository, now=lambda: NOW)
    get_state = GetPersonalMovieState(repository)

    assert get_state.execute(movie.id).preference is PersonalPreference.NO_OPINION
    assert set_preference.execute(movie.id, PersonalPreference.LIKED).preference is PersonalPreference.LIKED
    assert set_preference.execute(movie.id, PersonalPreference.BLACKLISTED).preference is PersonalPreference.BLACKLISTED
    assert ClearPersonalPreference(repository, now=lambda: NOW).execute(movie.id).preference is PersonalPreference.NO_OPINION
    assert SetPersonalPreference(repository, now=lambda: NOW).execute(movie.id, PersonalPreference.NO_OPINION).preference is PersonalPreference.NO_OPINION

    with pytest.raises(ValueError, match="preference"):
        set_preference.execute(movie.id, "LIKED")  # type: ignore[arg-type]

    with pytest.raises(ValueError, match="timezone-aware"):
        SetPersonalPreference(repository, now=lambda: datetime(2026, 1, 1)).execute(
            movie.id, PersonalPreference.LIKED
        )


def test_watch_history_derives_rewatch_count_and_last_watched(harness) -> None:
    movie = _movie(harness, "watch-history")
    default_time_movie = _movie(harness, "watch-now")
    repository = SqlitePersonalLibraryRepository(harness.database)
    record = RecordWatch(repository, now=lambda: NOW)
    history = GetWatchHistory(repository)

    first = record.execute(movie.id, NOW + timedelta(days=4))
    second = record.execute(movie.id, NOW + timedelta(days=5))
    third = record.execute(movie.id, NOW + timedelta(days=6))
    assert first.rewatch is False
    assert second.rewatch is True
    assert third.rewatch is True
    assert record.execute(default_time_movie.id).created_at == NOW

    historical = record.execute(movie.id, NOW + timedelta(days=1))
    events = history.execute(movie.id)
    assert [event.id for event in events] == [historical.id, first.id, second.id, third.id]
    assert [event.rewatch for event in events] == [False, True, True, True]

    removed = RemoveWatchEvent(repository).execute(first.id)
    assert removed.id == first.id
    state = GetPersonalMovieState(repository).execute(movie.id)
    assert state.watch_count == 3
    assert state.last_watched == NOW + timedelta(days=6)
    assert state.watched is True


def test_personal_repository_rejects_missing_records_and_invalid_bounds(harness) -> None:
    repository = SqlitePersonalLibraryRepository(harness.database)
    with pytest.raises(PersonalMovieNotFoundError):
        repository.get_state(999)
    with pytest.raises(ValueError, match="movie_id"):
        repository.get_state(0)
    with pytest.raises(WatchEventNotFoundError):
        RemoveWatchEvent(repository).execute(999)
    with pytest.raises(ValueError, match="event_id"):
        RemoveWatchEvent(repository).execute(0)
    with pytest.raises(ValueError, match="limit"):
        QueryReadyToWatch(repository).execute(limit=0)
    with pytest.raises(ValueError, match="offset"):
        QueryReadyToWatch(repository).execute(offset=-1)


def test_watchlist_and_ready_to_watch_are_file_independent_and_derived(harness, tmp_path: Path) -> None:
    ready_movie = _movie(harness, "ready")
    no_file_movie = _movie(harness, "no-file")
    missing_movie = _movie(harness, "missing")
    watched_movie = _movie(harness, "watched")
    not_listed_movie = _movie(harness, "not-listed")
    _file(harness, ready_movie.id, tmp_path / "ready-present.mkv")
    _file(harness, ready_movie.id, tmp_path / "ready-missing.mkv", status=MediaFileStatus.MISSING)
    _file(harness, missing_movie.id, tmp_path / "missing.mkv", status=MediaFileStatus.MISSING)
    _file(harness, watched_movie.id, tmp_path / "watched.mkv")
    _file(harness, not_listed_movie.id, tmp_path / "not-listed.mkv")

    repository = SqlitePersonalLibraryRepository(harness.database)
    add = AddToWatchlist(repository, now=lambda: NOW)
    add.execute(ready_movie.id)
    add.execute(no_file_movie.id)
    add.execute(missing_movie.id)
    add.execute(watched_movie.id)
    RecordWatch(repository, now=lambda: NOW).execute(watched_movie.id)

    results = QueryReadyToWatch(repository).execute()
    assert [item.movie_id for item in results] == [ready_movie.id]
    assert results[0].present_media_file_count == 1
    assert GetPersonalMovieState(repository).execute(no_file_movie.id).is_watchlisted
    assert ClearPersonalPreference(repository, now=lambda: NOW).execute(ready_movie.id).is_watchlisted
    assert RemoveFromWatchlist(repository, now=lambda: NOW).execute(ready_movie.id).is_watchlisted is False
    assert QueryReadyToWatch(repository).execute() == ()


def test_personal_library_sections_include_metadata_only_and_filter_ready_rules(
    harness, tmp_path: Path
) -> None:
    watchlisted = _movie(harness, "section-watchlist")
    ready = _movie(harness, "section-ready")
    liked = _movie(harness, "section-liked")
    blacklisted = _movie(harness, "section-blacklisted")
    watched = _movie(harness, "section-watched")
    _file(harness, ready.id, tmp_path / "section-ready.mkv")
    _file(harness, watched.id, tmp_path / "section-watched.mkv")
    repository = SqlitePersonalLibraryRepository(harness.database)

    AddToWatchlist(repository, now=lambda: NOW).execute(watchlisted.id)
    AddToWatchlist(repository, now=lambda: NOW).execute(ready.id)
    AddToWatchlist(repository, now=lambda: NOW).execute(watched.id)
    SetPersonalPreference(repository, now=lambda: NOW).execute(
        liked.id, PersonalPreference.LIKED
    )
    SetPersonalPreference(repository, now=lambda: NOW).execute(
        blacklisted.id, PersonalPreference.BLACKLISTED
    )
    RecordWatch(repository, now=lambda: NOW).execute(watched.id)

    def ids(section: PersonalLibrarySection) -> list[int]:
        return [item.movie.id for item in repository.list_movies(section, limit=100, offset=0)]

    assert ids(PersonalLibrarySection.WATCHLIST) == [watched.id, ready.id, watchlisted.id]
    assert ids(PersonalLibrarySection.READY_TO_WATCH) == [ready.id]
    assert ids(PersonalLibrarySection.LIKED) == [liked.id]
    assert ids(PersonalLibrarySection.BLACKLISTED) == [blacklisted.id]
    assert repository.list_movies(PersonalLibrarySection.WATCHLIST, limit=100, offset=0)[-1].media_file_count == 0


def test_personal_actions_never_create_file_operations_or_mutate_media(
    harness,
    tmp_path: Path,
) -> None:
    movie = _movie(harness, "safety")
    media = tmp_path / "safety.mkv"
    media.write_bytes(b"immutable")
    digest = sha256(media.read_bytes()).hexdigest()
    repository = SqlitePersonalLibraryRepository(harness.database)
    SetPersonalPreference(repository, now=lambda: NOW).execute(movie.id, PersonalPreference.LIKED)
    RecordWatch(repository, now=lambda: NOW).execute(movie.id)
    AddToWatchlist(repository, now=lambda: NOW).execute(movie.id)
    RemoveFromWatchlist(repository, now=lambda: NOW).execute(movie.id)

    with harness.database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 0
    assert media.exists()
    assert sha256(media.read_bytes()).hexdigest() == digest


def test_clear_local_library_retains_personal_movies_and_history(harness, tmp_path: Path) -> None:
    local_only = _movie(harness, "local-only")
    liked = _movie(harness, "liked")
    watched = _movie(harness, "watched-clear")
    watchlisted = _movie(harness, "watchlisted")
    multiple = _movie(harness, "multiple")
    for movie, name in (
        (local_only, "local-only.mkv"),
        (liked, "liked.mkv"),
        (watched, "watched.mkv"),
        (watchlisted, "watchlisted.mkv"),
        (multiple, "multiple.mkv"),
    ):
        _file(harness, movie.id, tmp_path / name)
    _file(harness, local_only.id, tmp_path / "local-only-missing.mkv", status=MediaFileStatus.MISSING)

    personal = SqlitePersonalLibraryRepository(harness.database)
    SetPersonalPreference(personal, now=lambda: NOW).execute(liked.id, PersonalPreference.LIKED)
    RecordWatch(personal, now=lambda: NOW).execute(watched.id)
    AddToWatchlist(personal, now=lambda: NOW).execute(watchlisted.id)
    SetPersonalPreference(personal, now=lambda: NOW).execute(multiple.id, PersonalPreference.BLACKLISTED)
    AddToWatchlist(personal, now=lambda: NOW).execute(multiple.id)
    with harness.database.transaction() as connection:
        connection.execute(
            """
            INSERT INTO metadata_cache(provider, cache_key, payload, fetched_at)
            VALUES ('tmdb', 'clear-test', '{}', ?)
            """,
            (NOW.isoformat(),),
        )
        connection.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state, created_at, updated_at
            ) VALUES ('clear-history', 'MOVE', 'C:\\source.mkv', 'C:\\destination.mkv', 'COMMITTED', ?, ?)
            """,
            (NOW.isoformat(), NOW.isoformat()),
        )

    physical = tmp_path / "local-only.mkv"
    digest = sha256(physical.read_bytes()).hexdigest()
    result = ClearLibraryData(
        SqliteLibraryMaintenanceRepository(harness.database),
        PosterCache(),
        execution_lock=threading.Lock(),
    ).execute()

    assert result.movies_removed == 1
    assert result.media_files_removed == 6
    with harness.database.connection() as connection:
        retained = {
            row["external_id"]
            for row in connection.execute("SELECT external_id FROM movies").fetchall()
        }
        assert retained == {"liked", "watched-clear", "watchlisted", "multiple"}
        assert connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0] == 0
        assert connection.execute("SELECT COUNT(*) FROM metadata_cache").fetchone()[0] == 0
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 1
    assert physical.exists()
    assert sha256(physical.read_bytes()).hexdigest() == digest
    assert GetPersonalMovieState(personal).execute(multiple.id).preference is PersonalPreference.BLACKLISTED
    reimported = EnsureLogicalMovie(SqliteMovieRepository(harness.database)).execute(
        _data("liked", "Reimported title")
    )
    assert reimported.id == liked.id
    reimported_file = tmp_path / "reimported-liked.mkv"
    _file(harness, reimported.id, reimported_file)
    assert MediaFileRepository(harness.database).list_for_movie(liked.id)
