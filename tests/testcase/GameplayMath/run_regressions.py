#!/usr/bin/env python3
"""Verify the production gameplay numeric path against immutable actual CLR2 captures."""

import argparse
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
MATH = SUITE.parent / "MathBaseline"
sys.path.insert(0, str(SUITE))
from compare_gameplay import compare as compare_frames

spec = importlib.util.spec_from_file_location(
    "historical_math_compare", MATH / "compare.py"
)
if spec is None or spec.loader is None:
    raise ImportError("Historical raw-bit comparator could not be loaded")
raw_compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(raw_compare)


def run(*command):
    print("+ " + " ".join(map(str, command)), flush=True)
    subprocess.run(list(map(str, command)), cwd=ROOT, check=True)


def verify_fixtures(directory):
    manifest = json.loads((directory / "baseline.json").read_text())
    for name, identity in manifest["files"].items():
        data = gzip.decompress((directory / (name + ".gz")).read_bytes())
        if (
            hashlib.sha256(data).hexdigest() != identity["sha256"]
            or len(data) != identity["bytes"]
        ):
            raise ValueError("Historical capture identity differs: " + name)
    if directory == MATH:
        identities = {"inputs.bin": manifest["input"]["sha256"]}
    else:
        identities = {
            "inputs.bin": manifest["inputs_sha256"],
            "conversion-inputs.bin": manifest["conversion_inputs_sha256"],
            "GameplayProbe.cs": manifest["probe_sha256"],
            "ScriptProbe.cs": manifest["script_probe_sha256"],
            "ConversionProbe.cs": manifest["conversion_probe_sha256"],
            "RandomEdgeProbe.cs": manifest["random_edge_probe_sha256"],
            "Capture.cs": manifest["capture_source_sha256"],
            "FormattingProbe.cs": manifest["formatting_probe_sha256"],
            "formatting-inputs.bin": manifest["formatting_inputs_sha256"],
            "ParseProbe.cs": manifest["parsing_probe_sha256"],
            "parse-inputs.b64": manifest["parsing_inputs_sha256"],
            "TextCapture.cs": manifest["text_capture_sha256"],
            "parse-corpus.json": manifest["parse_corpus_sha256"],
            "MathEdgeProbe.cs": manifest["math_edge_probe_sha256"],
        }
    for name, expected in identities.items():
        if hashlib.sha256((directory / name).read_bytes()).hexdigest() != expected:
            raise ValueError("Historical source/input identity differs: " + name)
    return manifest


def require_values(expected, actual):
    result = raw_compare.compare(expected, actual)
    if result["mismatched_rows"]:
        print(json.dumps(result, indent=2))
        raise AssertionError(
            "Production numeric output differs from actual CLR2 capture"
        )
    return result["rows"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--framework",
        choices=("net8.0", "net10.0"),
        default=os.environ.get("THSSS_FRAMEWORK", "net8.0"),
    )
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    args = parser.parse_args()
    if args.framework not in ("net8.0", "net10.0"):
        raise ValueError("Unsupported framework")
    verify_fixtures(MATH)
    verify_fixtures(SUITE)
    run(
        "dotnet",
        "build",
        ROOT / "src/THSSS.csproj",
        "-c",
        args.configuration,
        "-f",
        args.framework,
    )
    projects = {
        "ActualProbe": MATH,
        "RandomProbe": MATH,
        "GameplayProbe": SUITE,
        "ScriptProbe": SUITE,
        "RandomEdgeProbe": SUITE,
        "PrecisionProbeHost": SUITE,
        "RuntimeChecks": SUITE,
        "FormattingChecks": SUITE,
        "ParsingChecks": SUITE,
    }
    dlls = {}
    for project, directory in projects.items():
        run(
            "dotnet",
            "build",
            directory / (project + ".csproj"),
            "-c",
            args.configuration,
            "-p:TargetFramework=" + args.framework,
        )
        dlls[project] = (
            directory
            / "bin"
            / project
            / args.configuration
            / args.framework
            / (project + ".dll")
        )
    game = ROOT / "src/bin" / args.configuration / args.framework / "THSSS.dll"
    kernel = (
        "thsss_legacy_math.dll"
        if sys.platform == "win32"
        else "libthsss_legacy_math.dylib"
        if sys.platform == "darwin"
        else "libthsss_legacy_math.so"
    )
    source_kernel = game.parent / kernel
    if not source_kernel.is_file():
        raise FileNotFoundError(
            "Production build did not deliver its native numeric library: "
            + str(source_kernel)
        )
    shutil.copy2(source_kernel, dlls["RuntimeChecks"].parent / kernel)
    run("dotnet", dlls["RuntimeChecks"], SUITE)
    run("dotnet", dlls["FormattingChecks"], SUITE)
    run("dotnet", dlls["ParsingChecks"], SUITE)
    workspace = Path(tempfile.mkdtemp(prefix="thsss-gameplay-regressions-"))
    success = False
    checked_rows = 0
    try:
        for precision in (24, 53):
            suffix = "-d3d" if precision == 24 else ""
            checks = (
                (
                    "ActualProbe",
                    MATH / ("actual-windows-clr2" + suffix + "-x86.csv.gz"),
                    [game, workspace / f"actual-{precision}.csv", MATH / "inputs.bin"],
                ),
                (
                    "RandomProbe",
                    MATH / ("random-windows-clr2" + suffix + "-x86.csv.gz"),
                    [game, workspace / f"random-{precision}.csv"],
                ),
                (
                    "GameplayProbe",
                    SUITE / f"gameplay-windows-clr2-{precision}.csv.gz",
                    [
                        game,
                        workspace / f"gameplay-{precision}.csv",
                        SUITE / "inputs.bin",
                    ],
                ),
                (
                    "ScriptProbe",
                    SUITE / f"script-windows-clr2-{precision}.csv.gz",
                    [game, workspace / f"script-{precision}.csv", MATH / "inputs.bin"],
                ),
                (
                    "RandomEdgeProbe",
                    SUITE / f"random-edges-clr2-{precision}.csv.gz",
                    [game, workspace / f"random-edges-{precision}.csv"],
                ),
            )
            for probe, compressed, parameters in checks:
                run(
                    "dotnet",
                    dlls["PrecisionProbeHost"],
                    game,
                    precision,
                    dlls[probe],
                    *parameters,
                )
                actual = parameters[1]
                expected = workspace / compressed.name[:-3]
                expected.write_bytes(gzip.decompress(compressed.read_bytes()))
                if probe == "GameplayProbe":
                    result = compare_frames(expected, actual)
                    if result["different_rows"]:
                        print(json.dumps(result, indent=2))
                        raise AssertionError(
                            "Production gameplay state/decision differs"
                        )
                    rows = result["rows"]
                elif probe == "RandomEdgeProbe":
                    left, right = (
                        expected.read_text().splitlines(),
                        actual.read_text().splitlines(),
                    )
                    if len(left) != 2305 or left != right:
                        first = next(
                            (
                                (i, a, b)
                                for i, (a, b) in enumerate(zip(left, right))
                                if a != b
                            ),
                            None,
                        )
                        raise AssertionError(
                            f"Random state/error chain differs: rows={len(left)}/{len(right)} first={first}"
                        )
                    rows = len(left)
                else:
                    rows = require_values(expected, actual)
                checked_rows += rows
                print(
                    f"PRODUCTION_PARITY_PASS precision={precision} probe={probe} rows={rows}",
                    flush=True,
                )
        manifest_path = (
            ROOT / "src/obj" / args.configuration / args.framework / "GameplayMath.manifest.json"
        )
        manifest = json.loads(manifest_path.read_text())
        if (
            manifest["SignatureOnlyRuntimeAdapter"]
            or manifest["Errors"]
            or manifest["MissingRoots"]
        ):
            raise AssertionError(
                "Production weave has missing roots, placeholder runtime or unsupported operations"
            )
        if not any(row["Action"] == "rewritten" for row in manifest["Methods"]):
            raise AssertionError("Production gameplay was not woven")
        if any(
            row["RandomBefore"] != row["RandomAfter"] for row in manifest["Methods"]
        ):
            raise AssertionError("Weave changed RNG consumption order")
        print(
            f"GAMEPLAY_MATH_REGRESSIONS_PASS configuration={args.configuration} production_rows={checked_rows}; both_precisions; rendering_excluded; rng_order_preserved"
        )
        success = True
    finally:
        if success:
            shutil.rmtree(workspace)
        else:
            print("Failure artifacts retained at " + str(workspace), file=sys.stderr)


if __name__ == "__main__":
    main()
