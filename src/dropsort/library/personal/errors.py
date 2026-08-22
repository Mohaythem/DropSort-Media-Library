class PersonalLibraryError(Exception):
    """Base error for database-only personal-library operations."""


class PersonalMovieNotFoundError(PersonalLibraryError):
    """The requested logical Movie does not exist."""


class WatchEventNotFoundError(PersonalLibraryError):
    """The requested watch event does not exist."""
