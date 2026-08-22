from __future__ import annotations

import ast
from pathlib import Path


FILES = (
    Path("src/dropsort/application/dto/import_review.py"),
    Path("src/dropsort/application/use_cases/prepare_folder_import_review.py"),
)
FORBIDDEN = (
    "PySide6",
    "sqlite3",
    "dropsort.database",
    "dropsort.metadata.providers",
    "dropsort.core.file_engine",
    "dropsort.core.operations",
)


def test_import_review_application_boundary_has_no_ui_sql_or_file_operation_dependency() -> None:
    for path in FILES:
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        imports: list[str] = []
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imports.append(node.module)
        assert not any(
            value == forbidden or value.startswith(f"{forbidden}.")
            for value in imports
            for forbidden in FORBIDDEN
        ), path


def test_import_review_orchestration_contains_no_automatic_confirmation() -> None:
    text = Path(
        "src/dropsort/application/use_cases/prepare_folder_import_review.py"
    ).read_text(encoding="utf-8")

    assert "ConfirmMovieImport" not in text
    assert "RegisterMovieFile" not in text
    assert "FileOperation" not in text
