"""Check frozen vendor bytes under Windows Git checkout rules without building a game."""

from pathlib import Path
import importlib.util
import io
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[3]


def run(*args, cwd):
    return subprocess.run(args, cwd=cwd, check=True, capture_output=True)


with tempfile.TemporaryDirectory(prefix="thsss-windows-source-checkout-") as temporary:
    seed, checked = Path(temporary) / "seed", Path(temporary) / "checked"
    seed.mkdir()
    checked.mkdir()
    archive = run(
        "git", "archive", "HEAD", ".gitattributes", "native/LegacyMath", cwd=ROOT
    ).stdout
    with tarfile.open(fileobj=io.BytesIO(archive)) as contents:
        contents.extractall(seed, filter="data")
    # Proposed checkout rules must protect the immutable committed source blobs.
    (seed / ".gitattributes").write_bytes((ROOT / ".gitattributes").read_bytes())
    run("git", "init", "-q", cwd=seed)
    run("git", "config", "core.autocrlf", "false", cwd=seed)
    run("git", "add", ".gitattributes", "native/LegacyMath", cwd=seed)
    run(
        "git",
        "-c",
        "core.autocrlf=true",
        "checkout-index",
        "--all",
        "--force",
        "--prefix=" + str(checked) + "/",
        cwd=seed,
    )
    spec = importlib.util.spec_from_file_location(
        "checked_native_build", checked / "native/LegacyMath/build.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.sources()
    victim = checked / "native/LegacyMath/vendor/fpu/f128_67.cc"
    assert b"\r\n" not in victim.read_bytes()
    print("WINDOWS_AUTOCRLF_SOURCE_HASHES_PASS")
    victim.write_bytes(
        victim.read_bytes() + b"\n// intentional negative-control mutation\n"
    )
    try:
        module.sources()
    except module.BadSourceHash:
        print("SOURCE_TAMPER_REJECTION_PASS")
    else:
        raise AssertionError("Tampered vendor source was accepted")
