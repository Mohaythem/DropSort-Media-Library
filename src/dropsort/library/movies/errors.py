class CatalogError(Exception):
    """Base error for controlled movie-catalog failures."""


class CatalogDataError(CatalogError):
    """Persisted catalog data is malformed or cannot be normalized."""


class CatalogQueryError(CatalogError):
    """The local catalog could not complete a read query."""


class CatalogRecordNotFoundError(CatalogError):
    """A requested catalog record does not exist."""


class CatalogIntegrityError(CatalogError):
    """A catalog relationship or constraint is invalid."""


class MovieIdentityConflictError(CatalogError):
    """A movie with the same provider identity already exists."""


class MediaFilePathConflictError(CatalogError):
    """A media-file row already owns the Windows-normalized path."""


class MediaFileAssociationConflict(CatalogError):
    """A physical media file is already associated with another movie."""


class CatalogMaintenanceBlockedError(CatalogError):
    """Safety-critical journal state prevents catalog maintenance."""


class CatalogMaintenanceError(CatalogError):
    """Catalog maintenance could not be committed atomically."""
