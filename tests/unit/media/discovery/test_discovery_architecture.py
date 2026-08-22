from __future__ import annotations

import ast
from pathlib import Path

import dropsort


ROOT = Path(dropsort.__file__).parent


def test_discovery_has_no_provider_matcher_catalog_ui_or_mutation_dependencies() -> None:
    forbidden = (
        "dropsort.metadata",
        "dropsort.media.matcher",
        "dropsort.database",
        "dropsort.core.file_engine",
        "dropsort.core.operations",
        "PySide6",
        "urllib",
        "requests",
        "httpx",
        "shutil",
    )
    imports = _imports(ROOT / "media" / "discovery")

    assert not {
        name
        for name in imports
        if any(name == item or name.startswith(f"{item}.") for item in forbidden)
    }


def test_discovery_scanner_has_no_filesystem_mutation_calls() -> None:
    calls = _calls(ROOT / "media" / "discovery")
    forbidden = {
        "open",
        "Path.open",
        "Path.write_bytes",
        "Path.write_text",
        "Path.mkdir",
        "Path.rename",
        "Path.replace",
        "Path.unlink",
        "os.remove",
        "os.rename",
        "os.replace",
        "os.unlink",
        "shutil.copy",
        "shutil.move",
        "FileOperationService",
    }

    assert forbidden.isdisjoint(calls)


def test_discovery_application_use_case_does_not_import_os_or_concrete_scanner() -> None:
    path = ROOT / "application" / "use_cases" / "discover_media.py"
    imports = _imports(path)

    assert "os" not in imports
    assert "dropsort.media.discovery.scanner" not in imports


def _python_files(root: Path) -> tuple[Path, ...]:
    return tuple(root.rglob("*.py")) if root.is_dir() else (root,)


def _imports(root: Path) -> set[str]:
    result: set[str] = set()
    for path in _python_files(root):
        tree = ast.parse(path.read_text(encoding="utf-8"))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                result.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module is not None:
                result.add(node.module)
    return result


def _calls(root: Path) -> set[str]:
    result: set[str] = set()
    for path in _python_files(root):
        tree = ast.parse(path.read_text(encoding="utf-8"))
        result.update(
            _name(node.func) for node in ast.walk(tree) if isinstance(node, ast.Call)
        )
    return result


def _name(node: ast.expr) -> str:
    if isinstance(node, ast.Name):
        return node.id
    if isinstance(node, ast.Attribute):
        owner = _name(node.value)
        return f"{owner}.{node.attr}" if owner else node.attr
    return ""
