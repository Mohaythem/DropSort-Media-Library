# Phase 1 Dependency Audit

Method follows the selected `dependency-auditor` concerns: minimum dependency count, vulnerability status, license surface, and bounded upgrade ranges.

Skill source: https://github.com/alirezarezvani/claude-skills/tree/main/engineering/skills/dependency-auditor

## Manifest

Runtime:
- `PySide6>=6.11.1,<7`

Development:
- `pytest>=9.1.1,<10`
- `pytest-cov>=7.1,<8`

Build backend:
- `setuptools>=75`

No watchdog, HTTP client, ORM, Docker, cloud SDK, Redis, Kafka, server framework, or database server dependency is present in Phase 1.

## Current-source verification

- PyPI lists PySide6 6.11.1 as current and its license expression as `LGPL-3.0-only OR GPL-2.0-only OR GPL-3.0-only`.
- PyPI lists pytest 9.1.1 as current.
- OSV GHSA-6w46-j5rx-g56g / CVE-2025-71176 affects pytest through 9.0.2 on UNIX and is fixed in 9.0.3. The project floor is therefore set above the fixed version at 9.1.1.
- PyPI lists pytest-cov 7.1.0 as current.
- PyPI lists setuptools 83.0.0 as current; the build backend remains a broad minimum because it is not shipped as a DropSort runtime dependency.

References:
- https://pypi.org/project/PySide6/
- https://pypi.org/project/pytest/
- https://pypi.org/project/pytest-cov/
- https://pypi.org/project/setuptools/
- https://osv.dev/vulnerability/GHSA-6w46-j5rx-g56g

## Sandbox limitation

The sandbox already contains pytest 9.0.2 and pytest-cov 7.0.0 and has no package-install network path for upgrading them or installing PySide6. Final tests were therefore run with the existing pytest but forced to use a repository-local `--basetemp`, avoiding the vulnerable default `/tmp/pytest-of-{user}` path pattern described by the advisory. The project manifest itself requires the fixed/current pytest line.

A global `pip check` is not a valid project-environment check here because the shared sandbox contains unrelated packages; it reports an unrelated `moviepy`/`pillow` conflict. No project runtime package created that conflict.

## License note

PySide6's Qt licensing must be reviewed for distribution/compliance before packaging a public executable. This is a release/legal-compliance item, not a Phase 1 filesystem-safety blocker.

## Verdict

**PASS for Phase 1 with one environment limitation:** project dependency ranges are minimal and the known pytest advisory is excluded by the manifest. A clean Windows virtual environment with the declared versions is required before release/beta validation.
