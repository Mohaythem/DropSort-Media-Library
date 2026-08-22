from __future__ import annotations

import ast
from pathlib import Path

import dropsort


_SOURCE_ROOT = Path(dropsort.__file__).parent


def test_parser_does_not_depend_on_metadata() -> None:
    assert not _imports_matching(_SOURCE_ROOT / "media" / "parser", ("dropsort.metadata",))


def test_core_does_not_depend_on_http_or_metadata_providers() -> None:
    assert not _imports_matching(
        _SOURCE_ROOT / "core",
        ("http", "httpx", "requests", "urllib", "dropsort.metadata.providers"),
    )


def test_database_repositories_do_not_depend_on_http() -> None:
    assert not _imports_matching(
        _SOURCE_ROOT / "database" / "repositories",
        ("http", "httpx", "requests", "urllib"),
    )


def test_metadata_does_not_depend_on_ui_file_engine_or_matching() -> None:
    assert not _imports_matching(
        _SOURCE_ROOT / "metadata",
        ("PySide6", "dropsort.ui", "dropsort.core.file_engine", "dropsort.media.matcher"),
    )


def test_provider_code_does_not_reference_filesystem_mutation_calls() -> None:
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
    referenced = _referenced_calls(_SOURCE_ROOT / "metadata" / "providers")

    assert forbidden_calls.isdisjoint(referenced)


def _imports_matching(root: Path, forbidden_prefixes: tuple[str, ...]) -> set[str]:
    violations: set[str] = set()
    for source_path in root.rglob("*.py"):
        tree = ast.parse(source_path.read_text(encoding="utf-8"))
        for imported in _imported_modules(tree):
            if any(
                imported == prefix or imported.startswith(f"{prefix}.")
                for prefix in forbidden_prefixes
            ):
                violations.add(f"{source_path}:{imported}")
    return violations


def _imported_modules(tree: ast.AST) -> set[str]:
    imports: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imports.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module is not None:
            imports.add(node.module)
    return imports


def _referenced_calls(root: Path) -> set[str]:
    calls: set[str] = set()
    for source_path in root.rglob("*.py"):
        tree = ast.parse(source_path.read_text(encoding="utf-8"))
        calls.update(
            _qualified_name(node.func)
            for node in ast.walk(tree)
            if isinstance(node, ast.Call)
        )
    return calls


def _qualified_name(node: ast.expr) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        owner = _qualified_name(node.value)
        return f"{owner}.{node.attr}" if owner else node.attr
    return ""
