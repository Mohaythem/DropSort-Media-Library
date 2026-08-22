from pathlib import Path

import pytest

from dropsort.core.operations.errors import InvalidOperationStateError
from dropsort.core.operations.models import OperationState, OperationType


def test_terminal_operation_cannot_transition(harness, media_bytes: bytes) -> None:
    source = harness.source_root / "movie.mkv"
    source.write_bytes(media_bytes)
    plan = harness.service.plan_move(source, harness.destination_root / "movie.mkv")
    record = harness.service.execute(plan.operation_id)
    assert record.state is OperationState.COMMITTED

    with pytest.raises(InvalidOperationStateError):
        harness.operations.transition(record.id, OperationState.RECOVERY_REQUIRED)


def test_unknown_operation_cannot_execute(harness) -> None:
    from dropsort.core.operations.errors import OperationNotFoundError

    with pytest.raises(OperationNotFoundError):
        harness.service.execute("not-journaled")
