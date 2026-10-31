#!/usr/bin/env python3
import argparse
import gzip
import hashlib
import itertools
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent
OLD_SHA = "424f131cad81065e1aace057374b4c4f50650d1047921a74f594aaa6435c259b"
BACKENDS = ("software", "x87")
PRECISIONS = (24, 53)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def exclusive(path, data):
    with path.open("xb") as stream:
        stream.write(data)


def build(configuration):
    subprocess.run(
        [
            "dotnet",
            "build",
            str(ROOT / "CollisionCompatibility.csproj"),
            "-c",
            configuration,
            "--nologo",
        ],
        check=True,
        timeout=120,
    )
    return ROOT / "bin" / configuration / "net8.0" / "CollisionCompatibility.dll"


def run_probe(probe, game, inputs, output, precision, backend):
    env = dict(os.environ, THSSS_MATH_BACKEND=backend)
    subprocess.run(
        [
            "dotnet",
            str(probe),
            str(game.resolve()),
            str(output),
            str(inputs),
            str(precision),
        ],
        check=True,
        env=env,
        timeout=120,
        cwd=ROOT,
    )
    return output.read_bytes()


def identity(game):
    directory = game.parent
    files = sorted(
        path
        for path in directory.rglob("*")
        if path.is_file() and path.suffix.lower() in (".dll", ".so", ".dylib", ".json")
    )
    return {str(path.relative_to(directory)): sha(path.read_bytes()) for path in files}


def first_difference(expected, actual):
    if expected == actual:
        return None
    for line, (old, new) in enumerate(
        itertools.zip_longest(expected.splitlines(), actual.splitlines()), 1
    ):
        if old != new:
            return f"line {line}: old={old!r}\nnew={new!r}"
    return "Byte difference in line endings or final newline"


def compare(expected, actual, label):
    difference = first_difference(expected, actual)
    if difference is not None:
        raise AssertionError(f"{label}: {difference}")


def selftest(expected):
    # Flip precisely one recorded distance bit, not a case name or metadata byte.
    marker = expected.index(b"\tdp=") + 4
    end = expected.index(b"\t", marker)
    distance = int(expected[marker:end], 16)
    mutated = (
        expected[:marker] + f"{distance ^ 1:016X}".encode("ascii") + expected[end:]
    )
    try:
        compare(mutated, expected, "intentional one-bit expected-output mutation")
    except AssertionError as error:
        print(f"STRICT_COMPARISON_SELFTEST=PASS {error}", flush=True)
        return
    raise AssertionError("Strict comparator accepted one changed expected distance bit")


def capture(args, probe):
    game = args.old.resolve()
    if sha(game.read_bytes()) != OLD_SHA:
        raise AssertionError(
            "Old assembly SHA256 is not the authorized preserved pre-optimization game"
        )
    old_identity = identity(game)
    args.baseline.mkdir(parents=True, exist_ok=False)
    inputs = args.baseline / "inputs.bin"
    subprocess.run(
        ["dotnet", str(probe), "generate", str(inputs)], check=True, timeout=30
    )
    outputs = {}
    with tempfile.TemporaryDirectory(prefix="collision-capture-") as temporary:
        for backend in args.backends:
            for precision in PRECISIONS:
                key = f"{backend}-{precision}"
                data = run_probe(
                    probe,
                    game,
                    inputs,
                    Path(temporary) / f"{key}.txt",
                    precision,
                    backend,
                )
                packed = gzip.compress(data, mtime=0)
                exclusive(args.baseline / f"{key}.txt.gz", packed)
                outputs[key] = {
                    "sha256": sha(data),
                    "gzip_sha256": sha(packed),
                    "cases": len(data.splitlines()) - 1,
                }
                selftest(data)
    if old_identity != identity(game):
        raise AssertionError(
            "Preserved baseline assembly/dependencies changed during capture"
        )
    manifest = {
        "format": "collision-v1",
        "oracle": "actual pre-optimization modern compat production DLL; not historical CLR2",
        "nan_policy": "exact raw binary64 bits including NaNs; no canonicalization",
        "old_game": str(game),
        "old_game_sha256": OLD_SHA,
        "old_files_sha256": old_identity,
        "inputs_sha256": sha(inputs.read_bytes()),
        "outputs": outputs,
    }
    exclusive(
        args.baseline / "manifest.json",
        (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode(),
    )
    print(
        json.dumps(
            {
                "baseline": str(args.baseline),
                "inputs_sha256": manifest["inputs_sha256"],
                "outputs": outputs,
            },
            sort_keys=True,
        ),
        flush=True,
    )


def verify(args, probe):
    manifest = json.loads((args.baseline / "manifest.json").read_text())
    if manifest["old_game_sha256"] != OLD_SHA or manifest["format"] != "collision-v1":
        raise AssertionError("Unrecognized collision baseline identity")
    inputs = args.baseline / "inputs.bin"
    if sha(inputs.read_bytes()) != manifest["inputs_sha256"]:
        raise AssertionError("Immutable input corpus changed")
    with tempfile.TemporaryDirectory(prefix="collision-verify-") as temporary:
        for backend in args.backends:
            for precision in PRECISIONS:
                key = f"{backend}-{precision}"
                output_identity = manifest["outputs"][key]
                packed = (args.baseline / f"{key}.txt.gz").read_bytes()
                expected = gzip.decompress(packed)
                if (
                    sha(packed) != output_identity["gzip_sha256"]
                    or sha(expected) != output_identity["sha256"]
                ):
                    raise AssertionError(f"Immutable old output changed: {key}")
                selftest(expected)
                actual = run_probe(
                    probe,
                    args.game,
                    inputs,
                    Path(temporary) / f"{key}.txt",
                    precision,
                    backend,
                )
                compare(expected, actual, key)
                print(
                    f"PASS {key} cases={output_identity['cases']} output_sha256={sha(actual)}",
                    flush=True,
                )


def main():
    parser = argparse.ArgumentParser(
        description="Actual BaseObject old/new differential probe; builds only this standalone fixture"
    )
    parser.add_argument("mode", choices=("capture", "verify"))
    parser.add_argument(
        "--old", type=Path, default=Path("/tmp/thsss-pre-collision-baseline/THSSS.dll")
    )
    parser.add_argument("--game", type=Path)
    parser.add_argument("--baseline", type=Path, default=ROOT / "baseline")
    parser.add_argument(
        "--backends", nargs="+", choices=BACKENDS, default=list(BACKENDS)
    )
    parser.add_argument(
        "--configuration", choices=("Debug", "Release"), default="Release"
    )
    args = parser.parse_args()
    args.baseline = args.baseline.resolve()
    if args.mode == "verify" and args.game is None:
        parser.error("verify requires --game path/to/THSSS.dll")
    if args.game is not None:
        args.game = args.game.resolve()
    probe = build(args.configuration)
    if args.mode == "capture":
        capture(args, probe)
    else:
        verify(args, probe)


if __name__ == "__main__":
    main()
