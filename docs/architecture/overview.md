# Architecture Overview

DropSort is a **modular monolith**: one local Windows desktop process, explicit module boundaries, SQLite storage, and no backend server or distributed infrastructure.

Dependency direction:

```text
UI -> Application -> Library / Media / Core -> contracts
                                      ^
                         Database / Metadata adapters
```

Phase 1 implements only `core` and the SQLite foundation. `media`, `metadata`, `library`, and `ui` exist as reserved boundaries but contain no feature implementation yet.

Physical filesystem state is authoritative for file existence/path reality. SQLite is the catalog, metadata store, and durable operation journal. A database path is never advanced to a new location until the filesystem result has been verified.
