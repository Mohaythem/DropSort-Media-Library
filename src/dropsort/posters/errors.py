class PosterError(Exception):
    """Base error for controlled poster acquisition failures."""


class PosterUnavailableError(PosterError):
    pass


class PosterAuthenticationError(PosterError):
    pass


class PosterResponseError(PosterError):
    pass


class PosterTooLargeError(PosterResponseError):
    pass
