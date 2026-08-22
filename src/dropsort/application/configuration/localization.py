from __future__ import annotations

from enum import StrEnum
from typing import Protocol


class UiLanguage(StrEnum):
    ENGLISH = "en"
    ARABIC = "ar"


class UiLanguageRepository(Protocol):
    def get_language(self) -> str | None: ...

    def set_language(self, language: str) -> None: ...


class UiLanguageSettings:
    """Application boundary for the persisted desktop-language preference."""

    def __init__(self, repository: UiLanguageRepository) -> None:
        self._repository = repository

    def current_language(self) -> UiLanguage:
        value = self._repository.get_language()
        try:
            return UiLanguage(value) if value is not None else UiLanguage.ENGLISH
        except ValueError:
            return UiLanguage.ENGLISH

    def set_language(self, language: UiLanguage) -> UiLanguage:
        if not isinstance(language, UiLanguage):
            raise ValueError("language must be a supported UI language")
        self._repository.set_language(language.value)
        return language
