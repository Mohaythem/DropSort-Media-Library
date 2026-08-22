from dropsort.ui.common.formatting import (
    format_file_size,
    format_runtime,
    title_initials,
)


def test_runtime_formatting_covers_hours_minutes_and_unknown() -> None:
    assert format_runtime(None) == "Runtime unavailable"
    assert format_runtime(45) == "45m"
    assert format_runtime(120) == "2h"
    assert format_runtime(152) == "2h 32m"


def test_file_size_formatting_is_presentation_only() -> None:
    assert format_file_size(999) == "999 B"
    assert format_file_size(1_500) == "1.5 KB"
    assert format_file_size(1_500_000) == "1.5 MB"
    assert format_file_size(1_500_000_000) == "1.5 GB"


def test_title_initials_are_bounded_and_safe() -> None:
    assert title_initials("") == "?"
    assert title_initials("Arrival") == "A"
    assert title_initials("The Dark Knight Rises") == "TDK"
