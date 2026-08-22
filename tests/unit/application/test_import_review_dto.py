from pathlib import Path

import pytest

from dropsort.application.dto.import_review import ImportReviewSession


def test_import_review_session_validates_root_recursive_and_items() -> None:
    root = Path.cwd()

    with pytest.raises(ValueError, match="absolute"):
        ImportReviewSession(Path("relative"), True, ())
    with pytest.raises(ValueError, match="recursive"):
        ImportReviewSession(root, 1, ())  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="items"):
        ImportReviewSession(root, True, [])  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="items"):
        ImportReviewSession(root, True, (object(),))  # type: ignore[arg-type]
