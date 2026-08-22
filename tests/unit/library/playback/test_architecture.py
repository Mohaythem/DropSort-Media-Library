from __future__ import annotations

import ast
from pathlib import Path


def test_playback_boundary_has_no_catalog_mutation_or_file_engine_dependency() -> None:
    root = Path("src/dropsort/library/playback")
    imports: set[str] = set()
    forbidden_calls = {"rename", "replace", "unlink", "remove", "move", "copy"}
    calls: set[str] = set()

    for path in root.rglob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imports.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imports.add(node.module)
            elif isinstance(node, ast.Call):
                if isinstance(node.func, ast.Name):
                    calls.add(node.func.id)
                elif isinstance(node.func, ast.Attribute):
                    calls.add(node.func.attr)

    assert not any(
        name == prefix or name.startswith(f"{prefix}.")
        for name in imports
        for prefix in (
            "dropsort.core.file_engine",
            "dropsort.core.operations",
            "dropsort.database",
            "sqlite3",
            "PySide6",
        )
    )
    assert forbidden_calls.isdisjoint(calls)


def test_windows_adapter_never_uses_a_shell_command() -> None:
    source = Path("src/dropsort/library/playback/windows.py").read_text(encoding="utf-8")
    tree = ast.parse(source)

    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        for keyword in node.keywords:
            assert not (keyword.arg == "shell" and isinstance(keyword.value, ast.Constant) and keyword.value.value is True)

