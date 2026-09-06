#!/usr/bin/env python3
from __future__ import annotations

import os
import re
import sys
import tempfile
import zipfile
from pathlib import Path

REPLACEMENTS = [
    (re.compile(r'("(?:accessToken|refreshToken|password)"\s*:\s*")[^"]*(")', re.IGNORECASE), r'\1[REDACTED]\2'),
    (re.compile(r'((?:authorization|set-cookie|cookie)\s*[:=]\s*)[^\r\n]+', re.IGNORECASE), r'\1[REDACTED]'),
    (re.compile(r'(md_(?:access|refresh)=)[^;\s"\\]+', re.IGNORECASE), r'\1[REDACTED]'),
    (re.compile(r'(Bearer\s+)[A-Za-z0-9._~+\-/]+=*', re.IGNORECASE), r'\1[REDACTED]'),
    (re.compile(r'eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'), '[REDACTED-JWT]'),
]

PASSWORD = os.environ.get('E2E_PASSWORD', 'macro-deck-e2e-password')


def redact_text(text: str) -> str:
    if PASSWORD:
        text = text.replace(PASSWORD, '[REDACTED-PASSWORD]')
    for pattern, replacement in REPLACEMENTS:
        text = pattern.sub(replacement, text)
    return text


def redact_bytes(data: bytes) -> bytes:
    if b'\x00' in data:
        return data
    try:
        text = data.decode('utf-8')
    except UnicodeDecodeError:
        return data
    return redact_text(text).encode('utf-8')


def rewrite_zip(path: Path) -> None:
    with tempfile.NamedTemporaryFile(prefix=path.name, suffix='.tmp', dir=path.parent, delete=False) as tmp:
        temp_path = Path(tmp.name)
    try:
        with zipfile.ZipFile(path, 'r') as source, zipfile.ZipFile(temp_path, 'w') as target:
            for info in source.infolist():
                target.writestr(info, redact_bytes(source.read(info.filename)))
        temp_path.replace(path)
    finally:
        temp_path.unlink(missing_ok=True)


def redact_path(path: Path) -> None:
    if not path.exists():
        return
    files = [path] if path.is_file() else [item for item in path.rglob('*') if item.is_file()]
    for file in files:
        if file.suffix.lower() == '.zip':
            rewrite_zip(file)
            continue
        data = file.read_bytes()
        redacted = redact_bytes(data)
        if redacted != data:
            file.write_bytes(redacted)


def main() -> int:
    if len(sys.argv) < 2:
        print('usage: redact-playwright-artifacts.py <path> [<path> ...]', file=sys.stderr)
        return 2
    for raw_path in sys.argv[1:]:
        redact_path(Path(raw_path))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
