---
name: dropsort-project-rules
description: Enforce DropSort architecture and V1 scope on every implementation or refactor.
---
# DropSort Project Rules

- Windows desktop application, local-first.
- Python + PySide6 + SQLite.
- Modular monolith; no Docker, backend server, cloud, microservices, Redis, Kafka, or unnecessary infrastructure.
- UI is presentation only and never performs filesystem, SQL, or HTTP operations directly.
- File Engine stays independent from Media Library concepts.
- Preserve explicit module boundaries and dependency direction.
- Keep V1 scope locked; Folder Watcher and V2+ features are not implemented early.
- Architecture changes require a concrete implementation problem and an ADR/update before code changes.
