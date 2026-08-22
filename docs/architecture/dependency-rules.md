# Dependency Rules

Phase 1 runtime dependency footprint is intentionally minimal:

- Python standard library for SQLite and filesystem logic.
- PySide6 is declared for the eventual Windows desktop shell but is not imported by Phase 1 core.
- pytest/pytest-cov are development-only.

No HTTP client, watchdog, ORM, backend server, cloud SDK, Docker, Redis, Kafka, or distributed-system dependency is introduced in Phase 1.
