from __future__ import annotations

import ast
from pathlib import Path

import dropsort


SOURCE_ROOT = Path(dropsort.__file__).parent
LIBRARY_QUERY_FILES = (
    SOURCE_ROOT / "application" / "use_cases" / "list_movies.py",
    SOURCE_ROOT / "application" / "use_cases" / "get_movie_details.py",
    SOURCE_ROOT / "application" / "use_cases" / "_library_mapping.py",
)


def test_library_query_use_cases_depend_on_contracts_not_infrastructure() -> None:
    forbidden = (
        "sqlite3",
        "dropsort.database",
        "dropsort.metadata.providers",
        "dropsort.media.matcher",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "PySide6",
        "urllib",
        "requests",
        "httpx",
    )

    assert not set().union(*(_imports_matching(path, forbidden) for path in LIBRARY_QUERY_FILES))


def test_presentation_dtos_are_dependency_light() -> None:
    imports = _imports_under(SOURCE_ROOT / "application" / "dto" / "library.py")

    assert imports <= {"__future__", "dataclasses", "datetime", "enum"}


def test_library_query_code_has_no_filesystem_or_catalog_mutation_calls() -> None:
    roots = (
        SOURCE_ROOT / "application" / "dto" / "library.py",
        *LIBRARY_QUERY_FILES,
        SOURCE_ROOT / "library" / "movies" / "queries.py",
    )
    forbidden_calls = {
        "open",
        "Path.exists",
        "Path.resolve",
        "Path.stat",
        "os.path.exists",
        "os.stat",
        "os.remove",
        "os.rename",
        "shutil.copy",
        "shutil.move",
        "FileOperationService",
    }

    assert forbidden_calls.isdisjoint(
        set().union(*(_referenced_calls(root) for root in roots))
    )
    assert not any(
        word in path.read_text(encoding="utf-8").upper()
        for root in roots
        for path in _python_files(root)
        for word in ("SELECT ", "INSERT ", "UPDATE ", "DELETE ")
    )


def test_database_read_repository_has_no_provider_matcher_parser_ui_or_mutation_imports() -> None:
    repository = SOURCE_ROOT / "database" / "repositories" / "library_queries.py"
    forbidden = (
        "dropsort.metadata.providers",
        "dropsort.media.matcher",
        "dropsort.media.parser",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "PySide6",
        "urllib",
        "requests",
        "httpx",
        "os",
        "shutil",
    )

    assert not _imports_matching(repository, forbidden)
    source = repository.read_text(encoding="utf-8").upper()
    assert "INSERT " not in source
    assert "UPDATE " not in source
    assert "DELETE " not in source


def _imports_matching(root: Path, forbidden: tuple[str, ...]) -> set[str]:
    return {
        imported
        for path in _python_files(root)
        for imported in _imports_under(path)
        if any(imported == item or imported.startswith(f"{item}.") for item in forbidden)
    }


def _imports_under(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    imports: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imports.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module is not None:
            imports.add(node.module)
    return imports


def _python_files(root: Path) -> tuple[Path, ...]:
    return tuple(root.rglob("*.py")) if root.is_dir() else (root,)


def _referenced_calls(root: Path) -> set[str]:
    calls: set[str] = set()
    for path in _python_files(root):
        tree = ast.parse(path.read_text(encoding="utf-8"))
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
