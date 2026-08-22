from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest

from dropsort.core.operations import FileOperationService, RecoveryService
from dropsort.core.safety import PathPolicy
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import FileOperationRepository, MediaFileRepository, SqliteOperationStore


@dataclass
class Harness:
    database: Database
    operations: FileOperationRepository
    media_files: MediaFileRepository
    service: FileOperationService
    recovery: RecoveryService
    source_root: Path
    destination_root: Path


@pytest.fixture
def harness(tmp_path: Path) -> Harness:
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    source_root.mkdir()
    destination_root.mkdir()

    database = Database(tmp_path / "dropsort.db")
    MigrationRunner(database).migrate()
    operations = FileOperationRepository(database)
    media_files = MediaFileRepository(database)
    policy = PathPolicy([source_root, destination_root])
    store = SqliteOperationStore(database, operations, media_files)
    service = FileOperationService(policy, store)
    recovery = RecoveryService(store, policy)
    return Harness(
        database=database,
        operations=operations,
        media_files=media_files,
        service=service,
        recovery=recovery,
        source_root=source_root,
        destination_root=destination_root,
    )


@pytest.fixture
def media_bytes() -> bytes:
    return (b"DropSort-safe-media\n" * 1024) + b"EOF"
