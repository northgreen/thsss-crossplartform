"""Check the published numeric identity and mode-specific package contents."""

import hashlib
import json
from pathlib import Path
import sys


def check_archive(package: Path, mode: str, rid: str) -> None:
    if mode not in ("auto", "software", "off"):
        raise ValueError(f"invalid math mode: {mode!r}")
    record = json.loads(
        (package / "THSSS.math-mode.json").read_text(encoding="utf-8-sig")
    )
    if not isinstance(record, dict) or record.get("mode") != mode:
        raise ValueError("published math mode does not match requested mode")
    digest = record.get("sha256")
    if (
        not isinstance(digest, str)
        or len(digest) != 64
        or any(c not in "0123456789abcdefABCDEF" for c in digest)
    ):
        raise ValueError("invalid math mode SHA-256 record")
    if (
        digest.lower()
        != hashlib.sha256((package / "THSSS.dll").read_bytes()).hexdigest()
    ):
        raise ValueError("math mode SHA-256 mismatch")
    libraries = {
        "win": "thsss_legacy_math.dll",
        "linux": "libthsss_legacy_math.so",
        "osx": "libthsss_legacy_math.dylib",
    }
    source = package / "licenses/LegacyMath/source"
    if mode == "off":
        for name in (
            *libraries.values(),
            "GameplayMath.manifest.json",
            "licenses/LegacyMath/source",
        ):
            if (package / name).exists() or (package / name).is_symlink():
                raise ValueError(f"disabled math artifact remains: {name}")
        return
    for path in (
        package / libraries[rid.split("-")[0]],
        *(source / name for name in ("NOTICE.md", "build.py", "software_x87.cpp")),
    ):
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"missing numeric library/source: {path}")


if __name__ == "__main__":
    check_archive(Path(sys.argv[1]), sys.argv[2], sys.argv[3])
