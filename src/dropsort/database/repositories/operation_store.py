from __future__ import annotations

import sqlite3

from dropsort.core.operations.errors import DatabaseCommitError, InvalidOperationStateError
from dropsort.core.operations.models import (
    FileOperationPlan,
    FileOperationRecord,
    OperationState,
    OperationUpdate,
)
from dropsort.database.connection.sqlite import Database
from dropsort.database.repositories.file_operations import FileOperationRepository
from dropsort.database.repositories.media_files import MediaFileRepository


class SqliteOperationStore:
    """SQLite adapter implementing the core operation-store contract."""

    def __init__(
        self,
        database: Database,
        operations: FileOperationRepository | None = None,
        media_files: MediaFileRepository | None = None,
    ) -> None:
        self.database = database
        self.operations = operations or FileOperationRepository(database)
        self.media_files = media_files or MediaFileRepository(database)

    def create(self, plan: FileOperationPlan) -> FileOperationRecord:
        return self.operations.create(plan)

    def get(self, operation_id: str) -> FileOperationRecord:
        return self.operations.get(operation_id)

    def list_nonterminal(self) -> list[FileOperationRecord]:
        return self.operations.list_nonterminal()

    def transition(
        self,
        operation_id: str,
        new_state: OperationState,
        update: OperationUpdate | None = None,
    ) -> FileOperationRecord:
        return self.operations.transition(operation_id, new_state, update)

    def commit_verified(self, operation_id: str) -> FileOperationRecord:
        record = self.operations.get(operation_id)
        if record.state is not OperationState.FS_VERIFIED:
            raise InvalidOperationStateError(f"Expected FS_VERIFIED, got {record.state.value}")
        try:
            with self.database.transaction() as conn:
                if record.media_file_id is not None:
                    self.media_files.update_path(record.media_file_id, record.destination, conn=conn)
                self.operations.transition(record.id, OperationState.COMMITTED, conn=conn)
        except (sqlite3.DatabaseError, KeyError) as exc:
            raise DatabaseCommitError(
                f"Filesystem succeeded but database commit failed for {record.id}"
            ) from exc
        return self.operations.get(record.id)
