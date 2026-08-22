from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from threading import Barrier, Thread

from dropsort.posters import PosterAsset, PosterAssetCache, PosterAssetService, PosterRequest
from dropsort.posters.errors import PosterUnavailableError


@dataclass
class FakeSource:
    asset: PosterAsset | None
    error: Exception | None = None
    calls: int = 0

    def fetch(self, request: PosterRequest) -> PosterAsset:
        self.calls += 1
        if self.error:
            raise self.error
        assert self.asset is not None
        return self.asset


def test_cache_miss_fetches_then_hit_avoids_source(tmp_path: Path, png_bytes: bytes) -> None:
    source = FakeSource(PosterAsset("png", png_bytes))
    service = PosterAssetService(PosterAssetCache(tmp_path / "cache"), {"tmdb": source})
    request = PosterRequest("tmdb", "/poster.png")

    assert service.load_poster(request) == PosterAsset("png", png_bytes)
    assert service.load_poster(request) == PosterAsset("png", png_bytes)
    assert source.calls == 1


def test_offline_returns_cached_asset_or_none(tmp_path: Path, png_bytes: bytes) -> None:
    cache = PosterAssetCache(tmp_path / "cache")
    cached = PosterRequest("tmdb", "/cached.png")
    missing = PosterRequest("tmdb", "/missing.png")
    cache.put(cached, PosterAsset("png", png_bytes))
    source = FakeSource(None, PosterUnavailableError("offline"))
    service = PosterAssetService(cache, {"tmdb": source})

    assert service.load_poster(cached) == PosterAsset("png", png_bytes)
    assert service.load_poster(missing) is None


def test_unknown_provider_degrades_to_none(tmp_path: Path) -> None:
    service = PosterAssetService(PosterAssetCache(tmp_path / "cache"), {})

    assert service.load_poster(PosterRequest("other", "/poster.jpg")) is None


def test_concurrent_duplicate_requests_are_coalesced_at_service_boundary(
    tmp_path: Path,
    png_bytes: bytes,
) -> None:
    barrier = Barrier(2)

    class BlockingSource(FakeSource):
        def fetch(self, request: PosterRequest) -> PosterAsset:
            self.calls += 1
            barrier.wait()
            assert self.asset is not None
            return self.asset

    source = BlockingSource(PosterAsset("png", png_bytes))
    service = PosterAssetService(PosterAssetCache(tmp_path / "cache"), {"tmdb": source})
    request = PosterRequest("tmdb", "/poster.png")
    results: list[PosterAsset | None] = []
    first = Thread(target=lambda: results.append(service.load_poster(request)))
    first.start()
    barrier.wait()
    second = Thread(target=lambda: results.append(service.load_poster(request)))
    second.start()
    first.join()
    second.join()

    assert results == [PosterAsset("png", png_bytes), PosterAsset("png", png_bytes)]
    assert source.calls == 1
