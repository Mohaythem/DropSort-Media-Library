class MetadataError(Exception):
    """Base error for provider-neutral metadata failures."""


class MetadataUnavailableError(MetadataError):
    """The provider cannot currently be reached or serve the request."""


class MetadataAuthenticationError(MetadataError):
    """Metadata credentials are missing or rejected."""


class MetadataRateLimitError(MetadataError):
    """The provider rejected the request because of a rate limit."""


class MetadataResponseError(MetadataError):
    """The provider returned an invalid or unsupported response."""
