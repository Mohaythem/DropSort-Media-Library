# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path

import PySide6


project_root = Path(SPECPATH)
source_root = project_root / "src"
pyside_root = Path(PySide6.__file__).parent

qt_vc_runtime_names = (
    "MSVCP140.dll",
    "MSVCP140_1.dll",
    "MSVCP140_2.dll",
    "VCRUNTIME140.dll",
    "VCRUNTIME140_1.dll",
)
qt_vc_runtime = [
    (str(pyside_root / name), ".")
    for name in qt_vc_runtime_names
]

runtime_data = [
    *[
        (str(path), "dropsort/database/migrations")
        for path in sorted(
            (source_root / "dropsort" / "database" / "migrations").glob("*.sql")
        )
    ],
    *[
        (str(path), "dropsort/ui/assets/fluent")
        for path in sorted(
            (source_root / "dropsort" / "ui" / "assets" / "fluent").glob("*.svg")
        )
    ],
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "Inter-Regular.otf"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "Inter-Bold.otf"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "NotoSansArabic-Regular.ttf"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "NotoSansArabic-Bold.ttf"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "Inter-OFL.txt"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "fonts" / "NotoSansArabic-OFL.txt"),
        "dropsort/ui/assets/fonts",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "tmdb" / "blue_long.svg"),
        "dropsort/ui/assets/tmdb",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "dropsort.svg"),
        "dropsort/ui/assets",
    ),
    (
        str(source_root / "dropsort" / "ui" / "assets" / "dropsort.ico"),
        "dropsort/ui/assets",
    ),
    *[
        (str(path), "licenses")
        for path in sorted((project_root / "licenses").glob("*.txt"))
    ],
]

analysis = Analysis(
    [str(source_root / "dropsort" / "__main__.py")],
    pathex=[str(source_root)],
    binaries=qt_vc_runtime,
    datas=runtime_data,
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["pytest", "coverage"],
    noarchive=False,
    optimize=0,
)

# Qt uses the Windows ICU shim.  A developer PATH can expose an unrelated
# unversioned ICU build (for example Poppler's), which PyInstaller would then
# collect ahead of the OS shim and make Qt6Core fail with WinError 127.
foreign_icu_names = {"icuuc.dll", "icudt78.dll"}
analysis.binaries = [
    entry
    for entry in analysis.binaries
    if Path(entry[0]).name.casefold() not in foreign_icu_names
]
pyz = PYZ(analysis.pure)

executable = EXE(
    pyz,
    analysis.scripts,
    [],
    exclude_binaries=True,
    name="DropSort",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    icon=str(source_root / "dropsort" / "ui" / "assets" / "dropsort.ico"),
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

distribution = COLLECT(
    executable,
    analysis.binaries,
    analysis.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="DropSort",
)
