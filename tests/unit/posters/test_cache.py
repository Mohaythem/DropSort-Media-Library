from __future__ import annotations

from pathlib import Path
import os
import stat

import pytest

from dropsort.posters import PosterAsset, PosterAssetCache, PosterRequest, poster_cache_key
import dropsort.posters.cache as cache_module


def test_cache_key_is_deterministic_safe_and_provider_scoped() -> None:
    first = PosterRequest("tmdb", "/poster.jpg")
    same = PosterRequest("TMDB", "/poster.jpg")
    other = PosterRequest("other", "/poster.jpg")

    assert poster_cache_key(first) == poster_cache_key(same)
    assert poster_cache_key(first) != poster_cache_key(other)
    assert len(poster_cache_key(first)) == 64
    assert poster_cache_key(first).isalnum()


@pytest.mark.parametrize(
    "reference",
    ("", "../poster.jpg", "/../poster.jpg", r"\evil\poster.jpg", "https://evil.test/x.jpg", "a\x00b"),
)
def test_invalid_poster_references_are_rejected(reference: str) -> None:
    with pytest.raises(ValueError, match="reference"):
        PosterRequest("tmdb", reference)


def test_cache_miss_put_hit_and_hit_requires_no_source(tmp_path: Path, png_bytes: bytes) -> None:
    cache = PosterAssetCache(tmp_path / "posters", maximum_bytes=10_000)
    request = PosterRequest("tmdb", "/poster.png")

    assert cache.get(request) is None
    cache.put(request, PosterAsset("png", png_bytes))

    assert cache.get(request) == PosterAsset("png", png_bytes)
    assert len(tuple((tmp_path / "posters").glob("*.png"))) == 1


def test_corrupt_cache_entry_is_removed(tmp_path: Path, png_bytes: bytes) -> None:
    cache = PosterAssetCache(tmp_path / "posters")
    request = PosterRequest("tmdb", "/poster.png")
    path = cache.put(request, PosterAsset("png", png_bytes))
    path.write_bytes(b"corrupt")

    assert cache.get(request) is None
    assert not path.exists()


def test_partial_files_are_not_cache_hits_and_are_cleaned(tmp_path: Path) -> None:
    root = tmp_path / "posters"
    root.mkdir()
    partial = root / "abandoned.tmp"
    partial.write_bytes(b"partial")
    cache = PosterAssetCache(root)

    assert cache.get(PosterRequest("tmdb", "/poster.jpg")) is None
    assert not partial.exists()


def test_cache_evicts_least_recently_used_entries(tmp_path: Path, png_bytes: bytes) -> None:
    cache = PosterAssetCache(tmp_path / "posters", maximum_bytes=len(png_bytes) * 2)
    first = PosterRequest("tmdb", "/first.png")
    second = PosterRequest("tmdb", "/second.png")
    third = PosterRequest("tmdb", "/third.png")
    cache.put(first, PosterAsset("png", png_bytes))
    cache.put(second, PosterAsset("png", png_bytes))
    assert cache.get(first) is not None

    cache.put(third, PosterAsset("png", png_bytes))

    assert cache.get(first) is not None
    assert cache.get(second) is None
    assert cache.get(third) is not None


def test_cache_lru_order_is_stable_when_system_timestamps_tie(
    tmp_path: Path,
    png_bytes: bytes,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(cache_module.time, "time_ns", lambda: 1_000_000_000)
    cache = PosterAssetCache(tmp_path / "posters", maximum_bytes=len(png_bytes) * 2)
    first = PosterRequest("tmdb", "/first.png")
    second = PosterRequest("tmdb", "/second.png")
    third = PosterRequest("tmdb", "/third.png")
    cache.put(first, PosterAsset("png", png_bytes))
    cache.put(second, PosterAsset("png", png_bytes))
    assert cache.get(first) is not None

    cache.put(third, PosterAsset("png", png_bytes))

    assert cache.get(first) is not None
    assert cache.get(second) is None
    assert cache.get(third) is not None


def test_cache_root_symlink_is_rejected(tmp_path: Path) -> None:
    target = tmp_path / "target"
    target.mkdir()
    link = tmp_path / "link"
    try:
        link.symlink_to(target, target_is_directory=True)
    except OSError as error:
        pytest.skip(f"symlinks unavailable on this host: {error}")

    with pytest.raises(ValueError, match="cache root"):
        PosterAssetCache(link)


def test_cache_entry_that_is_reparse_like_is_removed_without_becoming_a_hit(
    tmp_path: Path,
    png_bytes: bytes,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cache = PosterAssetCache(tmp_path / "posters")
    request = PosterRequest("tmdb", "/poster.png")
    path = cache.put(request, PosterAsset("png", png_bytes))
    original = cache_module.os.lstat

    def reparse_like(target):
        info = original(target)
        if Path(target) == path:
            values = list(info)
            values[0] = stat.S_IFLNK
            return os.stat_result(values)
        return info

    monkeypatch.setattr(cache_module.os, "lstat", reparse_like)

    assert cache.get(request) is None
    assert not path.exists()


def test_clear_removes_only_regular_application_cache_files(
    tmp_path: Path,
    png_bytes: bytes,
) -> None:
    root = (tmp_path / "posters").absolute()
    cache = PosterAssetCache(root)
    cache.put(PosterRequest("tmdb", "/first.png"), PosterAsset("png", png_bytes))
    cache.put(PosterRequest("other", "/second.png"), PosterAsset("png", png_bytes))
    unrelated_directory = root / "nested"
    unrelated_directory.mkdir()
    preserved = unrelated_directory / "keep.txt"
    preserved.write_text("keep", encoding="utf-8")

    removed = cache.clear()

    assert removed == 2
    assert root.is_dir()
    assert preserved.read_text(encoding="utf-8") == "keep"


def test_clear_fails_closed_if_cache_root_becomes_unsafe(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    cache = PosterAssetCache((tmp_path / "posters").absolute())
    target = tmp_path / "outside.txt"
    target.write_text("do not delete", encoding="utf-8")
    monkeypatch.setattr(
        cache_module,
        "_validate_cache_root",
        lambda _root: (_ for _ in ()).throw(ValueError("unsafe root")),
    )

    with pytest.raises(ValueError, match="unsafe root"):
        cache.clear()

    assert target.read_text(encoding="utf-8") == "do not delete"
