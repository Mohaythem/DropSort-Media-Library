from __future__ import annotations

from datetime import datetime


_EASTERN_DIGITS = str.maketrans(
    "٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹",
    "01234567890123456789",
)


def to_western_numerals(value: object) -> str:
    """Normalize display text without changing stored/domain values."""

    return str(value).translate(_EASTERN_DIGITS)


def format_date(value: datetime | None) -> str:
    if value is None:
        return "Date unavailable"
    return to_western_numerals(value.strftime("%b %d, %Y"))


def format_datetime(value: datetime | None) -> str:
    if value is None:
        return "Timestamp unavailable"
    return to_western_numerals(value.astimezone().strftime("%Y-%m-%d %H:%M:%S"))


def format_year(year: int | None) -> str:
    return to_western_numerals(year) if year is not None else "Year unavailable"


def format_rating(rating: float | None) -> str:
    return to_western_numerals(f"{rating:.1f} / 10") if rating is not None else "Not rated"


def format_runtime(minutes: int | None) -> str:
    if minutes is None:
        return "Runtime unavailable"
    hours, remaining = divmod(minutes, 60)
    if hours and remaining:
        return to_western_numerals(f"{hours}h {remaining}m")
    if hours:
        return to_western_numerals(f"{hours}h")
    return to_western_numerals(f"{remaining}m")


def format_file_size(size_bytes: int) -> str:
    size = float(size_bytes)
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1_000 or unit == "GB":
            precision = 0 if unit == "B" else 1
            return to_western_numerals(f"{size:.{precision}f} {unit}")
        size /= 1_000
    raise AssertionError("unreachable")


def title_initials(title: str) -> str:
    words = tuple(word for word in title.split() if word)
    if not words:
        return "?"
    return "".join(word[0].upper() for word in words[:3])
