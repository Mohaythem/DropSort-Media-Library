from __future__ import annotations

from dataclasses import FrozenInstanceError

import pytest

from dropsort.application.dto.organization import (
    OrganizationOperation,
    OrganizationPreview,
    OrganizationResult,
)


def test_organization_preview_is_immutable_and_validates_required_fields() -> None:
    preview = OrganizationPreview(
        preview_id="preview-1",
        media_file_id=7,
        source_path=r"D:\Incoming\Movie.mkv",
        destination_path=r"E:\Movies\Movie.mkv",
        operation=OrganizationOperation.MOVE,
        same_volume=False,
        file_size=123,
        source_volume="D:\\",
        destination_volume="E:\\",
        warnings=("CROSS_VOLUME",),
    )

    assert preview.operation is OrganizationOperation.MOVE
    with pytest.raises(FrozenInstanceError):
        preview.source_path = "changed"  # type: ignore[misc]

    with pytest.raises(ValueError):
        OrganizationPreview(
            preview_id="",
            media_file_id=7,
            source_path="source",
            destination_path="destination",
            operation=OrganizationOperation.MOVE,
            same_volume=True,
            file_size=1,
            source_volume="",
            destination_volume="",
            warnings=(),
        )


def test_organization_result_requires_a_committed_operation_identity() -> None:
    result = OrganizationResult(
        operation_id="operation-1",
        media_file_id=7,
        source_path=r"D:\Incoming\Movie.mkv",
        destination_path=r"D:\Movies\Movie.mkv",
        strategy="hardlink-unlink",
    )

    assert result.operation_id == "operation-1"
    with pytest.raises(ValueError):
        OrganizationResult("", 7, "source", "destination", "strategy")


@pytest.mark.parametrize(
    "overrides",
    (
        {"media_file_id": 0},
        {"source_path": ""},
        {"destination_path": ""},
        {"operation": "MOVE"},
        {"same_volume": 1},
        {"file_size": -1},
        {"source_volume": 1},
        {"warnings": ["CROSS_VOLUME"]},
        {"warnings": ("",)},
    ),
)
def test_organization_preview_rejects_malformed_boundary_values(overrides) -> None:
    values = {
        "preview_id": "preview-1",
        "media_file_id": 7,
        "source_path": "source",
        "destination_path": "destination",
        "operation": OrganizationOperation.MOVE,
        "same_volume": True,
        "file_size": 1,
        "source_volume": "D:\\",
        "destination_volume": "E:\\",
        "warnings": (),
    }
    values.update(overrides)

    with pytest.raises(ValueError):
        OrganizationPreview(**values)


@pytest.mark.parametrize(
    "overrides",
    (
        {"media_file_id": False},
        {"source_path": ""},
        {"destination_path": ""},
        {"strategy": ""},
    ),
)
def test_organization_result_rejects_malformed_boundary_values(overrides) -> None:
    values = {
        "operation_id": "operation-1",
        "media_file_id": 7,
        "source_path": "source",
        "destination_path": "destination",
        "strategy": "strategy",
    }
    values.update(overrides)

    with pytest.raises(ValueError):
        OrganizationResult(**values)
