from __future__ import annotations

from pathlib import Path


PROJECT_ROOT = Path(__file__).parents[2]


def test_packaging_bundles_the_matching_pyside_visual_c_runtime() -> None:
    spec = (PROJECT_ROOT / "DropSort.spec").read_text(encoding="utf-8")

    assert "binaries=qt_vc_runtime" in spec
    assert "Path(PySide6.__file__).parent" in spec
    for runtime_name in (
        "MSVCP140.dll",
        "MSVCP140_1.dll",
        "MSVCP140_2.dll",
        "VCRUNTIME140.dll",
        "VCRUNTIME140_1.dll",
    ):
        assert runtime_name in spec


def test_packaging_filters_foreign_icu_binaries_from_the_build_environment() -> None:
    spec = (PROJECT_ROOT / "DropSort.spec").read_text(encoding="utf-8")

    assert 'foreign_icu_names = {"icuuc.dll", "icudt78.dll"}' in spec
    assert "analysis.binaries = [" in spec
    assert "Path(entry[0]).name.casefold() not in foreign_icu_names" in spec
