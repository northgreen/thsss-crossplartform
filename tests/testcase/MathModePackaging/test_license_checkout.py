"""Frozen package licenses must survive a Windows-style autocrlf checkout."""

from pathlib import Path
import shutil
import subprocess
import tempfile

from check_archive import check_skia_licenses

ROOT = Path(__file__).resolve().parents[3]


def git(directory, *arguments):
    subprocess.run(
        ["git", "-C", str(directory), *arguments],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=30,
    )


with tempfile.TemporaryDirectory(prefix="thsss-skia-license-checkout-") as temporary:
    base = Path(temporary)
    source, checkout = base / "source", base / "checkout"
    source.mkdir()
    shutil.copy2(ROOT / ".gitattributes", source / ".gitattributes")
    shutil.copytree(ROOT / "third_party/SkiaSharp", source / "third_party/SkiaSharp")
    git(source, "init", "-q")
    git(source, "config", "core.autocrlf", "true")
    git(source, "add", ".gitattributes", "third_party/SkiaSharp")
    git(
        source,
        "-c",
        "user.name=Fixture",
        "-c",
        "user.email=fixture@invalid",
        "commit",
        "-qm",
        "Frozen license fixture",
    )
    subprocess.run(
        [
            "git",
            "-c",
            "core.autocrlf=true",
            "clone",
            "--quiet",
            "--no-local",
            str(source),
            str(checkout),
        ],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=30,
    )
    package = base / "package"
    shutil.copytree(checkout / "third_party/SkiaSharp", package / "licenses/SkiaSharp")
    check_skia_licenses(package)
    license_file = next((package / "licenses/SkiaSharp").glob("*/LICENSE.txt"))
    license_file.write_bytes(license_file.read_bytes() + b"deliberate tampering\n")
    try:
        check_skia_licenses(package)
    except ValueError:
        print(
            "PASS Skia license Windows checkout: exact upstream bytes; tampering rejected"
        )
    else:
        raise AssertionError("License tampering was accepted")
