from __future__ import annotations

import ast
from pathlib import Path
import re


UI_ROOT = Path("src/dropsort/ui")
FORBIDDEN_IMPORTS = (
    "os",
    "subprocess",
    "sqlite3",
    "dropsort.database",
    "dropsort.metadata.providers",
    "dropsort.media.matcher",
    "dropsort.media.parser.filename_parser",
    "dropsort.core.file_engine",
    "dropsort.core.operations",
    "PySide6.QtNetwork",
    "urllib",
    "requests",
    "httpx",
)
FORBIDDEN_CALLS = {
    "open",
    "exists",
    "stat",
    "resolve",
    "rename",
    "replace",
    "unlink",
    "rmdir",
    "mkdir",
}


def _imports(tree: ast.AST) -> tuple[str, ...]:
    values: list[str] = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            values.extend(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            values.append(node.module)
    return tuple(values)


def test_ui_has_no_sql_http_provider_matcher_or_mutating_filesystem_dependencies() -> None:
    for path in UI_ROOT.rglob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        imports = _imports(tree)
        assert not any(
            imported == forbidden or imported.startswith(f"{forbidden}.")
            for imported in imports
            for forbidden in FORBIDDEN_IMPORTS
        ), path
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call):
                continue
            if isinstance(node.func, ast.Name):
                assert node.func.id not in FORBIDDEN_CALLS, path
            elif isinstance(node.func, ast.Attribute):
                assert node.func.attr not in FORBIDDEN_CALLS, path


def test_theme_colors_are_not_scattered_across_ui_modules() -> None:
    color_pattern = re.compile(r"#[0-9a-fA-F]{6}\b")
    offenders: list[Path] = []
    for path in UI_ROOT.rglob("*.py"):
        if path.name == "theme.py":
            continue
        if color_pattern.search(path.read_text(encoding="utf-8")):
            offenders.append(path)

    assert offenders == []


def test_widgets_do_not_embed_sql_statements() -> None:
    sql_pattern = re.compile(
        r"\b(?:SELECT\b.+\bFROM|INSERT\s+INTO|UPDATE\b.+\bSET|DELETE\s+FROM)\b",
        re.IGNORECASE | re.DOTALL,
    )
    offenders = [
        path
        for path in UI_ROOT.rglob("*.py")
        if sql_pattern.search(path.read_text(encoding="utf-8"))
    ]

    assert offenders == []


def test_widgets_do_not_call_platform_launch_or_file_validation_apis() -> None:
    forbidden_text = ("os.startfile", "subprocess.", ".exists(", ".is_file(")
    offenders = [
        path
        for path in UI_ROOT.rglob("*.py")
        if any(value in path.read_text(encoding="utf-8") for value in forbidden_text)
    ]

    assert offenders == []


def test_organization_widget_does_not_perform_physical_mutation_or_sql() -> None:
    source = (UI_ROOT / "organization" / "dialog.py").read_text(encoding="utf-8")
    forbidden = (
        "sqlite3",
        "database.repositories",
        "FileOperationService",
        "os.rename",
        "os.replace",
        "shutil",
        ".unlink(",
        ".rename(",
        ".replace(",
        "urllib",
        "requests",
        "httpx",
    )
    assert all(token not in source for token in forbidden)
