# Module Boundaries

- `core/file_engine`: low-level file transfer mechanics only; no media concepts, SQL, HTTP, or UI.
- `core/safety`: approved-root and path/collision policy.
- `core/operations`: plans, operation states, journaling orchestration, execution, verification, reverse planning, recovery.
- `database`: SQLite connection, migrations, repositories. SQL does not escape this module.
- `media`: reserved for parsing/matching after Phase 1 approval.
- `metadata`: reserved provider adapters/cache after Phase 1 approval.
- `library`: reserved application-facing library behavior after Phase 1 approval.
- `ui`: presentation only; it must never perform filesystem, SQL, or HTTP work directly.

Circular dependencies are forbidden. File-engine code must not depend on movie/library/UI modules.
