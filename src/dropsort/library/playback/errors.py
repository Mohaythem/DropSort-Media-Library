class LocalMediaActionError(RuntimeError):
    """Base class for controlled local media access failures."""


class MissingMediaFileError(LocalMediaActionError):
    """The cataloged media path no longer exists."""


class InvalidMediaFileError(LocalMediaActionError):
    """The cataloged path is not a regular physical media file."""


class LocalMediaLaunchError(LocalMediaActionError):
    """Windows could not launch the requested application action."""

