# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Linux x64 supplied-artifact gate; compiles only the isolated dlopen harness."""

import argparse
import hashlib
import platform
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
HEADER = HERE.parents[3] / "native/LegacyMath/software_x87.h"


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def tool(name):
    found = shutil.which(name)
    require(found, f"Required tool unavailable: {name}")
    return found


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit(rows):
    """Reject unknown instruction shapes rather than silently weakening the gate."""
    names = [name for name, _ in rows]
    require(
        names.count("fnsave") == names.count("frstor") == 1,
        "one FNSAVE/FRSTOR required",
    )
    start, end = names.index("fnsave"), names.index("frstor")
    require(start < end, "restore precedes save")
    transaction = rows[start : end + 1]
    expected = [
        "fnsave",
        "fldcw",
        "fldt",
        "fldt",
        "fsub",
        "fstp",
        "fldt",
        "fldt",
        "fsub",
        "fstp",
        "fxch",
        "fmul",
        "fstp",
        "fldt",
        "fldt",
        "fsub",
        "fstp",
        "fldt",
        "fldt",
        "fsub",
        "fstp",
        "fxch",
        "fmul",
        "fstp",
        "fxch",
        "fadd",
        "fstp",
        "fstpl",
        "fldl",
        "fsqrt",
        "fldt",
        "fldt",
        "fmul",
        "fstp",
        "fstpl",
        "fldl",
        "fxch",
        "fsub",
        "fstp",
        "fldt",
        "fldt",
        "fmul",
        "fstp",
        "fstpl",
        "fldl",
        "fxch",
        "fucomi",
        "setb",
        "setnp",
        "frstor",
    ]
    require(
        [name for name, _ in transaction] == expected,
        "unexpected transaction: calls/branches, missing N64, or post-sqrt spill",
    )
    require(transaction[0][1] == transaction[-1][1], "save/restore locations differ")
    for index, (name, operands) in enumerate(transaction):
        if name == "fstpl":
            require(
                transaction[index + 1] == ("fldl", operands),
                "N64 must reload its store",
            )
        if name in {"fsub", "fadd", "fmul", "fucomi"}:
            require(
                operands.replace(" ", "") == "%st(1),%st", "IL left operand must be ST0"
            )
        if name == "fstp":
            require(operands == "%st(1)", "unexpected arithmetic pop")
    require(
        sum(name == "fstpl" for name, _ in transaction) == 3,
        "three mandatory N64 stores",
    )
    # The exact sequence also requires sqrt immediately followed by target-product loads.
    return transaction


def machine_gate(library):
    text = subprocess.check_output(
        [
            tool("llvm-objdump"),
            "-d",
            "--no-show-raw-insn",
            "--disassemble-symbols=hardware_laser_point_hit",
            str(library),
        ],
        text=True,
    )
    rows = []
    for line in text.splitlines():
        match = re.match(r"\s*[0-9a-f]+:\s+(\w+)\s*(.*?)\s*$", line)
        if match:
            rows.append((match[1], match[2].split("#", 1)[0].strip()))
    transaction = audit(rows)
    sqrt_index = next(i for i, row in enumerate(transaction) if row[0] == "fsqrt")
    store_index = next(i for i, row in enumerate(transaction) if row[0] == "fstpl")
    mutants = {
        "extra sqrt N64": transaction[: sqrt_index + 1]
        + [("fstpl", "0x0(%rsp)"), ("fldl", "0x0(%rsp)")]
        + transaction[sqrt_index + 1 :],
        "missing mandatory N64": transaction[:store_index]
        + transaction[store_index + 2 :],
        "transaction call": transaction[:sqrt_index]
        + [("callq", "diagnostic")]
        + transaction[sqrt_index + 1 :],
        "missing restore": transaction[:-1],
        "unordered accepted": [
            ("setb" if name == "setnp" else name, operands)
            for name, operands in transaction
        ],
    }
    for name, mutant in mutants.items():
        try:
            audit(mutant)
        except RuntimeError:
            continue
        raise RuntimeError(f"Machine negative control escaped: {name}")
    print(
        "PASS machine gate: one transaction, three N64 stores, extended sqrt, ordered compare; "
        f"{len(mutants)} rejected diagnostic mutations",
        flush=True,
    )


def export_gate(library):
    expected = set(
        re.findall(r"^SOFTWARE_API \w+ (\w+)\(", HEADER.read_text(), re.MULTILINE)
    )
    require(expected, "empty header export inventory")
    text = subprocess.check_output(
        [tool("llvm-nm"), "--dynamic", "--defined-only", "--extern-only", str(library)],
        text=True,
    )
    actual = {line.split()[-1] for line in text.splitlines() if line.split()}
    actual = {
        name
        for name in actual
        if name.startswith(("software_", "hardware_", "legacy_"))
    }
    require(
        actual == expected,
        f"Export mismatch: missing={expected - actual}, extra={actual - expected}",
    )
    print(
        f"PASS {len(actual)} exports match current header (not a frozen count)",
        flush=True,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", required=True, type=Path)
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() in {"x86_64", "amd64"},
        "This full x87 state gate requires Linux x64",
    )
    require(
        args.library.is_absolute() and args.library.is_file(),
        "--library must be an absolute file path",
    )
    library = args.library.resolve(strict=True)
    before = sha256(library)
    print(f"Library {library}\nSHA256 {before}", flush=True)
    try:
        export_gate(library)
        machine_gate(library)
        with tempfile.TemporaryDirectory(
            prefix="thsss-laser-native-checks-"
        ) as temporary:
            executable = Path(temporary) / "check"
            subprocess.run(
                [
                    tool("clang++"),
                    "-std=c++17",
                    "-O2",
                    "-Wall",
                    "-Wextra",
                    "-Werror",
                    str(HERE / "check.cpp"),
                    "-ldl",
                    "-o",
                    str(executable),
                ],
                check=True,
            )
            subprocess.run([str(executable), str(library)], check=True)
    finally:
        after = sha256(library)
        require(before == after, f"Supplied library changed: {before} -> {after}")
        print(f"PASS supplied library SHA256 unchanged: {after}", flush=True)


if __name__ == "__main__":
    main()
