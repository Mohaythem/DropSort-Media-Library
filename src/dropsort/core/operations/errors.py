class DropSortOperationError(Exception):
    """Base exception for safe file operations."""


class OperationNotFoundError(DropSortOperationError):
    pass


class InvalidOperationStateError(DropSortOperationError):
    pass


class DatabaseCommitError(DropSortOperationError):
    pass
