from __future__ import annotations

from datetime import datetime, timezone

import pytest

from dropsort.application.dto.operation_history import (
    MAX_OPERATION_HISTORY_PAGE_SIZE,
    OperationDetails,
    OperationHistoryItem,
    OperationHistoryQuery,
    OperationKind,
    OperationStatus,
    RecoveryAssessment,
    RecoveryDisposition,
    UndoEligibility,
    UndoEligibilityCode,
    UndoPreview,
    UndoResult,
)


NOW = datetime(2026, 8, 12, tzinfo=timezone.utc)


def _item(**overrides: object) -> OperationHistoryItem:
    values: dict[str, object] = {
        "operation_id": "operation-1",
        "operation": OperationKind.MOVE,
        "state": OperationStatus.COMMITTED,
        "source_path": r"D:\Incoming\Movie.mkv",
        "destination_path": r"D:\Movies\Movie.mkv",
        "media_file_id": 7,
        "movie_title": "Movie",
        "created_at": NOW,
        "updated_at": NOW,
        "reverses_operation_id": None,
    }
    values.update(overrides)
    return OperationHistoryItem(**values)  # type: ignore[arg-type]


def test_history_query_has_bounded_pagination() -> None:
    assert OperationHistoryQuery().limit == MAX_OPERATION_HISTORY_PAGE_SIZE
    assert OperationHistoryQuery(limit=25, offset=50).offset == 50
    for invalid in (0, -1, True, MAX_OPERATION_HISTORY_PAGE_SIZE + 1):
        with pytest.raises(ValueError):
            OperationHistoryQuery(limit=invalid)  # type: ignore[arg-type]
    for invalid in (-1, True):
        with pytest.raises(ValueError):
            OperationHistoryQuery(offset=invalid)  # type: ignore[arg-type]


def test_history_item_is_immutable_and_validated() -> None:
    item = _item()
    assert item.operation is OperationKind.MOVE
    with pytest.raises(AttributeError):
        item.movie_title = "Changed"  # type: ignore[misc]
    with pytest.raises(ValueError):
        _item(operation_id="")
    with pytest.raises(ValueError):
        _item(media_file_id=0)
    with pytest.raises(ValueError):
        _item(created_at=datetime(2026, 1, 1))


def test_details_and_undo_models_keep_exact_paths_and_stable_reasons() -> None:
    eligibility = UndoEligibility(False, UndoEligibilityCode.SUPERSEDED, "A later operation exists")
    details = OperationDetails(
        history=_item(),
        current_catalog_path=r"D:\Movies\Newer.mkv",
        strategy="hardlink-unlink",
        error_code=None,
        error_message=None,
        reversed_by_operation_id=None,
        source_size=100,
        destination_size=100,
        destination_sha256=None,
    )
    preview = UndoPreview(
        preview_id="preview-1",
        operation_id="operation-1",
        media_file_id=7,
        source_path=r"D:\Movies\Movie.mkv",
        destination_path=r"D:\Incoming\Movie.mkv",
        operation=OperationKind.MOVE,
        same_volume=True,
        file_size=100,
        source_volume="D:\\",
        destination_volume="D:\\",
        warnings=(),
    )
    result = UndoResult(
        original_operation_id="operation-1",
        reverse_operation_id="operation-2",
        media_file_id=7,
        source_path=preview.source_path,
        destination_path=preview.destination_path,
        strategy="hardlink-unlink",
    )

    assert details.current_catalog_path.endswith("Newer.mkv")
    assert eligibility.code is UndoEligibilityCode.SUPERSEDED
    assert preview.destination_path == r"D:\Incoming\Movie.mkv"
    assert result.reverse_operation_id == "operation-2"


@pytest.mark.parametrize(
    "factory",
    (
        lambda: UndoEligibility(True, UndoEligibilityCode.SUPERSEDED, "wrong"),
        lambda: UndoPreview("", "op", 1, "a", "b", OperationKind.MOVE, True, 1, "D", "D", ()),
        lambda: UndoResult("op", "", 1, "a", "b", "strategy"),
    ),
)
def test_undo_models_reject_inconsistent_or_empty_values(factory) -> None:
    with pytest.raises(ValueError):
        factory()


def test_recovery_assessment_exposes_only_explicit_safe_action() -> None:
    assessment = RecoveryAssessment(
        operation_id="operation-1",
        state=OperationStatus.RECOVERY_REQUIRED,
        disposition=RecoveryDisposition.AMBIGUOUS_BOTH_EXIST,
        source_exists=True,
        destination_exists=True,
        action_available=False,
        explanation="Both files are preserved",
    )
    assert assessment.action_available is False
    assert assessment.disposition is RecoveryDisposition.AMBIGUOUS_BOTH_EXIST
    with pytest.raises(ValueError):
        RecoveryAssessment(
            operation_id="operation-1",
            state=OperationStatus.COMMITTED,
            disposition=RecoveryDisposition.NOT_REQUIRED,
            source_exists=False,
            destination_exists=True,
            action_available=True,
            explanation="wrong",
        )


@pytest.mark.parametrize(
    ("overrides", "message"),
    (
        ({"operation": "MOVE"}, "operation"),
        ({"state": "COMMITTED"}, "state"),
        ({"media_file_id": True}, "media_file_id"),
        ({"movie_title": ""}, "movie_title"),
        ({"updated_at": datetime(2026, 1, 1)}, "updated_at"),
        ({"reverses_operation_id": ""}, "reverses_operation_id"),
    ),
)
def test_history_item_rejects_invalid_typed_values(overrides, message) -> None:
    with pytest.raises(ValueError, match=message):
        _item(**overrides)


@pytest.mark.parametrize(
    "overrides",
    (
        {"history": object()},
        {"source_size": -1},
        {"destination_size": True},
        {"strategy": ""},
    ),
)
def test_operation_details_rejects_invalid_values(overrides) -> None:
    values: dict[str, object] = {
        "history": _item(),
        "current_catalog_path": None,
        "strategy": None,
        "error_code": None,
        "error_message": None,
        "reversed_by_operation_id": None,
        "source_size": None,
        "destination_size": None,
        "destination_sha256": None,
    }
    values.update(overrides)
    with pytest.raises(ValueError):
        OperationDetails(**values)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    "values",
    (
        ("yes", UndoEligibilityCode.ELIGIBLE, "ok"),
        (True, "ELIGIBLE", "ok"),
        (True, UndoEligibilityCode.ELIGIBLE, ""),
    ),
)
def test_undo_eligibility_rejects_invalid_values(values) -> None:
    with pytest.raises(ValueError):
        UndoEligibility(*values)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    "overrides",
    (
        {"operation": "MOVE"},
        {"same_volume": 1},
        {"file_size": -1},
        {"source_volume": None},
        {"warnings": ("",)},
    ),
)
def test_undo_preview_rejects_invalid_values(overrides) -> None:
    values: dict[str, object] = {
        "preview_id": "preview",
        "operation_id": "operation",
        "media_file_id": 1,
        "source_path": "source",
        "destination_path": "destination",
        "operation": OperationKind.MOVE,
        "same_volume": True,
        "file_size": 1,
        "source_volume": "D",
        "destination_volume": "D",
        "warnings": (),
    }
    values.update(overrides)
    with pytest.raises(ValueError):
        UndoPreview(**values)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    "overrides",
    (
        {"state": "COMMITTED"},
        {"disposition": "NOT_REQUIRED"},
        {"source_exists": 1},
    ),
)
def test_recovery_assessment_rejects_invalid_types(overrides) -> None:
    values: dict[str, object] = {
        "operation_id": "operation",
        "state": OperationStatus.COMMITTED,
        "disposition": RecoveryDisposition.NOT_REQUIRED,
        "source_exists": False,
        "destination_exists": True,
        "action_available": False,
        "explanation": "terminal",
    }
    values.update(overrides)
    with pytest.raises(ValueError):
        RecoveryAssessment(**values)  # type: ignore[arg-type]


def test_recovery_result_rejects_invalid_state() -> None:
    from dropsort.application.dto.operation_history import RecoveryResult

    with pytest.raises(ValueError):
        RecoveryResult("operation", "COMMITTED")  # type: ignore[arg-type]
