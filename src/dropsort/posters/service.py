from __future__ import annotations

from collections.abc import Mapping
from contextlib import contextmanager
from threading import Lock

from dropsort.posters.cache import PosterAssetCache
from dropsort.posters.contracts import PosterAsset, PosterRequest, PosterSource
from dropsort.posters.errors import PosterError


class PosterAssetService:
    def __init__(
        self,
        cache: PosterAssetCache,
        sources: Mapping[str, PosterSource],
    ) -> None:
        self._cache = cache
        self._sources = {provider.casefold(): source for provider, source in sources.items()}
        self._locks_guard = Lock()
        self._request_locks: dict[PosterRequest, tuple[Lock, int]] = {}

    def load_poster(self, request: PosterRequest) -> PosterAsset | None:
        cached = self._cache.get(request)
        if cached is not None:
            return cached
        with self._locked_request(request):
            cached = self._cache.get(request)
            if cached is not None:
                return cached
            source = self._sources.get(request.provider)
            if source is None:
                return None
            try:
                asset = source.fetch(request)
                self._cache.put(request, asset)
            except (PosterError, OSError):
                return None
            return asset

    @contextmanager
    def _locked_request(self, request: PosterRequest):
        with self._locks_guard:
            lock, users = self._request_locks.get(request, (Lock(), 0))
            self._request_locks[request] = (lock, users + 1)
        try:
            with lock:
                yield
        finally:
            with self._locks_guard:
                current_lock, current_users = self._request_locks[request]
                if current_users == 1:
                    del self._request_locks[request]
                else:
                    self._request_locks[request] = (current_lock, current_users - 1)
