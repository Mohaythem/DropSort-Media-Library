# ADR-0006: Filesystem Authority

**Status:** Accepted

The filesystem is authoritative for physical existence/path reality. SQLite stores catalog state and last-known locations. Missing files do not cause automatic catalog deletion.
