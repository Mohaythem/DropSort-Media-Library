from __future__ import annotations

import logging
from logging.handlers import RotatingFileHandler
from pathlib import Path
import re


_SECRET_PATTERNS = (
    re.compile(r"(?i)(authorization\s*[:=]\s*(?:bearer\s+)?)[^\s,;]+"),
    re.compile(r"(?i)(DROPSORT_TMDB_READ_ACCESS_TOKEN\s*[:=]\s*)[^\s,;]+"),
)


class _SecretRedactionFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        message = record.getMessage()
        for pattern in _SECRET_PATTERNS:
            message = pattern.sub(r"\1[REDACTED]", message)
        record.msg = message
        record.args = ()
        return True


def configure_runtime_logging(log_directory: Path) -> Path:
    log_directory.mkdir(parents=True, exist_ok=True)
    log_path = log_directory / "dropsort.log"
    handler = RotatingFileHandler(
        log_path,
        maxBytes=1_048_576,
        backupCount=3,
        encoding="utf-8",
    )
    handler.addFilter(_SecretRedactionFilter())
    handler.setFormatter(
        logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s")
    )
    root = logging.getLogger()
    if not any(
        isinstance(existing, RotatingFileHandler)
        and Path(existing.baseFilename) == log_path.absolute()
        for existing in root.handlers
    ):
        root.addHandler(handler)
    else:
        handler.close()
    root.setLevel(logging.INFO)
    return log_path
