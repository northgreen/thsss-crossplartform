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
CONFIGS = ("Debug", "Release")
COMBOS = tuple(itertools.product(("software", "x87"), (24, 53)))
FIELDS = (
    "g",
    "s",
    "so",
    "move",
    "o",
    "p",
    "v",
    "d",
    "a",
    "t",
    "dest",
    "virtual",
    "calls",
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def exclusive(path, data):
    with path.open("xb") as stream:
        stream.write(data)


def sources():
    return {
        p.name: sha(p.read_bytes())
        for p in sorted(ROOT.iterdir())
        if p.suffix in (".cs", ".csproj", ".py")
    }


def identity(game):
    return {
        str(p.relative_to(game.parent)): sha(p.read_bytes())
        for p in sorted(game.parent.rglob("*"))
        if p.is_file() and p.suffix.lower() in (".dll", ".so", ".dylib", ".json")
    }


def build(config):
    subprocess.run(
        [
            "dotnet",
            "build",
            str(ROOT / "MotionCompatibility.csproj"),
            "-c",
            config,
            "--nologo",
        ],
        check=True,
        timeout=120,
    )
    return ROOT / "bin" / config / "net8.0" / "MotionCompatibility.dll"


def run(probe, game, inputs, output, backend, precision):
    subprocess.run(
        ["dotnet", str(probe), str(game), str(output), str(inputs), str(precision)],
        env=dict(os.environ, THSSS_MATH_BACKEND=backend),
        check=True,
        timeout=120,
        cwd=ROOT,
    )
    return output.read_bytes()


def validate(data, precision, keys=None):
    if not data.endswith(b"\n"):
        raise AssertionError("Output missing final newline")
    lines = data.decode("ascii").splitlines()
    count = len(lines) - 1
    if not 5000 <= count <= 10000:
        raise AssertionError(f"Invalid output count: {count}")
    if (
        lines[0]
        != f"motion-v1 precision={precision} count={count} nan=raw-bits runtime=initialized"
    ):
        raise AssertionError("Invalid output header")
    actual_keys = []
    for line in lines[1:]:
        parts = line.split("\t")
        actual_keys.append(parts[0])
        if (
            len(parts) != len(FIELDS) + 1
            or tuple(p.split("=", 1)[0] for p in parts[1:]) != FIELDS
        ):
            raise AssertionError(f"Invalid output schema: {parts[0]}")
    if len(set(actual_keys)) != count or (keys is not None and actual_keys != keys):
        raise AssertionError("Output case keys missing, duplicated or reordered")
    return actual_keys


def compare(expected, actual, label):
    if expected == actual:
        return
    for number, (old, new) in enumerate(
        itertools.zip_longest(expected.splitlines(), actual.splitlines()), 1
    ):
        if old != new:
            raise AssertionError(f"{label} line {number}: old={old!r} new={new!r}")
    raise AssertionError(f"{label}: raw byte/line ending difference")


def selftest(data):
    marker = data.index(b"\to=") + 3
    mutated = (
        data[:marker]
        + f"{int(data[marker : marker + 8], 16) ^ 1:08X}".encode()
        + data[marker + 8 :]
    )
    try:
        compare(data, mutated, "intentional one-bit OriginalPosition mutation")
    except AssertionError:
        print("STRICT_ONE_BIT_MUTATION_REJECTION=PASS", flush=True)
        return
    raise AssertionError("Comparator accepted one changed float bit")


def capture(args):
    if sha(args.old.read_bytes()) != OLD_SHA:
        raise AssertionError("Unauthorized old assembly hash")
    old_files = identity(args.old)
    source_hashes = sources()
    probes = {c: build(c) for c in CONFIGS}
    args.baseline.mkdir(parents=True, exist_ok=False)
    inputs = args.baseline / "inputs.bin"
    subprocess.run(
        ["dotnet", str(probes["Release"]), "generate", str(inputs)],
        check=True,
        timeout=30,
    )
    outputs, keys = {}, None
    with tempfile.TemporaryDirectory(prefix="motion-capture-", dir=ROOT) as directory:
        for backend, precision in COMBOS:
            key = f"{backend}-{precision}"
            expected = None
            for config, probe in probes.items():
                data = run(
                    probe,
                    args.old,
                    inputs,
                    Path(directory) / f"{key}-{config}.txt",
                    backend,
                    precision,
                )
                keys = validate(data, precision, keys)
                if expected is not None:
                    compare(expected, data, f"{key} Debug/Release reproducibility")
                expected = data
            assert expected is not None and keys is not None
            selftest(expected)
            packed = gzip.compress(expected, mtime=0)
            exclusive(args.baseline / f"{key}.txt.gz", packed)
            outputs[key] = {
                "sha256": sha(expected),
                "gzip_sha256": sha(packed),
                "cases": len(keys),
            }
            print(
                f"CAPTURE {key} cases={len(keys)} Debug=Release sha256={sha(expected)}",
                flush=True,
            )
    if identity(args.old) != old_files or sources() != source_hashes:
        raise AssertionError(
            "Oracle dependencies or fixture sources changed during capture"
        )
    manifest = {
        "format": "motion-v1",
        "oracle": "actual pinned pre-optimization .NET8 DLL; not CLR2",
        "nan_policy": "exact raw IEEE bits, including sign/payload/quieting; no canonicalization",
        "runtime_precondition": "native Precision/Backend initialized; BaseObject constructor and warm Move executed before null-stage cases",
        "old_game": str(args.old),
        "old_game_sha256": OLD_SHA,
        "old_files_sha256": old_files,
        "source_sha256": source_hashes,
        "probe_sha256": {c: sha(p.read_bytes()) for c, p in probes.items()},
        "inputs_sha256": sha(inputs.read_bytes()),
        "case_keys": keys,
        "outputs": outputs,
    }
    exclusive(
        args.baseline / "manifest.json",
        (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode(),
    )


def verify(args):
    manifest = json.loads((args.baseline / "manifest.json").read_text())
    if manifest["format"] != "motion-v1" or manifest["old_game_sha256"] != OLD_SHA:
        raise AssertionError("Unrecognized motion baseline")
    if manifest["source_sha256"] != sources():
        raise AssertionError("Fixture source differs from capture manifest")
    inputs = args.baseline / "inputs.bin"
    if sha(inputs.read_bytes()) != manifest["inputs_sha256"]:
        raise AssertionError("Frozen inputs changed")
    probe = build(args.configuration)
    with tempfile.TemporaryDirectory(prefix="motion-verify-", dir=ROOT) as directory:
        for backend, precision in COMBOS:
            key = f"{backend}-{precision}"
            frozen = manifest["outputs"][key]
            packed = (args.baseline / f"{key}.txt.gz").read_bytes()
            expected = gzip.decompress(packed)
            if (
                sha(packed) != frozen["gzip_sha256"]
                or sha(expected) != frozen["sha256"]
            ):
                raise AssertionError(f"Frozen old outputs changed: {key}")
            keys = validate(expected, precision, manifest["case_keys"])
            if len(keys) != frozen["cases"]:
                raise AssertionError("Manifest case count mismatch")
            selftest(expected)
            actual = run(
                probe,
                args.game,
                inputs,
                Path(directory) / f"{key}.txt",
                backend,
                precision,
            )
            validate(actual, precision, keys)
            compare(expected, actual, key)
            print(
                f"PASS {key} config={args.configuration} cases={len(keys)} sha256={sha(actual)}",
                flush=True,
            )


def main():
    parser = argparse.ArgumentParser(
        description="Actual public Move/getter/setter old-DLL differential fixture; never builds game"
    )
    parser.add_argument("mode", choices=("capture", "verify"))
    parser.add_argument(
        "--old", type=Path, default=Path("/tmp/thsss-pre-collision-baseline/THSSS.dll")
    )
    parser.add_argument("--game", type=Path)
    parser.add_argument("--baseline", type=Path, default=ROOT / "baseline")
    parser.add_argument("--configuration", choices=CONFIGS, default="Release")
    args = parser.parse_args()
    args.old, args.baseline = args.old.resolve(), args.baseline.resolve()
    if args.mode == "verify":
        if args.game is None:
            parser.error("verify requires --game path/to/THSSS.dll")
        args.game = args.game.resolve()
        verify(args)
    else:
        capture(args)


if __name__ == "__main__":
    main()
