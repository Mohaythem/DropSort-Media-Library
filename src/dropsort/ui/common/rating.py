from __future__ import annotations

from math import isfinite


def provider_rating_stars(rating: float | None) -> str:
    """Return a five-position read-only visual for a provider 0-10 rating."""

    if rating is None:
        return ""
    try:
        value = float(rating)
    except (TypeError, ValueError):
        return ""
    if not isfinite(value):
        return ""
    value = max(0.0, min(10.0, value))
    half_steps = max(0, min(10, int(value / 2 * 2 + 0.5)))
    full = half_steps // 2
    half = half_steps % 2
    empty = 5 - full - half
    return "★" * full + ("½" if half else "") + "☆" * empty


def provider_rating_text(rating: float | None) -> str:
    """Keep the authoritative provider value visible in its 0-10 scale."""

    return f"{float(rating):.1f} / 10" if rating is not None else ""
