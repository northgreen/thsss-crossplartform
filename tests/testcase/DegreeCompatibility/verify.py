import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

from cases import COUNT, corpus

ROOT = Path(__file__).resolve().parent
OLD = Path("/home/ictye/.cache/thsss-hard6-pre-degree-2026/THSSS.dll")
OLD_SHA = "a76424c2aaad24ed8e37456cfb8f4f1fcf8637d84ee2cac424e143da0afbeafe"
NATIVE = OLD.with_name("libthsss_legacy_math.so")
NATIVE_SHA = "2dfe1d9092e7e7077b58b808d1e553c968ed8a4e6330a6494af9988e56bea2b6"
DEPS = Path("/tmp/thsss-hard6-laser-coarse-candidate")
SOURCES = ["DegreeProbe.csproj", "Probe.cs", "cases.py", "verify.py"]
MODES = [(backend, pc) for backend in ["software", "x87"] for pc in [24, 53]]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require_equal(expected, actual, label):
    if expected != actual:
        raise ValueError(f"{label}: strict byte mismatch")


def baseline_identity():
    for path, expected in [(OLD, OLD_SHA), (NATIVE, NATIVE_SHA)]:
        require_equal(expected, sha(path.read_bytes()), f"Pinned baseline {path}")


def negative_check(expected):
    # Mutate one bit in one uncompressed output line; never write the frozen file.
    changed = bytearray(expected)
    position = expected.index(b"after:") + len(b"after:")
    changed[position] ^= 1
    try:
        require_equal(expected, bytes(changed), "Deliberate output-line bit mutation")
    except ValueError:
        return
    raise AssertionError("Mutated expected output was accepted")


def dependency_inventory(directory):
    return {
        str(path.resolve()): sha(path.read_bytes())
        for path in sorted(directory.iterdir())
        if path.is_file()
        and path.suffix in [".dll", ".json"]
        and not path.name.startswith("THSSS")
    }


def build(configuration):
    subprocess.run(
        [
            "dotnet",
            "build",
            str(ROOT / "DegreeProbe.csproj"),
            "-c",
            configuration,
            "--nologo",
        ],
        check=True,
        cwd=ROOT,
        timeout=120,
    )


def probe(dll, native, dependencies, configuration, backend, pc, inputs):
    with tempfile.TemporaryDirectory(prefix="degree-probe-") as temporary:
        package = Path(temporary)
        # Per-file symlinks avoid copying the game's large managed dependency set.
        for filename in dependencies:
            path = Path(filename)
            (package / path.name).symlink_to(path)
        (package / "THSSS.dll").symlink_to(dll)
        (package / "libthsss_legacy_math.so").symlink_to(native)
        output = package / "result.txt"
        env = dict(os.environ, THSSS_MATH_BACKEND=backend)
        subprocess.run(
            [
                "dotnet",
                str(ROOT / f"bin/{configuration}/net8.0/DegreeProbe.dll"),
                str(package / "THSSS.dll"),
                str(package / "libthsss_legacy_math.so"),
                backend,
                str(pc),
                str(inputs),
                str(output),
            ],
            check=True,
            cwd=package,
            env=env,
            timeout=120,
        )
        return output.read_bytes()


def capture(args):
    require_equal(OLD.resolve(), args.dll.resolve(), "Capture DLL path")
    require_equal(
        NATIVE.resolve(), args.native_library.resolve(), "Capture native path"
    )
    baseline_identity()
    dependencies = dependency_inventory(args.dependencies)
    if not dependencies:
        raise ValueError("Missing dependency resolver files")
    frozen = ROOT / "frozen"
    frozen.mkdir()  # Exclusive capture: never overwrite an existing oracle.
    inputs = corpus()
    with (frozen / "inputs.bin").open("xb") as stream:
        stream.write(inputs)
    manifest = {
        "oracle": "Frozen actual woven modern BaseObject.Ctrl; NOT a CLR2 oracle",
        "dll": str(OLD),
        "dll_sha256": OLD_SHA,
        "native_library": str(NATIVE),
        "native_sha256": NATIVE_SHA,
        "cases": COUNT,
        "dependencies": dependencies,
        "sources": {name: sha((ROOT / name).read_bytes()) for name in SOURCES},
        "inputs_sha256": sha(inputs),
        "outputs": {},
        "probe_sha256": {},
    }
    for configuration in ["Debug", "Release"]:
        build(configuration)
        manifest['probe_sha256'][configuration] = sha(
            (ROOT / f'bin/{configuration}/net8.0/DegreeProbe.dll').read_bytes())
        for backend, pc in MODES:
            name = f"{backend}-{pc}.txt.gz"
            data = probe(
                OLD,
                NATIVE,
                dependencies,
                configuration,
                backend,
                pc,
                frozen / "inputs.bin",
            )
            negative_check(data)
            if configuration == "Debug":
                packed = gzip.compress(data, mtime=0)
                with (frozen / name).open("xb") as stream:
                    stream.write(packed)
                manifest["outputs"][name] = {
                    "raw_sha256": sha(data),
                    "gzip_sha256": sha(packed),
                }
            else:
                require_equal(
                    gzip.decompress((frozen / name).read_bytes()),
                    data,
                    f"Debug/Release {backend} PC{pc}",
                )
            print(
                f"CAPTURE {configuration} {backend} PC{pc} cases={COUNT} SHA256={sha(data)}",
                flush=True,
            )
    baseline_identity()
    for path, expected in dependencies.items():
        require_equal(
            expected, sha(Path(path).read_bytes()), "Unchanged dependency " + path
        )
    with (frozen / "manifest.json").open("x") as stream:
        json.dump(manifest, stream, indent=2, sort_keys=True)
        stream.write("\n")


def verify(args):
    baseline_identity()
    frozen = ROOT / "frozen"
    manifest = json.loads((frozen / "manifest.json").read_text())
    require_equal(OLD_SHA, manifest["dll_sha256"], "Oracle DLL identity")
    require_equal(NATIVE_SHA, manifest["native_sha256"], "Oracle native identity")
    require_equal(COUNT, manifest['cases'], 'Frozen case count')
    require_equal(
        SOURCES,
        sorted(manifest["sources"], key=SOURCES.index),
        "Selected source inventory",
    )
    for name in SOURCES:
        require_equal(
            manifest["sources"][name],
            sha((ROOT / name).read_bytes()),
            "Frozen source " + name,
        )
    for path, expected in manifest["dependencies"].items():
        require_equal(
            expected,
            sha(Path(path).read_bytes()),
            "Preserved baseline dependency " + path,
        )
    inputs = (frozen / "inputs.bin").read_bytes()
    require_equal(manifest["inputs_sha256"], sha(inputs), "Frozen input SHA256")
    require_equal(inputs, corpus(), "Deterministic frozen input bits")
    expected_outputs = {}
    for backend, pc in MODES:
        name = f"{backend}-{pc}.txt.gz"
        packed = (frozen / name).read_bytes()
        require_equal(
            manifest["outputs"][name]["gzip_sha256"], sha(packed), "Frozen gzip SHA256"
        )
        expected = gzip.decompress(packed)
        require_equal(
            manifest["outputs"][name]["raw_sha256"], sha(expected), "Frozen raw SHA256"
        )
        negative_check(expected)
        expected_outputs[backend, pc] = expected
    for configuration in ["Debug", "Release"]:
        build(configuration)
        require_equal(manifest['probe_sha256'][configuration], sha(
            (ROOT / f'bin/{configuration}/net8.0/DegreeProbe.dll').read_bytes()), 'Frozen probe assembly')
        for backend, pc in MODES:
            actual = probe(
                args.dll.resolve(),
                args.native_library.resolve(),
                manifest["dependencies"],
                configuration,
                backend,
                pc,
                frozen / "inputs.bin",
            )
            require_equal(
                expected_outputs[backend, pc],
                actual,
                f"{configuration} {backend} PC{pc}",
            )
            print(
                f"PASS {configuration} {backend} PC{pc} cases={COUNT} SHA256={sha(actual)} mutation-negative=PASS",
                flush=True,
            )
    baseline_identity()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["capture", "verify"])
    parser.add_argument("--dll", type=Path, default=OLD)
    parser.add_argument("--native-library", type=Path)
    parser.add_argument("--dependencies", type=Path, default=DEPS)
    args = parser.parse_args()
    if args.native_library is None:
        args.native_library = args.dll.with_name("libthsss_legacy_math.so")
    if args.action == "capture":
        capture(args)
    else:
        verify(args)


if __name__ == "__main__":
    main()
