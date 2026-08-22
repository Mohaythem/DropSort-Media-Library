# ADR-0003: Durable File Operation Journal

**Status:** Accepted

Every Move/Rename is journaled before filesystem mutation and follows a durable state machine. Filesystem success is recorded before authoritative media-path updates, enabling restart reconciliation when database commit fails.
