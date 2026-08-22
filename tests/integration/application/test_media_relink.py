from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
import threading

import pytest

from dropsort.application.errors import (
    RelinkAlreadyConfirmedError,
    RelinkPreviewStaleError,
    RelinkValidationCode,
    RelinkValidationError,
    RelinkPreviewNotFoundError,
    RelinkCatalogError,
)
from dropsort.application.use_cases.relink_media_file import RelinkMediaFile
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteMovieLibraryReadRepository,
    SqliteMovieRepository,
)
from dropsort.library.availability import NoFollowMediaFileInspector
from dropsort.library.movies import MediaFileStatus, MovieCatalogData, VerifiedMediaFileFacts


NOW = datetime(2026, 8, 12, 12, 0, tzinfo=timezone.utc)


def _build(harness, tmp_path: Path, *, old_name: str = "Prisoners.2013.1080p.BluRay.mkv"):
    movie_id = SqliteMovieRepository(harness.database).create(
        MovieCatalogData("tmdb", "146233", "Prisoners", "Prisoners", 2013, None, (), 153, 8.1, None),
        now=NOW,
    ).id
    repository = MediaFileRepository(harness.database)
    old = (tmp_path / old_name).absolute()
    media = repository.add(
        VerifiedMediaFileFacts(old, 7, ".mkv", "1080p", "x264", "BluRay", NOW),
        movie_id,
    )
    repository.mark_missing(media.id)
    actions = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
    )
    return actions, repository, media, old


def _journal_count(harness) -> int:
    with harness.database.connection() as connection:
        return int(connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0])


def test_valid_externally_renamed_file_previews_then_relinks_same_row_without_mutation(
    harness,
    tmp_path: Path,
) -> None:
    actions, repository, media, old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners (2013).mkv").absolute()
    candidate.write_bytes(b"1234567")
    before = candidate.read_bytes()

    preview = actions.prepare_preview(media.id, candidate)

    assert preview.old_path == str(old)
    assert preview.new_path == str(candidate)
    assert repository.get_by_id(media.id).current_path == old  # type: ignore[union-attr]
    assert _journal_count(harness) == 0

    result = actions.confirm(preview.preview_id)

    assert result.id == media.id
    assert result.movie_id == media.movie_id
    assert result.current_path == candidate
    assert result.status is MediaFileStatus.PRESENT
    assert (result.resolution, result.codec, result.source) == ("1080p", "x264", "BluRay")
    assert candidate.read_bytes() == before
    assert _journal_count(harness) == 0
    with pytest.raises(RelinkAlreadyConfirmedError):
        actions.confirm(preview.preview_id)


@pytest.mark.parametrize(
    ("candidate_name", "candidate_bytes", "code"),
    (
        ("Prisoners.2013.txt", b"1234567", RelinkValidationCode.UNSUPPORTED_MEDIA),
        ("Prisoners.2013.mp4", b"1234567", RelinkValidationCode.EXTENSION_MISMATCH),
        ("Other.Movie.2013.mkv", b"1234567", RelinkValidationCode.TITLE_MISMATCH),
        ("Prisoners.2014.mkv", b"1234567", RelinkValidationCode.YEAR_MISMATCH),
        ("Prisoners.2013.2160p.mkv", b"1234567", RelinkValidationCode.TECHNICAL_MISMATCH),
        ("Prisoners.2013.mkv", b"wrong-size", RelinkValidationCode.SIZE_MISMATCH),
    ),
)
def test_wrong_candidate_is_blocked(
    harness,
    tmp_path: Path,
    candidate_name: str,
    candidate_bytes: bytes,
    code: RelinkValidationCode,
) -> None:
    actions, repository, media, old = _build(harness, tmp_path)
    candidate = (tmp_path / candidate_name).absolute()
    candidate.write_bytes(candidate_bytes)

    with pytest.raises(RelinkValidationError) as caught:
        actions.prepare_preview(media.id, candidate)

    assert caught.value.code is code
    assert repository.get_by_id(media.id).current_path == old  # type: ignore[union-attr]
    assert _journal_count(harness) == 0


def test_missing_directory_and_link_candidates_are_blocked(harness, tmp_path: Path, monkeypatch) -> None:
    actions, _repository, media, _old = _build(harness, tmp_path)
    directory = (tmp_path / "Prisoners.2013.mkv").absolute()
    directory.mkdir()

    with pytest.raises(RelinkValidationError) as directory_error:
        actions.prepare_preview(media.id, directory)
    assert directory_error.value.code is RelinkValidationCode.CANDIDATE_UNAVAILABLE

    monkeypatch.setattr(
        "dropsort.application.use_cases.relink_media_file.NoFollowMediaFileInspector.inspect",
        lambda _self, path: __import__("dropsort.library.availability", fromlist=["AvailabilityInspection"]).AvailabilityInspection(
            path, __import__("dropsort.library.availability", fromlist=["AvailabilityInspectionStatus"]).AvailabilityInspectionStatus.MISSING, error_code="UNSAFE_LINK"
        ),
    )
    with pytest.raises(RelinkValidationError) as link_error:
        actions.prepare_preview(media.id, (tmp_path / "link.mkv").absolute())
    assert link_error.value.code is RelinkValidationCode.UNSAFE_LINK


def test_catalog_owned_candidate_is_blocked_including_case_alias(harness, tmp_path: Path) -> None:
    actions, repository, media, _old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.MKV").absolute()
    candidate.write_bytes(b"1234567")
    other_movie = SqliteMovieRepository(harness.database).create(
        MovieCatalogData("tmdb", "2", "Other", None, 2020, None, (), None, None, None),
        now=NOW,
    )
    repository.add(
        VerifiedMediaFileFacts(candidate, 7, ".mkv", None, None, None, NOW),
        other_movie.id,
    )

    with pytest.raises(RelinkValidationError) as caught:
        actions.prepare_preview(media.id, candidate.with_name("prisoners.2013.mkv"))
    assert caught.value.code is RelinkValidationCode.CATALOG_CONFLICT


def test_candidate_and_catalog_are_revalidated_after_preview(harness, tmp_path: Path) -> None:
    actions, repository, media, _old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    preview = actions.prepare_preview(media.id, candidate)
    candidate.write_bytes(b"changed!")

    with pytest.raises(RelinkPreviewStaleError):
        actions.confirm(preview.preview_id)

    candidate.write_bytes(b"1234567")
    second = actions.prepare_preview(media.id, candidate)
    repository.mark_present(media.id, observed_at=NOW)
    with pytest.raises(RelinkPreviewStaleError):
        actions.confirm(second.preview_id)


def test_returned_original_path_blocks_preview_and_post_preview_ambiguity(
    harness,
    tmp_path: Path,
) -> None:
    actions, repository, media, old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    old.write_bytes(b"1234567")

    with pytest.raises(RelinkValidationError) as caught:
        actions.prepare_preview(media.id, candidate)
    assert caught.value.code is RelinkValidationCode.ORIGINAL_PATH_AVAILABLE

    old.unlink()
    preview = actions.prepare_preview(media.id, candidate)
    old.write_bytes(b"1234567")
    with pytest.raises(RelinkPreviewStaleError):
        actions.confirm(preview.preview_id)

    assert repository.get_by_id(media.id).current_path == old  # type: ignore[union-attr]
    assert old.exists() and candidate.exists()


def test_catalog_original_title_is_valid_conservative_title_evidence(
    harness,
    tmp_path: Path,
) -> None:
    movie_id = SqliteMovieRepository(harness.database).create(
        MovieCatalogData(
            "tmdb",
            "3",
            "Localized Name",
            "Original Name",
            2020,
            None,
            (),
            None,
            None,
            None,
        ),
        now=NOW,
    ).id
    repository = MediaFileRepository(harness.database)
    old = (tmp_path / "Localized.Name.2020.mkv").absolute()
    media = repository.add(
        VerifiedMediaFileFacts(old, 7, ".mkv", None, None, None, NOW),
        movie_id,
    )
    repository.mark_missing(media.id)
    candidate = (tmp_path / "Original.Name.2020.mkv").absolute()
    candidate.write_bytes(b"1234567")
    actions = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
    )

    assert actions.prepare_preview(media.id, candidate).new_path == str(candidate)


def test_invalid_context_preview_eviction_discard_and_clock_are_controlled(
    harness,
    tmp_path: Path,
) -> None:
    actions, repository, media, _old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")

    with pytest.raises(ValueError, match="max_previews"):
        RelinkMediaFile(
            repository,
            SqliteMovieLibraryReadRepository(harness.database),
            NoFollowMediaFileInspector(),
            max_previews=0,
        )

    for invalid_id in (0, True, 999):
        with pytest.raises(RelinkValidationError):
            actions.prepare_preview(invalid_id, candidate)
    repository.mark_present(media.id, observed_at=NOW)
    with pytest.raises(RelinkValidationError) as not_missing:
        actions.prepare_preview(media.id, candidate)
    assert not_missing.value.code is RelinkValidationCode.MEDIA_FILE_NOT_MISSING
    repository.mark_missing(media.id)

    bounded = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
        max_previews=1,
    )
    first = bounded.prepare_preview(media.id, candidate)
    second = bounded.prepare_preview(media.id, candidate)
    with pytest.raises(RelinkPreviewNotFoundError):
        bounded.confirm(first.preview_id)
    bounded.discard_preview(second.preview_id)
    with pytest.raises(RelinkPreviewNotFoundError):
        bounded.confirm(second.preview_id)

    naive = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: datetime(2026, 8, 12),
    )
    with pytest.raises(ValueError, match="timezone-aware"):
        naive.confirm(naive.prepare_preview(media.id, candidate).preview_id)


def test_consumed_confirmation_registry_is_bounded(harness, tmp_path: Path) -> None:
    actions, repository, media, _old = _build(harness, tmp_path)
    actions = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
        max_previews=1,
    )
    first_candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    first_candidate.write_bytes(b"1234567")
    first = actions.prepare_preview(media.id, first_candidate)
    actions.confirm(first.preview_id)
    repository.mark_missing(media.id)
    second_candidate = (tmp_path / "Prisoners (2013).mkv").absolute()
    second_candidate.write_bytes(b"1234567")
    first_candidate.unlink()
    second = actions.prepare_preview(media.id, second_candidate)
    actions.confirm(second.preview_id)

    assert tuple(actions._consumed) == (second.preview_id,)


def test_unassigned_or_missing_movie_and_relative_candidate_are_controlled(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    unassigned = repository.create((tmp_path / "old.mkv").absolute(), 7)
    actions = RelinkMediaFile(
        repository,
        SqliteMovieLibraryReadRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
    )
    with pytest.raises(RelinkValidationError) as unassigned_error:
        actions.prepare_preview(unassigned, (tmp_path / "candidate.mkv").absolute())
    assert unassigned_error.value.code is RelinkValidationCode.MEDIA_FILE_NOT_FOUND

    linked, _repository, media, _old = _build(harness, tmp_path / "linked")
    with pytest.raises(RelinkValidationError) as relative:
        linked.prepare_preview(media.id, Path("relative.mkv"))
    assert relative.value.code is RelinkValidationCode.INVALID_REQUEST


def test_transient_old_path_error_and_catalog_commit_error_are_controlled(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    actions, repository, media, old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    original_inspect = actions._inspector.inspect

    def error_old(path: Path):
        if path == old:
            from dropsort.library.availability import AvailabilityInspection, AvailabilityInspectionStatus
            return AvailabilityInspection(path, AvailabilityInspectionStatus.ERROR, error_code="DENIED")
        return original_inspect(path)

    monkeypatch.setattr(actions._inspector, "inspect", error_old)
    with pytest.raises(RelinkValidationError) as caught:
        actions.prepare_preview(media.id, candidate)
    assert caught.value.code is RelinkValidationCode.ORIGINAL_PATH_UNVERIFIED

    monkeypatch.setattr(actions._inspector, "inspect", original_inspect)
    preview = actions.prepare_preview(media.id, candidate)
    monkeypatch.setattr(
        repository,
        "relink",
        lambda *args, **kwargs: (_ for _ in ()).throw(
            __import__("dropsort.library.movies", fromlist=["CatalogIntegrityError"]).CatalogIntegrityError("db")
        ),
    )
    with pytest.raises(RelinkCatalogError):
        actions.confirm(preview.preview_id)


def test_same_size_candidate_replacement_and_repository_path_race_are_stale(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    actions, repository, media, _old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    preview = actions.prepare_preview(media.id, candidate)
    candidate.write_bytes(b"7654321")
    with pytest.raises(RelinkPreviewStaleError, match="candidate changed"):
        actions.confirm(preview.preview_id)

    preview = actions.prepare_preview(media.id, candidate)
    monkeypatch.setattr(
        repository,
        "relink",
        lambda *args, **kwargs: (_ for _ in ()).throw(
            __import__("dropsort.library.movies", fromlist=["MediaFilePathConflictError"]).MediaFilePathConflictError("race")
        ),
    )
    with pytest.raises(RelinkPreviewStaleError, match="ownership"):
        actions.confirm(preview.preview_id)


def test_content_fingerprint_rejects_rewrite_when_filesystem_identity_is_unchanged(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    actions, _repository, media, _old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    preview = actions.prepare_preview(media.id, candidate)
    original_inspect = actions._inspector.inspect
    before = original_inspect(candidate)
    candidate.write_bytes(b"7654321")

    def unchanged_identity(path: Path):
        inspected = original_inspect(path)
        if path == candidate:
            return type(inspected)(
                path=inspected.path,
                status=inspected.status,
                identity=before.identity,
                error_code=inspected.error_code,
            )
        return inspected

    monkeypatch.setattr(actions._inspector, "inspect", unchanged_identity)

    with pytest.raises(RelinkPreviewStaleError, match="candidate changed"):
        actions.confirm(preview.preview_id)


def test_missing_associated_movie_is_controlled(harness, tmp_path: Path, monkeypatch) -> None:
    actions, _repository, media, _old = _build(harness, tmp_path)
    monkeypatch.setattr(actions._library, "get_movie_details", lambda _movie_id: None)

    with pytest.raises(RelinkValidationError) as caught:
        actions.prepare_preview(media.id, (tmp_path / "candidate.mkv").absolute())

    assert caught.value.code is RelinkValidationCode.MEDIA_FILE_NOT_FOUND


def test_competing_confirmations_produce_one_catalog_update(harness, tmp_path: Path) -> None:
    actions, repository, media, old = _build(harness, tmp_path)
    candidate = (tmp_path / "Prisoners.2013.mkv").absolute()
    candidate.write_bytes(b"1234567")
    preview = actions.prepare_preview(media.id, candidate)
    outcomes: list[object] = []

    def confirm() -> None:
        try:
            outcomes.append(actions.confirm(preview.preview_id))
        except Exception as error:
            outcomes.append(error)

    threads = [threading.Thread(target=confirm) for _ in range(2)]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    assert sum(hasattr(value, "current_path") for value in outcomes) == 1
    assert sum(isinstance(value, RelinkAlreadyConfirmedError) for value in outcomes) == 1
    assert repository.get_by_id(media.id).current_path == candidate  # type: ignore[union-attr]
    assert not old.exists()
