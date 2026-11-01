# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Supplied six-RID acceleration gates: independent raw bits, state and machine code."""

import argparse
import hashlib
import importlib.util
import json
import platform
import re
import subprocess
import tempfile
from pathlib import Path
from machine import machine_gate, require

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
ORACLE_SHA = "7e47dc54c0126250c13d73a9867aa72d7997c886522dcecfd7c3114006555776"
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
    require(len(names) == 69, "expected 69 current header declarations")
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
    require(len(actual) == (69 if "x64" in rid else 50), f"{rid}: export count")
    print(
        f"PASS {rid} exact header exports={len(actual)} SHA256={digest(artifact)}",
        flush=True,
    )


def degree_gate(library, output):
    # Reuse unchanged degree machine checker against candidate and frozen predecessor.
    spec = importlib.util.spec_from_file_location(
        "degree_machine",
        HERE.parent.parent / "DegreeCompatibility/NativeChecks/machine.py",
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.machine_gate(library, output / "degree-disassembly.txt")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("library", "oracle", "artifacts"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--sysroot", type=Path, default=Path("/usr/aarch64-linux-gnu"))
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() in {"x86_64", "amd64"},
        "requires Linux x64",
    )
    library, oracle, base = (
        p.resolve(strict=True) for p in (args.library, args.oracle, args.artifacts)
    )
    require(
        library != oracle and digest(oracle) == ORACLE_SHA,
        "frozen independent oracle SHA mismatch",
    )
    artifacts = {}
    for rid in RIDS:
        matches = [
            p.resolve()
            for p in (base / rid).iterdir()
            if p.suffix in {".so", ".dll", ".dylib"}
        ]
        require(len(matches) == 1, f"{rid}: expected one library")
        artifacts[rid] = matches[0]
    require(artifacts["linux-x64"] == library, "--library must match supplied artifact")
    before = {p: digest(p) for p in {oracle, *artifacts.values()}}
    output = (
        args.output.resolve()
        if args.output
        else Path(tempfile.mkdtemp(prefix="acceleration-native-2026-"))
    )
    output.mkdir(parents=True, exist_ok=True)
    (output / "manifest.json").write_text(
        json.dumps({str(p): sha for p, sha in before.items()}, indent=2) + "\n"
    )
    print(f"OUTPUT {output}\nORACLE {oracle} SHA256={before[oracle]}", flush=True)
    try:
        for rid, artifact in artifacts.items():
            exports(artifact, rid)
        machine_gate(library, output / "disassembly.txt")
        degree_gate(library, output)
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
        execute(["g++", *common, "-o", output / "check"], output / "compile-x64.log")
        execute(
            [output / "check", library, oracle, output / "corpus.bin"],
            output / "x64.log",
        )
        execute(
            ["aarch64-linux-gnu-g++", *common, "-o", output / "check-arm64"],
            output / "compile-arm64.log",
        )
        execute(
            [
                "qemu-aarch64",
                "-L",
                args.sysroot.resolve(strict=True),
                output / "check-arm64",
                artifacts["linux-arm64"],
                output / "corpus.bin",
            ],
            output / "arm64.log",
        )
        print(
            f"PASS x64 frozen raw SOFTWARE corpus SHA256={digest(output / 'corpus.bin')}",
            flush=True,
        )
    finally:
        for path, sha in before.items():
            require(digest(path) == sha, f"artifact changed: {path}")
        for name in ("check", "check-arm64"):
            (output / name).unlink(missing_ok=True)
        print(
            "PASS supplied SHA unchanged; harness binaries removed, source/corpus/logs retained",
            flush=True,
        )
    print(
        "PASS native gates only; ARM64 QEMU is software, six-RID exports are not six-OS execution; no GUI/FPS claim",
        flush=True,
    )


if __name__ == "__main__":
    main()
