from __future__ import annotations

from typing import get_type_hints

from dropsort.library.movies import (
    CatalogUnitOfWork,
    MediaFileRepository,
    MovieRepository,
)


def test_repository_contracts_are_specific_and_do_not_expose_sql() -> None:
    assert {
        "get_by_id",
        "get_by_external_id",
        "create",
        "update_metadata",
        "list_all",
    } <= set(MovieRepository.__dict__)
    assert {
        "get_by_id",
        "get_by_path",
        "add",
        "refresh_verified_facts",
        "link_to_movie",
        "mark_missing",
        "mark_present",
        "list_for_movie",
    } <= set(MediaFileRepository.__dict__)

    annotations = repr(get_type_hints(CatalogUnitOfWork))
    assert "sqlite3" not in annotations
    assert "Connection" not in annotations


def test_catalog_unit_of_work_is_an_application_facing_context_boundary() -> None:
    assert {"__enter__", "__exit__"} <= set(CatalogUnitOfWork.__dict__)
