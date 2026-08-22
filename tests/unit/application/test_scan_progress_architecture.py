from __future__ import annotations

import ast
from pathlib import Path


FILES = (
    Path("src/dropsort/media/discovery/models.py"),
    Path("src/dropsort/media/discovery/contracts.py"),
    Path("src/dropsort/media/discovery/scanner.py"),
    Path("src/dropsort/application/dto/import_review.py"),
    Path("src/dropsort/application/use_cases/prepare_folder_import_review.py"),
)
FORBIDDEN = (
    "PySide6",
    "sqlite3",
    "dropsort.database",
    "dropsort.core.file_engine",
    "dropsort.core.operations",
    "dropsort.ui",
)


def test_progress_and_cancellation_boundaries_have_no_ui_sql_or_file_engine_dependency() -> None:
    for path in FILES:
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        imports = []
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.extend(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imports.append(node.module)
        assert not any(
            name == forbidden or name.startswith(f"{forbidden}.")
            for name in imports
            for forbidden in FORBIDDEN
        ), path


def test_cancel_and_progress_code_contains_no_mutation_or_automatic_import_calls() -> None:
    source = "\n".join(path.read_text(encoding="utf-8") for path in FILES)
    forbidden = (
        "FileOperationService",
        "confirm_movie_import",
        "RegisterMovieFile",
        "OrganizeMediaFile",
        "UndoFileOperation",
        "RecoverFileOperation",
        ".rename(",
        ".replace(",
        ".unlink(",
        "shutil",
    )
    assert all(value not in source for value in forbidden)
