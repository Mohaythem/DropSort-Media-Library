from __future__ import annotations

from typing import Protocol

from dropsort.core.operations.models import (
    FileOperationPlan,
    FileOperationRecord,
    OperationState,
    OperationUpdate,
)


class OperationStore(Protocol):
    def create(self, plan: FileOperationPlan) -> FileOperationRecord: ...

    def get(self, operation_id: str) -> FileOperationRecord: ...

    def list_nonterminal(self) -> list[FileOperationRecord]: ...

    def transition(
        self,
        operation_id: str,
        new_state: OperationState,
        update: OperationUpdate | None = None,
    ) -> FileOperationRecord: ...

    def commit_verified(self, operation_id: str) -> FileOperationRecord: ...
