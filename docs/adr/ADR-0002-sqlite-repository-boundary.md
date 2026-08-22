# ADR-0002: SQLite Behind Repositories

**Status:** Accepted

SQLite is the embedded database. SQL is confined to `database/`; callers use repository contracts/classes. This prevents UI/domain code from becoming coupled to schema details.
