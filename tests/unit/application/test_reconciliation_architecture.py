from __future__ import annotations

import ast
from pathlib import Path


ROOT = Path(__file__).parents[3]


def _imports(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    values: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            values.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            values.add(node.module)
    return values


def test_reconciliation_and_relink_do_not_depend_on_mutation_or_external_metadata() -> None:
    files = (
        ROOT / "src/dropsort/application/use_cases/reconcile_library_files.py",
        ROOT / "src/dropsort/application/use_cases/relink_media_file.py",
    )
    forbidden = (
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "dropsort.metadata",
        "dropsort.media.matcher.matcher",
        "PySide6",
        "sqlite3",
    )

    for path in files:
        imported = _imports(path)
        assert not any(name.startswith(prefix) for name in imported for prefix in forbidden)


def test_reconciliation_ui_has_no_sql_http_or_direct_filesystem_inspection() -> None:
    path = ROOT / "src/dropsort/ui/reconciliation/dialogs.py"
    source = path.read_text(encoding="utf-8")
    imported = _imports(path)

    assert "sqlite3" not in imported
    assert not any(name.startswith("dropsort.database") for name in imported)
    assert not any(name.startswith("dropsort.core") for name in imported)
    assert not any(name.startswith("dropsort.metadata") for name in imported)
    for fragment in (".exists(", ".stat(", ".lstat(", "os.path", "SELECT ", "UPDATE "):
        assert fragment not in source


def test_database_repositories_remain_free_of_ui_and_runtime_inspection() -> None:
    path = ROOT / "src/dropsort/database/repositories/media_files.py"
    imported = _imports(path)
    assert not any(name.startswith("PySide6") for name in imported)
    assert not any(name.startswith("dropsort.ui") for name in imported)
    assert not any(name.startswith("dropsort.library.availability") for name in imported)


def test_production_source_has_no_developer_or_manual_fixture_paths() -> None:
    forbidden = (
        r"D:\DropSort",
        r"D:\DropSort_ chat",
        r"C:\Users\Example",
        ".manual-phase",
        ".pytest-tmp",
    )
    for path in (ROOT / "src/dropsort").rglob("*.py"):
        source = path.read_text(encoding="utf-8")
        assert not any(value in source for value in forbidden), path
