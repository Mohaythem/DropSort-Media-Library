from __future__ import annotations

import logging

import pytest

from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
    MetadataSettings,
    SessionTmdbCredentials,
)


ENV_TOKEN = "environment-token-value-1234567890"
SESSION_TOKEN = "session-token-value-123456789012345"


def test_no_session_or_environment_token_is_not_configured() -> None:
    credentials = SessionTmdbCredentials(environment={})

    assert credentials.access_token() is None
    assert credentials.status().origin is MetadataCredentialOrigin.NOT_CONFIGURED
    assert credentials.status().configured is False


def test_environment_token_is_used_when_session_is_empty() -> None:
    credentials = SessionTmdbCredentials(
        environment={credentials_environment_name(): ENV_TOKEN}
    )

    assert credentials.access_token() == ENV_TOKEN
    assert credentials.status().origin is MetadataCredentialOrigin.ENVIRONMENT


def test_session_token_overrides_environment_and_clear_restores_fallback() -> None:
    credentials = SessionTmdbCredentials(
        environment={credentials_environment_name(): ENV_TOKEN}
    )

    credentials.set_session_token(SESSION_TOKEN)
    assert credentials.access_token() == SESSION_TOKEN
    assert credentials.status().origin is MetadataCredentialOrigin.SESSION

    credentials.clear_session_token()
    assert credentials.access_token() == ENV_TOKEN
    assert credentials.status().origin is MetadataCredentialOrigin.ENVIRONMENT


@pytest.mark.parametrize(
    "token",
    ("", "   ", "short", "contains whitespace token 123456"),
)
def test_invalid_session_token_is_rejected_without_echoing_value(token: str) -> None:
    credentials = SessionTmdbCredentials(environment={})

    with pytest.raises(ValueError) as caught:
        credentials.set_session_token(token)

    if token.strip():
        assert token.strip() not in str(caught.value)
    assert credentials.access_token() is None


def test_settings_service_never_returns_secret_and_repr_is_redacted() -> None:
    credentials = SessionTmdbCredentials(environment={})
    settings = MetadataSettings(credentials)

    status = settings.apply_tmdb_session_token(SESSION_TOKEN)

    assert status.origin is MetadataCredentialOrigin.SESSION
    assert SESSION_TOKEN not in repr(credentials)
    assert SESSION_TOKEN not in repr(settings)
    assert SESSION_TOKEN not in repr(status)


def test_token_is_not_logged(caplog: pytest.LogCaptureFixture) -> None:
    credentials = SessionTmdbCredentials(environment={})
    settings = MetadataSettings(credentials)
    caplog.set_level(logging.DEBUG)

    settings.apply_tmdb_session_token(SESSION_TOKEN)
    settings.clear_tmdb_session_token()

    assert SESSION_TOKEN not in caplog.text


def test_invalid_environment_value_is_treated_as_not_configured() -> None:
    credentials = SessionTmdbCredentials(
        environment={credentials_environment_name(): "invalid value with spaces"}
    )

    assert credentials.access_token() is None
    assert credentials.status().origin is MetadataCredentialOrigin.NOT_CONFIGURED


def test_credential_status_rejects_inconsistent_state() -> None:
    with pytest.raises(ValueError, match="inconsistent"):
        MetadataCredentialStatus(
            configured=True,
            origin=MetadataCredentialOrigin.NOT_CONFIGURED,
        )


def test_non_text_and_overlong_session_values_are_rejected() -> None:
    credentials = SessionTmdbCredentials(environment={})

    with pytest.raises(ValueError, match="text"):
        credentials.set_session_token(object())  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="valid"):
        credentials.set_session_token("x" * 4097)


def credentials_environment_name() -> str:
    return "DROPSORT_TMDB_READ_ACCESS_TOKEN"
