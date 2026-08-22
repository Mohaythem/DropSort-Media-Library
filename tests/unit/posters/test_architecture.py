from __future__ import annotations

import ast
from pathlib import Path


def _imports(root: Path) -> set[str]:
    values: set[str] = set()
    for path in root.rglob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                values.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                values.add(node.module)
    return values


def test_poster_asset_code_has_no_media_mutation_database_or_ui_dependency() -> None:
    imports = _imports(Path("src/dropsort/posters"))
    forbidden = (
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "dropsort.database",
        "dropsort.ui",
        "PySide6",
    )

    assert not {
        name
        for name in imports
        if any(name == item or name.startswith(f"{item}.") for item in forbidden)
    }


def test_parser_matcher_file_engine_and_repositories_do_not_depend_on_posters() -> None:
    roots = (
        Path("src/dropsort/media/parser"),
        Path("src/dropsort/media/matcher"),
        Path("src/dropsort/core"),
        Path("src/dropsort/database/repositories"),
    )

    assert not set().union(*(_imports(root) for root in roots)) & {"dropsort.posters"}
