from __future__ import annotations

from typing import Protocol

from dropsort.library.operations.models import OperationJournalSnapshot


class OperationJournalReadRepository(Protocol):
    def list_operations(
        self,
        *,
        limit: int,
        offset: int,
    ) -> tuple[OperationJournalSnapshot, ...]: ...

    def get_operation(self, operation_id: str) -> OperationJournalSnapshot | None: ...

    def latest_relevant_for_media_file(
        self,
        media_file_id: int,
    ) -> OperationJournalSnapshot | None: ...
