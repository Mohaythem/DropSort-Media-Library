class OperationJournalError(Exception):
    """Base error for operation-journal read contracts."""


class OperationJournalQueryError(OperationJournalError):
    """The durable operation journal could not be read."""
