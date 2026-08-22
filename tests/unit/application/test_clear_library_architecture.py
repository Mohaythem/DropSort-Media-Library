from __future__ import annotations

import ast
from pathlib import Path


ROOT = Path(__file__).parents[3] / "src" / "dropsort"


def _imports(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    values: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            values.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            values.add(node.module)
    return values


def test_clear_use_case_has_no_media_mutation_ui_sql_or_provider_dependencies() -> None:
    path = ROOT / "application" / "use_cases" / "clear_library_data.py"
    imports = _imports(path)
    source = path.read_text(encoding="utf-8")
    forbidden = (
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "dropsort.metadata.providers",
        "dropsort.database",
        "dropsort.ui",
        "PySide6",
        "sqlite3",
        "shutil",
        "os",
    )

    assert not any(
        name == prefix or name.startswith(f"{prefix}.")
        for name in imports
        for prefix in forbidden
    )
    assert all(
        token not in source
        for token in ("FileOperationService", ".rename(", ".replace(", ".unlink(")
    )


def test_sqlite_clear_repository_has_no_filesystem_provider_or_ui_dependency() -> None:
    path = ROOT / "database" / "repositories" / "library_maintenance.py"
    imports = _imports(path)
    forbidden = (
        "dropsort.core.file_engine",
        "dropsort.metadata",
        "dropsort.posters",
        "dropsort.ui",
        "PySide6",
        "os",
        "shutil",
    )

    assert not any(
        name == prefix or name.startswith(f"{prefix}.")
        for name in imports
        for prefix in forbidden
    )
