# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Supplied-artifact degree gates; no kernel rebuild, GUI, replay, or benchmark."""

import argparse
import hashlib
import platform
import re
import subprocess
import tempfile
from pathlib import Path

from machine import machine_gate, require

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
ORACLE_SHA = "2dfe1d9092e7e7077b58b808d1e553c968ed8a4e6330a6494af9988e56bea2b6"
RIDS = ("linux-x64", "linux-arm64", "win-x64", "win-arm64", "osx-x64", "osx-arm64")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(command, log):
    result = subprocess.run(
        [str(arg) for arg in command],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    log.write_text(result.stdout)
    print(result.stdout, end="", flush=True)
    require(
        result.returncode == 0,
        f"command failed ({result.returncode}): {command}; log={log}",
    )


def exports(artifact, rid):
    names = set(
        re.findall(
            r"^SOFTWARE_API \w+ (\w+)\(",
            (ROOT / "native/LegacyMath/software_x87.h").read_text(),
            re.M,
        )
    )
    require(
        {"software_degree_increment", "hardware_degree_increment", "legacy_degree_increment"} <= names,
        "degree ABI declarations missing from current header",
    )
    expected = {n for n in names if "x64" in rid or not n.startswith("hardware_")}
    if rid.startswith("win"):
        text = subprocess.check_output(
            ["llvm-readobj", "--coff-exports", str(artifact)], text=True
        )
        actual = set(re.findall(r"^  Name: (\w+)$", text, re.M))
    else:
        command = ["llvm-nm", "--defined-only", "--extern-only"]
        if rid.startswith("linux"):
            command.append("--dynamic")
        text = subprocess.check_output(command + [str(artifact)], text=True)
        actual = {
            line.split()[-1].lstrip("_") for line in text.splitlines() if line.split()
        }
        actual = {
            n for n in actual if n.startswith(("software_", "legacy_", "hardware_"))
        }
    require(
        actual == expected,
        f"{rid}: missing={expected - actual}, extra={actual - expected}",
    )
    print(
        f"PASS {rid} exact header exports={len(actual)} SHA256={digest(artifact)}",
        flush=True,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--library", required=True, type=Path, help="Linux x64 candidate"
    )
    parser.add_argument(
        "--oracle",
        required=True,
        type=Path,
        help="Frozen pre-degree x64 primitive oracle",
    )
    parser.add_argument(
        "--artifacts",
        required=True,
        type=Path,
        help="Existing six-RID artifact directory",
    )
    parser.add_argument(
        "--output", type=Path, help="Logs and ephemeral compiled harnesses"
    )
    parser.add_argument("--sysroot", type=Path, default=Path("/usr/aarch64-linux-gnu"))
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() in {"x86_64", "amd64"},
        "requires Linux x64 host",
    )
    library, oracle, base = (
        args.library.resolve(strict=True),
        args.oracle.resolve(strict=True),
        args.artifacts.resolve(strict=True),
    )
    require(library != oracle, "candidate cannot be its own oracle")
    require(digest(oracle) == ORACLE_SHA, "frozen pre-degree oracle SHA mismatch")
    artifacts = {}
    for rid in RIDS:
        matches = [
            p.resolve()
            for p in (base / rid).iterdir()
            if p.suffix in {".so", ".dll", ".dylib"}
        ]
        require(len(matches) == 1, f"{rid}: expected one supplied library")
        artifacts[rid] = matches[0]
    require(
        artifacts["linux-x64"] == library,
        "--library must match supplied linux-x64 artifact",
    )
    paths = {oracle, *artifacts.values()}
    before = {p: digest(p) for p in paths}
    output = (
        args.output.resolve()
        if args.output
        else Path(tempfile.mkdtemp(prefix="degree-native-checks-2026-"))
    )
    output.mkdir(parents=True, exist_ok=True)
    print(f"OUTPUT {output}\nORACLE {oracle} SHA256={before[oracle]}", flush=True)
    try:
        for rid, artifact in artifacts.items():
            exports(artifact, rid)
        machine_gate(library, output / "degree-disassembly.txt")
        common = [
            "-std=c++17",
            "-O2",
            "-Wall",
            "-Wextra",
            "-Werror",
            "-fno-fast-math",
            str(HERE / "check.cpp"),
            "-ldl",
        ]
        execute(
            ["g++", *common, "-o", output / "degree-x64"], output / "compile-x64.log"
        )
        execute(
            [output / "degree-x64", library, oracle, output / "corpus.bin"],
            output / "degree-x64.log",
        )
        execute(
            ["aarch64-linux-gnu-g++", *common, "-o", output / "degree-arm64"],
            output / "compile-arm64.log",
        )
        execute(
            [
                "qemu-aarch64",
                "-L",
                args.sysroot.resolve(strict=True),
                output / "degree-arm64",
                artifacts["linux-arm64"],
                output / "corpus.bin",
            ],
            output / "degree-arm64.log",
        )
        print(
            f"PASS portable frozen expected corpus SHA256={digest(output / 'corpus.bin')}",
            flush=True,
        )
    finally:
        for path, expected in before.items():
            require(digest(path) == expected, f"artifact changed during gate: {path}")
        print("PASS supplied library/oracle SHA256 unchanged pre/post", flush=True)
    print(
        "PASS native correctness only; ARM64 is QEMU software/dispatch, NOT x87 hardware or FPS evidence",
        flush=True,
    )


if __name__ == "__main__":
    main()
