from __future__ import annotations

import ast
from pathlib import Path

import dropsort


_SOURCE_ROOT = Path(dropsort.__file__).parent


def test_catalog_repositories_do_not_orchestrate_parser_matcher_provider_or_http() -> None:
    forbidden = (
        "dropsort.metadata.providers",
        "dropsort.media.matcher",
        "dropsort.media.parser.detector",
        "dropsort.media.parser.filename_parser",
        "PySide6",
        "urllib",
        "requests",
        "httpx",
    )

    assert not _imports_matching(_SOURCE_ROOT / "database" / "repositories", forbidden)


def test_catalog_application_does_not_depend_on_sqlite_ui_file_engine_or_tmdb() -> None:
    forbidden = (
        "sqlite3",
        "PySide6",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "dropsort.metadata.providers",
        "dropsort.media.matcher",
    )
    roots = (
        _SOURCE_ROOT / "application" / "dto" / "catalog.py",
        _SOURCE_ROOT / "application" / "use_cases" / "register_movie_file.py",
        _SOURCE_ROOT / "library" / "movies",
    )

    assert not set().union(*(_imports_matching(root, forbidden) for root in roots))


def test_catalog_application_contains_no_sql_or_filesystem_mutation_calls() -> None:
    roots = (
        _SOURCE_ROOT / "application" / "dto" / "catalog.py",
        _SOURCE_ROOT / "application" / "use_cases" / "register_movie_file.py",
        _SOURCE_ROOT / "library" / "movies",
    )
    forbidden_calls = {
        "open",
        "os.remove",
        "os.rename",
        "os.replace",
        "os.unlink",
        "shutil.copy",
        "shutil.move",
        "Path.rename",
        "Path.replace",
        "Path.unlink",
        "FileOperationService",
    }

    assert forbidden_calls.isdisjoint(
        set().union(*(_referenced_calls(root) for root in roots))
    )
    assert not any(
        sql_word in source_path.read_text(encoding="utf-8").upper()
        for root in roots
        for source_path in _source_files(root)
        for sql_word in ("SELECT ", "INSERT ", "UPDATE ", "DELETE ")
    )


def test_registration_command_has_no_match_threshold_or_authorization_fields() -> None:
    from dropsort.application.dto import RegisterMovieFileCommand

    fields = set(RegisterMovieFileCommand.__dataclass_fields__)

    assert fields.isdisjoint(
        {"confidence", "match_status", "threshold", "authorized", "destination_path"}
    )


def _imports_matching(root: Path, forbidden_prefixes: tuple[str, ...]) -> set[str]:
    violations: set[str] = set()
    for source_path in _source_files(root):
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
    for source_path in _source_files(root):
        tree = ast.parse(source_path.read_text(encoding="utf-8"))
        calls.update(
            _qualified_name(node.func)
            for node in ast.walk(tree)
            if isinstance(node, ast.Call)
        )
    return calls


def _source_files(root: Path) -> tuple[Path, ...]:
    return tuple(root.rglob("*.py")) if root.is_dir() else (root,)


def _qualified_name(node: ast.expr) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        owner = _qualified_name(node.value)
        return f"{owner}.{node.attr}" if owner else node.attr
    return ""
