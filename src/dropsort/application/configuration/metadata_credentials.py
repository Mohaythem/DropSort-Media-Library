from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from enum import Enum
import os
from threading import Lock


TMDB_READ_ACCESS_TOKEN_ENVIRONMENT_VARIABLE = "DROPSORT_TMDB_READ_ACCESS_TOKEN"
_MINIMUM_TOKEN_LENGTH = 20
_MAXIMUM_TOKEN_LENGTH = 4096


class MetadataCredentialOrigin(Enum):
    NOT_CONFIGURED = "NOT_CONFIGURED"
    ENVIRONMENT = "ENVIRONMENT"
    SESSION = "SESSION"


@dataclass(frozen=True, slots=True)
class MetadataCredentialStatus:
    configured: bool
    origin: MetadataCredentialOrigin

    def __post_init__(self) -> None:
        expected = self.origin is not MetadataCredentialOrigin.NOT_CONFIGURED
        if self.configured is not expected:
            raise ValueError("credential status is inconsistent")


class SessionTmdbCredentials:
    """Resolve a TMDB token without persisting or exposing the secret."""

    def __init__(self, *, environment: Mapping[str, str] | None = None) -> None:
        self._environment = os.environ if environment is None else environment
        self._session_token: str | None = None
        self._lock = Lock()

    def access_token(self) -> str | None:
        with self._lock:
            return self._session_token or self._environment_token()

    def status(self) -> MetadataCredentialStatus:
        with self._lock:
            if self._session_token is not None:
                origin = MetadataCredentialOrigin.SESSION
            elif self._environment_token() is not None:
                origin = MetadataCredentialOrigin.ENVIRONMENT
            else:
                origin = MetadataCredentialOrigin.NOT_CONFIGURED
        return MetadataCredentialStatus(
            configured=origin is not MetadataCredentialOrigin.NOT_CONFIGURED,
            origin=origin,
        )

    def set_session_token(self, token: str) -> None:
        normalized = _validated_token(token)
        with self._lock:
            self._session_token = normalized

    def clear_session_token(self) -> None:
        with self._lock:
            self._session_token = None

    def _environment_token(self) -> str | None:
        value = self._environment.get(TMDB_READ_ACCESS_TOKEN_ENVIRONMENT_VARIABLE)
        if value is None:
            return None
        try:
            return _validated_token(value)
        except ValueError:
            return None

    def __repr__(self) -> str:
        return f"{type(self).__name__}(credential=<redacted>)"


class MetadataSettings:
    """Application actions for session-scoped metadata configuration."""

    def __init__(self, credentials: SessionTmdbCredentials) -> None:
        self._credentials = credentials

    def metadata_credential_status(self) -> MetadataCredentialStatus:
        return self._credentials.status()

    def apply_tmdb_session_token(self, token: str) -> MetadataCredentialStatus:
        self._credentials.set_session_token(token)
        return self._credentials.status()

    def clear_tmdb_session_token(self) -> MetadataCredentialStatus:
        self._credentials.clear_session_token()
        return self._credentials.status()

    def __repr__(self) -> str:
        return f"{type(self).__name__}(credential=<redacted>)"


def _validated_token(token: str) -> str:
    if not isinstance(token, str):
        raise ValueError("TMDB token must be text")
    normalized = token.strip()
    if (
        len(normalized) < _MINIMUM_TOKEN_LENGTH
        or len(normalized) > _MAXIMUM_TOKEN_LENGTH
        or any(character.isspace() for character in normalized)
    ):
        raise ValueError("Enter a valid TMDB Read Access Token")
    return normalized
