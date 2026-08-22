from __future__ import annotations

from pathlib import Path


def test_organization_application_boundary_has_no_ui_http_metadata_or_matching_imports() -> None:
    root = Path(__file__).parents[3] / "src" / "dropsort" / "application"
    source = (root / "use_cases" / "organize_media_file.py").read_text(encoding="utf-8")

    forbidden = (
        "PySide6",
        "metadata.providers",
        "media.matcher",
        "media.parser",
        "urllib",
        "requests",
        "httpx",
        "shutil",
        ".rename(",
        ".replace(",
        ".unlink(",
    )
    assert all(token not in source for token in forbidden)


def test_unrelated_workflows_do_not_authorize_organization() -> None:
    root = Path(__file__).parents[3] / "src" / "dropsort"
    files = (
        root / "application" / "use_cases" / "confirm_movie_import.py",
        root / "application" / "use_cases" / "propose_movie_import.py",
        root / "media" / "matcher" / "matcher.py",
        root / "media" / "discovery" / "scanner.py",
        root / "posters" / "service.py",
        root / "library" / "playback" / "windows.py",
    )

    for path in files:
        source = path.read_text(encoding="utf-8")
        assert "OrganizeMediaFile" not in source
        assert "confirm_organization" not in source

