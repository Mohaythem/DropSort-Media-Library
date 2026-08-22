from __future__ import annotations

import ast
from pathlib import Path

import dropsort


_MATCHER_ROOT = Path(dropsort.__file__).parent / "media" / "matcher"


def test_matcher_has_only_approved_dependencies() -> None:
    forbidden = (
        "dropsort.core",
        "dropsort.database",
        "dropsort.metadata.providers",
        "PySide6",
        "urllib",
        "os",
        "shutil",
        "sqlite3",
    )

    assert not _imports_matching(_MATCHER_ROOT, forbidden)


def test_matcher_has_no_filesystem_or_operation_side_effect_calls() -> None:
    forbidden_calls = {
        "open",
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
        "FileOperationService",
    }

    assert forbidden_calls.isdisjoint(_referenced_calls(_MATCHER_ROOT))


def test_match_decision_models_have_no_filesystem_authorization_fields() -> None:
    from dropsort.media.matcher import CandidateScore, MatchDecision

    field_names = set(CandidateScore.__dataclass_fields__) | set(
        MatchDecision.__dataclass_fields__
    )

    assert field_names.isdisjoint(
        {"source_path", "destination_path", "authorized", "operation", "plan"}
    )


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
