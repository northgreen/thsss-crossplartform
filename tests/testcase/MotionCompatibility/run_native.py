#!/usr/bin/env python3
"""Run the standalone native motion correctness gate against an existing library."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--library",
        type=Path,
        required=True,
        help="existing native library; never rebuilt or linked into probes",
    )
    parser.add_argument(
        "--benchmark",
        action="store_true",
        help="optional microbenchmark, not performance proof",
    )
    parser.add_argument(
        "--report", type=Path, help="write commands, fingerprints and results as JSON"
    )
    args = parser.parse_args()
    if sys.platform != "linux" or platform.machine().lower() not in {"x86_64", "amd64"}:
        parser.error(
            "unsupported platform: native motion/state gates require Linux x64"
        )
    library = args.library.resolve(strict=True)
    if not library.is_file():
        parser.error("--library must name an existing regular file")
    if args.report and args.report.resolve() == library:
        parser.error("--report must not overwrite the supplied library")
    fixtures = Path(__file__).resolve().parent
    report = {
        "library": str(library),
        "sha256_before": sha256(library),
        "seed": "d219edbeeffeed41",
        "cases_per_backend_precision": 100000,
        "benchmark": args.benchmark,
        "commands": [],
        "passed": False,
    }
    try:
        with tempfile.TemporaryDirectory(prefix="thsss-native-motion-") as temporary:
            for name in ("native_motion_probe", "native_motion_state"):
                executable = Path(temporary) / name
                command = [
                    os.environ.get("CXX", "clang++"),
                    "-std=c++17",
                    "-O2",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    "-fno-fast-math",
                    "-ffp-contract=off",
                    str(fixtures / (name + ".cpp")),
                    "-ldl",
                    "-o",
                    str(executable),
                ]
                print("COMPILE", " ".join(command), flush=True)
                report["commands"].append(command)
                subprocess.run(command, check=True, timeout=120)
                command = [str(executable), str(library)]
                if args.benchmark and name == "native_motion_probe":
                    command.append("--benchmark")
                report["commands"].append(command)
                result = subprocess.run(
                    command, text=True, capture_output=True, timeout=600
                )
                report[name] = {
                    "returncode": result.returncode,
                    "stdout": result.stdout,
                    "stderr": result.stderr,
                }
                print(result.stdout, end="", flush=True)
                print(result.stderr, end="", file=sys.stderr, flush=True)
                result.check_returncode()
        report["passed"] = True
    except (OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(f"FAIL: {error}", file=sys.stderr)
    finally:
        report["sha256_after"] = sha256(library)
        report["library_unchanged"] = report["sha256_before"] == report["sha256_after"]
        report["passed"] = report["passed"] and report["library_unchanged"]
        print(json.dumps(report, indent=2), flush=True)
        if args.report:
            args.report.write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
