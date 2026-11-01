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
OLD = Path("/home/ictye/.cache/thsss-hard6-pre-acceleration-2026/THSSS.dll")
NATIVE = OLD.with_name("libthsss_legacy_math.so")
IDENTITIES = OLD.with_name("identities.json")
DEPS = Path("/home/ictye/.cache/thsss-degree-candidate-2026")
PINNED = {
    "THSSS.dll": "32096e8326f89cb8bc003a8c2001dc1acc2bc34af802a8dd8ec0fdd45eff8740",
    "libthsss_legacy_math.so": "7e47dc54c0126250c13d73a9867aa72d7997c886522dcecfd7c3114006555776",
}
# Deliberately explicit: NativeChecks belongs to a separate agent and identity domain.
SOURCES = ["AccelerationProbe.csproj", "Probe.cs", "cases.py", "verify.py"]
MODES = [(backend, pc) for backend in ["software", "x87"] for pc in [24, 53]]
CONFIGURATIONS = ["Debug", "Release"]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def require_equal(expected, actual, label):
    if expected != actual:
        raise ValueError(f"{label}: strict byte mismatch")


def baseline_identity():
    identities = json.loads(IDENTITIES.read_bytes())
    require_equal(set(PINNED), set(identities), "Pinned identities inventory")
    for name, expected in PINNED.items():
        data = OLD.with_name(name).read_bytes()
        require_equal(expected, identities[name]["sha256"], "Identity source " + name)
        require_equal(expected, sha(data), "Pinned baseline " + name)
        require_equal(identities[name]["bytes"], len(data), "Baseline size " + name)
    return sha(IDENTITIES.read_bytes())


def negative_check(expected):
    # A one-bit raw-state mutation must fail the exact same comparator as a candidate.
    changed = bytearray(expected)
    position = expected.index(b"after:") + len(b"after:")
    changed[position] ^= 1
    try:
        require_equal(expected, bytes(changed), "Deliberate output bit mutation")
    except ValueError:
        return
    raise AssertionError("Mutated output was accepted")


def source_inventory():
    return {name: sha((ROOT / name).read_bytes()) for name in SOURCES}


def dependency_inventory(directory):
    inventory = {
        str(path.resolve()): sha(path.read_bytes())
        for path in sorted(directory.iterdir())
        if path.is_file()
        and path.suffix in [".dll", ".json"]
        and not path.name.startswith("THSSS")
    }
    if not inventory:
        raise ValueError("Missing dependency resolver files")
    names = [Path(path).name for path in inventory]
    require_equal(len(names), len(set(names)), "Unique dependency link names")
    return inventory


def check_dependencies(dependencies):
    for path, expected in dependencies.items():
        require_equal(
            expected, sha(Path(path).read_bytes()), "Preserved dependency " + path
        )


def probe_path(configuration):
    return ROOT / f"bin/{configuration}/net8.0/AccelerationProbe.dll"


def build(configuration):
    subprocess.run(
        [
            "dotnet",
            "build",
            str(ROOT / "AccelerationProbe.csproj"),
            "-c",
            configuration,
            "--nologo",
        ],
        cwd=ROOT,
        check=True,
        timeout=120,
    )
    return sha(probe_path(configuration).read_bytes())


def probe(dll, native, dependencies, configuration, backend, pc, inputs):
    # Only this invocation's temporary symlinks/results are removed; no player assets
    # or existing candidate/baseline dependency directories are copied or deleted.
    with tempfile.TemporaryDirectory(
        prefix="acceleration-probe-", dir=ROOT
    ) as temporary:
        package = Path(temporary)
        for filename in dependencies:
            path = Path(filename)
            (package / path.name).symlink_to(path)
        (package / "THSSS.dll").symlink_to(dll)
        (package / "libthsss_legacy_math.so").symlink_to(native)
        output = package / "result.txt"
        subprocess.run(
            [
                "dotnet",
                str(probe_path(configuration)),
                str(package / "THSSS.dll"),
                str(package / "libthsss_legacy_math.so"),
                backend,
                str(pc),
                str(inputs),
                str(output),
            ],
            cwd=package,
            env=dict(os.environ, THSSS_MATH_BACKEND=backend),
            check=True,
            timeout=240,
        )
        return output.read_bytes()


def capture(args):
    require_equal(OLD.resolve(), args.dll.resolve(), "Capture DLL path")
    require_equal(
        NATIVE.resolve(), args.native_library.resolve(), "Capture native path"
    )
    identities_sha = baseline_identity()
    dependencies = dependency_inventory(args.dependencies)
    sources = source_inventory()
    frozen = ROOT / "frozen"
    frozen.mkdir()  # Exclusive creation; incomplete capture is retained for inspection.
    inputs = corpus()
    with (frozen / "inputs.bin").open("xb") as stream:
        stream.write(inputs)
    manifest = {
        "oracle": "Actual frozen modern woven BaseObject_CS.Ctrl; NOT a CLR2 oracle",
        "dll": str(OLD),
        "dll_sha256": PINNED[OLD.name],
        "native_library": str(NATIVE),
        "native_sha256": PINNED[NATIVE.name],
        "identities_json": str(IDENTITIES),
        "identities_sha256": identities_sha,
        "cases": COUNT,
        "prefix_only_cases": 32,
        "dependencies": dependencies,
        "sources": sources,
        "inputs_sha256": sha(inputs),
        "outputs": {},
        "probe_sha256": {},
    }
    for configuration in CONFIGURATIONS:
        manifest["probe_sha256"][configuration] = build(configuration)
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
                f"CAPTURE {configuration} {backend} PC{pc} cases={COUNT} "
                f"SHA256={sha(data)} mutation-negative=PASS",
                flush=True,
            )
    require_equal(identities_sha, baseline_identity(), "Unchanged identities")
    require_equal(sources, source_inventory(), "Unchanged probe sources")
    check_dependencies(dependencies)
    with (frozen / "manifest.json").open("x") as stream:
        json.dump(manifest, stream, indent=2, sort_keys=True)
        stream.write("\n")


def frozen_outputs(manifest):
    frozen = ROOT / "frozen"
    require_equal(PINNED[OLD.name], manifest["dll_sha256"], "Oracle DLL identity")
    require_equal(
        PINNED[NATIVE.name], manifest["native_sha256"], "Oracle native identity"
    )
    require_equal(
        baseline_identity(), manifest["identities_sha256"], "Identity source hash"
    )
    require_equal(COUNT, manifest["cases"], "Frozen case count")
    require_equal(
        source_inventory(), manifest["sources"], "Explicit frozen source inventory"
    )
    check_dependencies(manifest["dependencies"])
    inputs = (frozen / "inputs.bin").read_bytes()
    require_equal(manifest["inputs_sha256"], sha(inputs), "Frozen input SHA256")
    require_equal(inputs, corpus(), "Deterministic frozen input bits")
    outputs = {}
    for backend, pc in MODES:
        name = f"{backend}-{pc}.txt.gz"
        packed = (frozen / name).read_bytes()
        require_equal(b"\0\0\0\0", packed[4:8], "Deterministic gzip mtime")
        require_equal(
            manifest["outputs"][name]["gzip_sha256"], sha(packed), "Frozen gzip SHA256"
        )
        expected = gzip.decompress(packed)
        require_equal(
            manifest["outputs"][name]["raw_sha256"], sha(expected), "Frozen raw SHA256"
        )
        negative_check(expected)
        outputs[backend, pc] = expected
    return outputs


def verify(args):
    manifest = json.loads((ROOT / "frozen/manifest.json").read_text())
    expected = frozen_outputs(manifest)
    dll, native = args.dll.resolve(), args.native_library.resolve()
    identities = {str(path): sha(path.read_bytes()) for path in [dll, native]}
    print(
        "VERIFY actual binary SHA256: " + json.dumps(identities, sort_keys=True),
        flush=True,
    )
    for configuration in CONFIGURATIONS:
        require_equal(
            manifest["probe_sha256"][configuration],
            build(configuration),
            "Frozen actual probe assembly " + configuration,
        )
        for backend, pc in MODES:
            actual = probe(
                dll,
                native,
                manifest["dependencies"],
                configuration,
                backend,
                pc,
                ROOT / "frozen/inputs.bin",
            )
            require_equal(
                expected[backend, pc], actual, f"{configuration} {backend} PC{pc}"
            )
            print(
                f"PASS {configuration} {backend} PC{pc} cases={COUNT} "
                f"SHA256={sha(actual)} mutation-negative=PASS",
                flush=True,
            )
    require_equal(
        identities,
        {str(path): sha(path.read_bytes()) for path in [dll, native]},
        "Unchanged tested binaries",
    )
    frozen_outputs(manifest)


def main():
    parser = argparse.ArgumentParser(
        description="Modern old-method oracle, not CLR2; no GUI/assets required."
    )
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
