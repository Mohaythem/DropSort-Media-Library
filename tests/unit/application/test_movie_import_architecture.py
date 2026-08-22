from __future__ import annotations

import ast
from pathlib import Path

import dropsort


ROOT = Path(dropsort.__file__).parent
FILES = (
    ROOT / "application" / "dto" / "movie_import.py",
    ROOT / "application" / "use_cases" / "propose_movie_import.py",
    ROOT / "application" / "use_cases" / "movie_search_fallbacks.py",
    ROOT / "application" / "use_cases" / "confirm_movie_import.py",
)


def test_import_orchestration_has_no_sql_ui_tmdb_or_file_engine_dependencies() -> None:
    forbidden = (
        "sqlite3",
        "dropsort.database",
        "dropsort.metadata.providers",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "PySide6",
        "urllib",
        "requests",
        "httpx",
        "os",
        "shutil",
    )
    imports = set().union(*(_imports(path) for path in FILES))

    assert not {
        name
        for name in imports
        if any(name == item or name.startswith(f"{item}.") for item in forbidden)
    }


def test_proposal_generation_cannot_call_catalog_registrar_or_mutate_filesystem() -> None:
    path = ROOT / "application" / "use_cases" / "propose_movie_import.py"
    source = path.read_text(encoding="utf-8")
    calls = _calls(path)

    assert "RegisterMovieFile" not in source
    assert "ConfirmMovieImport" not in source
    assert {
        "open", "Path.rename", "Path.replace", "Path.unlink", "FileOperationService"
    }.isdisjoint(calls)


def test_orchestration_contains_no_sql_or_filesystem_mutation_calls() -> None:
    forbidden_calls = {
        "open",
        "Path.open",
        "Path.write_bytes",
        "Path.write_text",
        "Path.mkdir",
        "Path.rename",
        "Path.replace",
        "Path.unlink",
        "FileOperationService",
    }

    assert forbidden_calls.isdisjoint(set().union(*(_calls(path) for path in FILES)))
    assert not any(
        word in path.read_text(encoding="utf-8").upper()
        for path in FILES
        for word in ("SELECT *", "INSERT INTO", "UPDATE MOVIES", "DELETE FROM")
    )


def test_confirmation_does_not_import_or_interpret_matcher_thresholds() -> None:
    source = (
        ROOT / "application" / "use_cases" / "confirm_movie_import.py"
    ).read_text(encoding="utf-8")

    assert "media.matcher" not in source
    assert "confidence" not in source
    assert "threshold" not in source


def test_confirmation_models_contain_no_filesystem_authorization_fields() -> None:
    from dropsort.application.dto.movie_import import ConfirmMovieImportCommand

    fields = set(ConfirmMovieImportCommand.__dataclass_fields__)
    assert fields == {"proposal", "chosen_candidate"}


def _imports(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    result: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            result.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module is not None:
            result.add(node.module)
    return result


def _calls(path: Path) -> set[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    return {
        _name(node.func) for node in ast.walk(tree) if isinstance(node, ast.Call)
    }


def _name(node: ast.expr) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        owner = _name(node.value)
        return f"{owner}.{node.attr}" if owner else node.attr
    return ""
