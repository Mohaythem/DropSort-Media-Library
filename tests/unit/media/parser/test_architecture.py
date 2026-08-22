from __future__ import annotations

import ast
from pathlib import Path

from dropsort.media.parser import detector, filename_parser, models


_FORBIDDEN_IMPORT_PREFIXES = (
    "PySide6",
    "http",
    "httpx",
    "requests",
    "sqlite3",
    "urllib",
    "dropsort.database",
    "dropsort.ui",
    "dropsort.core.file_engine",
)


def test_parser_modules_do_not_depend_on_io_or_presentation_layers() -> None:
    for module in (models, detector, filename_parser):
        module_path = Path(module.__file__)
        tree = ast.parse(module_path.read_text(encoding="utf-8"))
        imported_modules = _imported_modules(tree)

        assert not any(
            imported == forbidden or imported.startswith(f"{forbidden}.")
            for imported in imported_modules
            for forbidden in _FORBIDDEN_IMPORT_PREFIXES
        )


def test_parser_modules_do_not_reference_filesystem_mutation_calls() -> None:
    forbidden_calls = {
        "os.remove",
        "os.rename",
        "os.replace",
        "os.unlink",
        "shutil.copy",
        "shutil.copy2",
        "shutil.move",
        "Path.rename",
        "Path.replace",
        "Path.unlink",
    }
    for module in (models, detector, filename_parser):
        module_path = Path(module.__file__)
        tree = ast.parse(module_path.read_text(encoding="utf-8"))
        referenced_calls = {
            _qualified_name(node.func)
            for node in ast.walk(tree)
            if isinstance(node, ast.Call)
        }

        assert forbidden_calls.isdisjoint(referenced_calls)


def _imported_modules(tree: ast.AST) -> set[str]:
    imports: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imports.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module is not None:
            imports.add(node.module)
    return imports


def _qualified_name(node: ast.expr) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        owner = _qualified_name(node.value)
        return f"{owner}.{node.attr}" if owner else node.attr
    return ""
