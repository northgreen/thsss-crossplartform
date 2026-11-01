import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

from cases import corpus

ROOT = Path(__file__).resolve().parent
OLD = Path("/tmp/thsss-production-softopt-auto-audit/THSSS.dll")
OLD_SHA = "6df271ec740cec0761d371b1651da8e5e58f493f97c85476b4b3fb68c1dfaa2e"
SOURCES = ["LaserProbe.csproj", "Probe.cs", "cases.py", "verify.py"]
MODES = [(backend, pc) for backend in ["software", "x87"] for pc in [24, 53]]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require_equal(expected, actual, label):
    if expected != actual:
        raise ValueError(f"{label}: strict byte mismatch")


def negative_check(data):
    changed = bytearray(data)
    changed[len(changed) // 2] ^= 1
    try:
        require_equal(data, bytes(changed), "deliberate single-bit negative")
    except ValueError:
        return
    raise AssertionError("Single-bit corruption was accepted")


def inventory(dll):
    files = [
        *dll.parent.glob("*.dll"),
        *dll.parent.glob("*.json"),
        *dll.parent.glob("*.so"),
    ]
    return {str(path.resolve()): sha(path.read_bytes()) for path in sorted(files)}


def build():
    subprocess.run(
        [
            "dotnet",
            "build",
            str(ROOT / "LaserProbe.csproj"),
            "-c",
            "Release",
            "--nologo",
        ],
        check=True,
        cwd=ROOT,
    )


def probe(dll, backend, pc, inputs):
    with tempfile.TemporaryDirectory(prefix="laser-probe-") as temporary:
        output = Path(temporary) / "result.bin"
        env = dict(os.environ, THSSS_MATH_BACKEND=backend)
        subprocess.run(
            [
                "dotnet",
                str(ROOT / "bin/Release/net8.0/LaserProbe.dll"),
                str(dll),
                backend,
                str(pc),
                str(inputs),
                str(output),
            ],
            check=True,
            env=env,
            cwd=ROOT,
        )
        return output.read_bytes()


def capture():
    require_equal(OLD_SHA, sha(OLD.read_bytes()), "Pinned old production DLL")
    frozen = ROOT / "frozen"
    # Exclusive directory and files: capture can never overwrite an existing oracle.
    frozen.mkdir()
    inputs = corpus()
    with (frozen / "inputs.bin").open("xb") as stream:
        stream.write(inputs)
    manifest = {
        "oracle": "Frozen modern production baseline; not a CLR2 oracle",
        "dll": str(OLD),
        "dll_sha256": OLD_SHA,
        "cases": 5000,
        "runtime": inventory(OLD),
        "sources": {name: sha((ROOT / name).read_bytes()) for name in SOURCES},
        "inputs_sha256": sha(inputs),
        "outputs": {},
    }
    for backend, pc in MODES:
        name = f"{backend}-{pc}.bin.gz"
        data = probe(OLD, backend, pc, frozen / "inputs.bin")
        packed = gzip.compress(data, mtime=0)
        with (frozen / name).open("xb") as stream:
            stream.write(packed)
        manifest["outputs"][name] = {
            "raw_sha256": sha(data),
            "gzip_sha256": sha(packed),
        }
        print(f"CAPTURE {backend} PC{pc} cases=5000 SHA256={sha(data)}")
    with (frozen / "manifest.json").open("x") as stream:
        json.dump(manifest, stream, indent=2, sort_keys=True)
        stream.write("\n")


def verify(dll):
    frozen = ROOT / "frozen"
    manifest = json.loads((frozen / "manifest.json").read_text())
    require_equal(OLD_SHA, manifest["dll_sha256"], "Pinned oracle identity")
    for name, expected in manifest["sources"].items():
        require_equal(
            expected, sha((ROOT / name).read_bytes()), "Frozen source " + name
        )
    for name, expected in manifest["runtime"].items():
        require_equal(
            expected,
            sha(Path(name).read_bytes()),
            "Preserved baseline dependency " + name,
        )
    inputs = (frozen / "inputs.bin").read_bytes()
    require_equal(manifest["inputs_sha256"], sha(inputs), "Frozen inputs hash")
    require_equal(inputs, corpus(), "Deterministic corpus")
    negative_check(inputs)
    for backend, pc in MODES:
        name = f"{backend}-{pc}.bin.gz"
        packed = (frozen / name).read_bytes()
        require_equal(
            manifest["outputs"][name]["gzip_sha256"], sha(packed), "Frozen gzip hash"
        )
        expected = gzip.decompress(packed)
        require_equal(
            manifest["outputs"][name]["raw_sha256"], sha(expected), "Frozen raw hash"
        )
        negative_check(expected)
        actual = probe(dll, backend, pc, frozen / "inputs.bin")
        require_equal(expected, actual, f"{backend} PC{pc}")
        print(
            f"PASS {backend} PC{pc} cases=5000 SHA256={sha(actual)} single-bit-negative=PASS"
        )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["capture", "verify"])
    parser.add_argument("--dll", type=Path, default=OLD)
    args = parser.parse_args()
    if args.action == "capture" and args.dll.resolve() != OLD:
        parser.error("Capture only accepts the pinned old production DLL")
    build()
    if args.action == "capture":
        capture()
    else:
        verify(args.dll.resolve())


if __name__ == "__main__":
    main()
