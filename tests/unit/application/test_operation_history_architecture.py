from __future__ import annotations

import ast
from pathlib import Path


APPLICATION = Path("src/dropsort/application/use_cases/operation_history.py")
UI_HISTORY = Path("src/dropsort/ui/history")


def _imports(path: Path) -> tuple[str, ...]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    values: list[str] = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            values.extend(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            values.append(node.module)
    return tuple(values)


def test_application_history_depends_on_contracts_not_sqlite_ui_or_providers() -> None:
    imports = _imports(APPLICATION)
    forbidden = (
        "sqlite3",
        "dropsort.database",
        "dropsort.metadata",
        "dropsort.media.matcher",
        "PySide6",
    )
    assert not any(
        imported == item or imported.startswith(f"{item}.")
        for imported in imports
        for item in forbidden
    )


def test_history_widgets_do_not_import_sql_http_or_file_engine() -> None:
    forbidden = (
        "sqlite3",
        "dropsort.database",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "dropsort.metadata.providers",
        "urllib",
        "requests",
        "httpx",
        "os",
        "shutil",
    )
    for path in UI_HISTORY.rglob("*.py"):
        imports = _imports(path)
        assert not any(
            imported == item or imported.startswith(f"{item}.")
            for imported in imports
            for item in forbidden
        ), path


def test_database_history_adapter_does_not_mutate_files_or_call_provider() -> None:
    source = Path("src/dropsort/database/repositories/operation_history.py").read_text(
        encoding="utf-8"
    )
    forbidden = (
        "FileOperationService",
        ".unlink(",
        ".rename(",
        "shutil",
        "metadata.providers",
        "urllib",
        "requests",
        "PySide6",
    )
    assert all(value not in source for value in forbidden)
