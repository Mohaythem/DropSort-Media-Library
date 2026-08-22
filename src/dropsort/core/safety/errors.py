class FileSafetyError(Exception):
    """Base class for safety-policy failures."""


class UnsafePathError(FileSafetyError):
    pass


class SourceMissingError(FileSafetyError):
    pass


class DestinationExistsError(FileSafetyError):
    pass


class CaseInsensitiveCollisionError(DestinationExistsError):
    pass


class SameFileError(FileSafetyError):
    pass


class InvalidRenameError(FileSafetyError):
    pass


class LinkTraversalError(FileSafetyError):
    pass


class SourceChangedError(FileSafetyError):
    pass
