$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$pyInstaller = Join-Path $projectRoot ".venv\Scripts\pyinstaller.exe"
$specPath = Join-Path $projectRoot "DropSort.spec"
$releaseRoot = Join-Path $projectRoot "release"
$workRoot = Join-Path $projectRoot ".build-phase6b"
$distribution = Join-Path $releaseRoot "DropSort"

if (-not (Test-Path -LiteralPath $pyInstaller -PathType Leaf)) {
    throw "PyInstaller is not installed in the project virtual environment."
}

Push-Location -LiteralPath $projectRoot
try {
    & $pyInstaller --clean --noconfirm --distpath $releaseRoot --workpath $workRoot $specPath
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller failed with exit code $LASTEXITCODE."
    }

    Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $distribution -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "THIRD_PARTY_NOTICES.md") -Destination $distribution -Force
}
finally {
    Pop-Location
}
