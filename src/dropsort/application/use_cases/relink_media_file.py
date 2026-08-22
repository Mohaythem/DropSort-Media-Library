from __future__ import annotations

from collections import OrderedDict
from datetime import datetime, timezone
import hashlib
import os
from pathlib import Path
import threading
import uuid

from dropsort.application.dto.reconciliation import RelinkPreview, RelinkResult
from dropsort.application.errors import (
    RelinkAlreadyConfirmedError,
    RelinkCatalogError,
    RelinkPreviewNotFoundError,
    RelinkPreviewStaleError,
    RelinkValidationCode,
    RelinkValidationError,
)
from dropsort.library.availability import (
    AvailabilityInspectionStatus,
    MediaFileIdentity,
    NoFollowMediaFileInspector,
)
from dropsort.library.movies import (
    CatalogError,
    MediaFile,
    MediaFilePathConflictError,
    MediaFileRepository,
    MediaFileStatus,
    MovieLibraryReadRepository,
)
from dropsort.media.matcher.normalization import normalize_title
from dropsort.media.parser import MediaType, is_supported_video_filename, parse_media_filename


class RelinkMediaFile:
    """Prepare and explicitly confirm a catalog-only correction for one missing row."""

    def __init__(
        self,
        media_files: MediaFileRepository,
        library: MovieLibraryReadRepository,
        inspector: NoFollowMediaFileInspector,
        *,
        now=None,
        confirmation_lock: threading.Lock | None = None,
        max_previews: int = 256,
    ) -> None:
        self._media_files = media_files
        self._library = library
        self._inspector = inspector
        self._now = now or (lambda: datetime.now(timezone.utc))
        self._lock = confirmation_lock or threading.Lock()
        self._previews: OrderedDict[
            str, tuple[RelinkPreview, MediaFileIdentity, str]
        ] = OrderedDict()
        if isinstance(max_previews, bool) or not isinstance(max_previews, int) or max_previews <= 0:
            raise ValueError("max_previews must be a positive integer")
        self._consumed: OrderedDict[str, None] = OrderedDict()
        self._max_previews = max_previews

    def prepare_preview(self, media_file_id: int, candidate_path: Path) -> RelinkPreview:
        media_file, movie = self._require_context(media_file_id)
        if media_file.status is not MediaFileStatus.MISSING:
            raise RelinkValidationError(
                "only a missing media-file row can be relinked",
                RelinkValidationCode.MEDIA_FILE_NOT_MISSING,
            )
        self._require_original_still_missing(media_file)
        candidate, identity, fingerprint, reasons = self._validate_candidate(
            media_file, movie, candidate_path
        )
        preview = RelinkPreview(
            preview_id=uuid.uuid4().hex,
            media_file_id=media_file.id,
            movie_id=movie.movie.id,
            old_path=str(media_file.current_path),
            new_path=str(candidate),
            file_size=identity.size,
            validation_reasons=reasons,
        )
        self._previews[preview.preview_id] = (preview, identity, fingerprint)
        while len(self._previews) > self._max_previews:
            self._previews.popitem(last=False)
        return preview

    def confirm(self, preview_id: str) -> RelinkResult:
        with self._lock:
            if preview_id in self._consumed:
                raise RelinkAlreadyConfirmedError("relink preview was already confirmed")
            stored = self._previews.pop(preview_id, None)
            if stored is None:
                raise RelinkPreviewNotFoundError("relink preview is unavailable or expired")
            preview, expected_identity, expected_fingerprint = stored
            try:
                media_file, movie = self._require_context(preview.media_file_id)
                if (
                    media_file.status is not MediaFileStatus.MISSING
                    or str(media_file.current_path) != preview.old_path
                    or movie.movie.id != preview.movie_id
                ):
                    raise RelinkPreviewStaleError("catalog association changed after preview")
                try:
                    self._require_original_still_missing(media_file)
                except RelinkValidationError as error:
                    raise RelinkPreviewStaleError(str(error)) from error
                _candidate, actual_identity, actual_fingerprint, _reasons = self._validate_candidate(
                    media_file,
                    movie,
                    Path(preview.new_path),
                )
                if (
                    actual_identity != expected_identity
                    or actual_fingerprint != expected_fingerprint
                ):
                    raise RelinkPreviewStaleError("candidate changed after preview")
                try:
                    relinked = self._media_files.relink(
                        media_file.id,
                        expected_path=media_file.current_path,
                        new_path=Path(preview.new_path),
                        observed_at=self._require_now(),
                    )
                except MediaFilePathConflictError as error:
                    raise RelinkPreviewStaleError(
                        "catalog path ownership changed after preview"
                    ) from error
            except RelinkValidationError as error:
                raise RelinkPreviewStaleError(str(error)) from error
            except CatalogError as error:
                raise RelinkCatalogError("catalog relink could not be committed") from error
            finally:
                self._consumed[preview_id] = None
                while len(self._consumed) > self._max_previews:
                    self._consumed.popitem(last=False)
            return RelinkResult(relinked)

    def discard_preview(self, preview_id: str) -> None:
        self._previews.pop(preview_id, None)

    def _require_context(self, media_file_id: int):
        if isinstance(media_file_id, bool) or not isinstance(media_file_id, int) or media_file_id <= 0:
            raise RelinkValidationError("invalid media-file id", RelinkValidationCode.INVALID_REQUEST)
        media_file = self._media_files.get_by_id(media_file_id)
        if media_file is None or media_file.movie_id is None:
            raise RelinkValidationError(
                "media-file row was not found",
                RelinkValidationCode.MEDIA_FILE_NOT_FOUND,
            )
        movie = self._library.get_movie_details(media_file.movie_id)
        if movie is None:
            raise RelinkValidationError(
                "associated movie was not found",
                RelinkValidationCode.MEDIA_FILE_NOT_FOUND,
            )
        return media_file, movie

    def _validate_candidate(self, media_file: MediaFile, movie, candidate_path: Path):
        if not isinstance(candidate_path, Path) or not candidate_path.is_absolute():
            raise RelinkValidationError("candidate path must be absolute", RelinkValidationCode.INVALID_REQUEST)
        inspection = self._inspector.inspect(candidate_path)
        if inspection.status is not AvailabilityInspectionStatus.PRESENT or inspection.identity is None:
            code = (
                RelinkValidationCode.UNSAFE_LINK
                if inspection.error_code == "UNSAFE_LINK"
                else RelinkValidationCode.CANDIDATE_UNAVAILABLE
            )
            raise RelinkValidationError("candidate is not an available regular file", code)
        candidate = inspection.path
        if not is_supported_video_filename(candidate):
            raise RelinkValidationError("candidate extension is unsupported", RelinkValidationCode.UNSUPPORTED_MEDIA)
        if media_file.extension is not None and candidate.suffix.casefold() != media_file.extension.casefold():
            raise RelinkValidationError(
                "candidate extension differs from catalog facts",
                RelinkValidationCode.EXTENSION_MISMATCH,
            )
        owner = self._media_files.get_by_path(candidate)
        if owner is not None and owner.id != media_file.id:
            raise RelinkValidationError("candidate path belongs to another catalog row", RelinkValidationCode.CATALOG_CONFLICT)
        if inspection.identity.size != media_file.file_size:
            raise RelinkValidationError("candidate size differs from catalog facts", RelinkValidationCode.SIZE_MISMATCH)
        parsed = parse_media_filename(candidate)
        expected_titles = {
            normalize_title(movie.movie.title),
            normalize_title(movie.movie.original_title),
        }
        expected_titles.discard("")
        if (
            parsed.media_type is not MediaType.MOVIE
            or normalize_title(parsed.title) not in expected_titles
        ):
            raise RelinkValidationError("candidate title is incompatible", RelinkValidationCode.TITLE_MISMATCH)
        if movie.movie.year is not None and parsed.year is not None and parsed.year != movie.movie.year:
            raise RelinkValidationError("candidate year conflicts with catalog metadata", RelinkValidationCode.YEAR_MISMATCH)
        for expected, actual in (
            (media_file.resolution, parsed.resolution),
            (media_file.codec, parsed.codec),
            (media_file.source, parsed.source),
        ):
            if expected is not None and actual is not None and expected.casefold() != actual.casefold():
                raise RelinkValidationError("candidate technical facts conflict", RelinkValidationCode.TECHNICAL_MISMATCH)
        fingerprint = _sha256_file(candidate)
        after_hash = self._inspector.inspect(candidate)
        if (
            after_hash.status is not AvailabilityInspectionStatus.PRESENT
            or after_hash.identity != inspection.identity
        ):
            raise RelinkValidationError(
                "candidate changed during validation",
                RelinkValidationCode.CANDIDATE_UNAVAILABLE,
            )
        return (
            candidate,
            inspection.identity,
            fingerprint,
            ("REGULAR_FILE", "SIZE_EXACT", "TITLE_COMPATIBLE", "CONTENT_FINGERPRINT"),
        )

    def _require_original_still_missing(self, media_file: MediaFile) -> None:
        inspection = self._inspector.inspect(media_file.current_path)
        if inspection.status is AvailabilityInspectionStatus.PRESENT:
            raise RelinkValidationError(
                "the original catalog path is available again; refresh file status instead",
                RelinkValidationCode.ORIGINAL_PATH_AVAILABLE,
            )
        if inspection.status is AvailabilityInspectionStatus.ERROR:
            raise RelinkValidationError(
                "the original catalog path could not be safely inspected",
                RelinkValidationCode.ORIGINAL_PATH_UNVERIFIED,
            )

    def _require_now(self) -> datetime:
        value = self._now()
        if not isinstance(value, datetime) or value.tzinfo is None:
            raise ValueError("now must return a timezone-aware datetime")
        return value


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            opened_identity = _identity_from_stat(os.fstat(stream.fileno()))
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
            if _identity_from_stat(os.fstat(stream.fileno())) != opened_identity:
                raise RelinkValidationError(
                    "candidate changed during content validation",
                    RelinkValidationCode.CANDIDATE_UNAVAILABLE,
                )
    except OSError as error:
        raise RelinkValidationError(
            "candidate could not be read safely",
            RelinkValidationCode.CANDIDATE_UNAVAILABLE,
        ) from error
    return digest.hexdigest()


def _identity_from_stat(information: os.stat_result) -> MediaFileIdentity:
    return MediaFileIdentity(
        size=information.st_size,
        mtime_ns=information.st_mtime_ns,
        ctime_ns=information.st_ctime_ns,
        dev=information.st_dev,
        ino=information.st_ino,
    )
