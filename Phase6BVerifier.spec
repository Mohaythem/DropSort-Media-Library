# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path

project_root = Path(SPECPATH)
source_root = project_root / "src"
datas = [
    (str(path), "dropsort/database/migrations")
    for path in sorted((source_root / "dropsort" / "database" / "migrations").glob("*.sql"))
]
analysis = Analysis(
    [str(project_root / "tools" / "phase6b_verify.py")],
    pathex=[str(source_root)],
    binaries=[],
    datas=datas,
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["pytest", "coverage"],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(analysis.pure)
executable = EXE(
    pyz, analysis.scripts, analysis.binaries, analysis.datas, [],
    name="Phase6BVerifier", debug=False, strip=False, upx=True, console=True,
)
