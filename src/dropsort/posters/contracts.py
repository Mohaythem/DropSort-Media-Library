from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol
from urllib.parse import urlsplit
import zlib

from dropsort.posters.errors import PosterResponseError


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
JPEG_SIGNATURE = b"\xff\xd8"


@dataclass(frozen=True, slots=True)
class PosterRequest:
    provider: str
    reference: str

    def __post_init__(self) -> None:
        if not isinstance(self.provider, str) or not self.provider.strip():
            raise ValueError("provider must be non-empty text")
        if not isinstance(self.reference, str) or not self.reference.strip():
            raise ValueError("poster reference must be non-empty text")
        reference = self.reference.strip()
        parsed = urlsplit(reference)
        segments = reference.replace("\\", "/").split("/")
        if (
            len(reference) > 1024
            or "\\" in reference
            or "\x00" in reference
            or any(ord(character) < 32 for character in reference)
            or ".." in segments
            or parsed.scheme
            or parsed.netloc
            or parsed.query
            or parsed.fragment
        ):
            raise ValueError("poster reference is unsafe")
        object.__setattr__(self, "provider", self.provider.strip().casefold())
        object.__setattr__(self, "reference", reference)


@dataclass(frozen=True, slots=True)
class PosterAsset:
    image_format: str
    content: bytes

    def __post_init__(self) -> None:
        normalized_format = self.image_format.casefold()
        if normalized_format not in {"jpeg", "png"}:
            raise ValueError("unsupported poster format")
        if not isinstance(self.content, bytes):
            raise ValueError("poster content must be bytes")
        detected = detect_image_format(self.content)
        if detected != normalized_format:
            raise PosterResponseError("poster image content is invalid")
        object.__setattr__(self, "image_format", normalized_format)


class PosterSource(Protocol):
    def fetch(self, request: PosterRequest) -> PosterAsset: ...


class PosterActions(Protocol):
    def load_poster(self, request: PosterRequest) -> PosterAsset | None: ...


def detect_image_format(content: bytes) -> str:
    if content.startswith(PNG_SIGNATURE) and _valid_png(content):
        return "png"
    if content.startswith(JPEG_SIGNATURE) and content.endswith(b"\xff\xd9"):
        return "jpeg"
    raise PosterResponseError("poster image content is invalid or truncated")


def _valid_png(content: bytes) -> bool:
    offset = len(PNG_SIGNATURE)
    first = True
    while offset + 12 <= len(content):
        length = int.from_bytes(content[offset : offset + 4], "big")
        chunk_end = offset + 12 + length
        if chunk_end > len(content):
            return False
        chunk_type = content[offset + 4 : offset + 8]
        data = content[offset + 8 : offset + 8 + length]
        expected_crc = int.from_bytes(content[offset + 8 + length : chunk_end], "big")
        if zlib.crc32(chunk_type + data) & 0xFFFFFFFF != expected_crc:
            return False
        if first and (chunk_type != b"IHDR" or length != 13):
            return False
        first = False
        offset = chunk_end
        if chunk_type == b"IEND":
            return length == 0 and offset == len(content)
    return False
