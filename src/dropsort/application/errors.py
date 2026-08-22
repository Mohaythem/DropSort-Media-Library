from enum import StrEnum
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from dropsort.application.dto.import_review import ImportReviewProgress


class LibraryQueryError(Exception):
    """The local movie library could not be read."""


class MovieNotFoundError(LibraryQueryError):
    """The requested movie is not present in the local catalog."""


class CatalogClearError(Exception):
    """The explicit library-data reset could not complete."""


class CatalogClearBlockedError(CatalogClearError):
    """Concurrent or recoverable work makes clearing unsafe."""


class MovieImportError(Exception):
    """Base error for an explicitly requested movie catalog import."""


class MovieImportMetadataError(MovieImportError):
    """Movie details could not be loaded or did not match the confirmation."""


class MovieImportCatalogError(MovieImportError):
    """The explicitly confirmed catalog registration failed."""


class ImportReviewCancelled(Exception):
    """One read-only folder review session ended cooperatively."""

    def __init__(self, progress: "ImportReviewProgress") -> None:
        self.progress = progress
        super().__init__("folder import review was cancelled")


class OrganizationError(Exception):
    """Base error for explicit per-file organization."""


class OrganizationValidationCode(StrEnum):
    INVALID_REQUEST = "INVALID_REQUEST"
    CATALOG_MISMATCH = "CATALOG_MISMATCH"
    SOURCE_MISSING = "SOURCE_MISSING"
    DESTINATION_EXISTS = "DESTINATION_EXISTS"
    CASE_COLLISION = "CASE_COLLISION"
    SAME_FILE = "SAME_FILE"
    LINK_TRAVERSAL = "LINK_TRAVERSAL"
    UNSAFE_PATH = "UNSAFE_PATH"


class OrganizationValidationError(OrganizationError):
    """A preview could not satisfy the Phase 1 path-safety policy."""

    def __init__(
        self,
        message: str,
        code: OrganizationValidationCode = OrganizationValidationCode.INVALID_REQUEST,
    ) -> None:
        super().__init__(message)
        self.code = code


class OrganizationPreviewNotFoundError(OrganizationError):
    """The requested in-memory preview is unavailable or expired."""


class OrganizationPreviewStaleError(OrganizationError):
    """The source or destination changed after the user reviewed the preview."""


class OrganizationAlreadyConfirmedError(OrganizationError):
    """The same preview cannot authorize a second physical operation."""


class OrganizationExecutionError(OrganizationError):
    """The operation failed while the original remained authoritative."""


class OrganizationRecoveryRequiredError(OrganizationError):
    """Filesystem/catalog state requires recovery rather than automatic retry."""

    def __init__(self, operation_id: str, message: str) -> None:
        super().__init__(message)
        self.operation_id = operation_id


class OperationHistoryError(Exception):
    """Operation history, undo, or recovery could not be completed safely."""


class OperationHistoryNotFoundError(OperationHistoryError):
    """The requested durable journal record does not exist."""


class UndoError(OperationHistoryError):
    """Base error for explicit reverse-operation requests."""


class UndoNotEligibleError(UndoError):
    def __init__(self, code, message: str) -> None:
        super().__init__(message)
        self.code = code


class UndoPreviewNotFoundError(UndoError):
    pass


class UndoAlreadyConfirmedError(UndoError):
    pass


class UndoPreviewStaleError(UndoError):
    pass


class UndoExecutionError(UndoError):
    pass


class UndoRecoveryRequiredError(UndoError):
    def __init__(self, operation_id: str, message: str) -> None:
        super().__init__(message)
        self.operation_id = operation_id


class RecoveryActionUnavailableError(OperationHistoryError):
    pass


class LibraryReconciliationError(Exception):
    """The explicit read-only availability check could not complete coherently."""


class LibraryReconciliationCancelled(LibraryReconciliationError):
    def __init__(self, progress) -> None:
        self.progress = progress
        super().__init__("library file reconciliation was cancelled")


class RelinkError(Exception):
    """Base error for explicit catalog-only media relink requests."""


class RelinkValidationCode(StrEnum):
    INVALID_REQUEST = "INVALID_REQUEST"
    MEDIA_FILE_NOT_FOUND = "MEDIA_FILE_NOT_FOUND"
    MEDIA_FILE_NOT_MISSING = "MEDIA_FILE_NOT_MISSING"
    ORIGINAL_PATH_AVAILABLE = "ORIGINAL_PATH_AVAILABLE"
    ORIGINAL_PATH_UNVERIFIED = "ORIGINAL_PATH_UNVERIFIED"
    CANDIDATE_UNAVAILABLE = "CANDIDATE_UNAVAILABLE"
    UNSAFE_LINK = "UNSAFE_LINK"
    UNSUPPORTED_MEDIA = "UNSUPPORTED_MEDIA"
    EXTENSION_MISMATCH = "EXTENSION_MISMATCH"
    SIZE_MISMATCH = "SIZE_MISMATCH"
    TITLE_MISMATCH = "TITLE_MISMATCH"
    YEAR_MISMATCH = "YEAR_MISMATCH"
    TECHNICAL_MISMATCH = "TECHNICAL_MISMATCH"
    CATALOG_CONFLICT = "CATALOG_CONFLICT"


class RelinkValidationError(RelinkError):
    def __init__(self, message: str, code: RelinkValidationCode) -> None:
        self.code = code
        super().__init__(message)


class RelinkPreviewNotFoundError(RelinkError):
    pass


class RelinkAlreadyConfirmedError(RelinkError):
    pass


class RelinkPreviewStaleError(RelinkError):
    pass


class RelinkCatalogError(RelinkError):
    pass
