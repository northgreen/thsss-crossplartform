#!/usr/bin/env python3
"""2026 Linux x64 raw-native ellipse gate; loads an explicit read-only kernel, never rebuilds it."""

import argparse
import hashlib
from pathlib import Path
import platform
import re
import shutil
import struct
import subprocess
import tempfile

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[2]
TARGETS = {
    "linux-x64": ("libthsss_legacy_math.so", 62),
    "linux-arm64": ("libthsss_legacy_math.so", 183),
    "win-x64": ("thsss_legacy_math.dll", 0x8664),
    "win-arm64": ("thsss_legacy_math.dll", 0xAA64),
    "osx-x64": ("libthsss_legacy_math.dylib", 0x01000007),
    "osx-arm64": ("libthsss_legacy_math.dylib", 0x0100000C),
}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def tool(name):
    result = shutil.which(name)
    require(result, f"Missing tool: {name}")
    return result


def identity(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit_instructions(library):
    assembly = subprocess.check_output(
        [
            tool("llvm-objdump"),
            "-d",
            "--no-show-raw-insn",
            "--disassemble-symbols=hardware_ellipse_hit",
            str(library),
        ],
        text=True,
    )
    for instruction in ("fnsave", "frstor", "fsin", "fcos", "fpatan", "fsqrt"):
        require(
            len(re.findall(r"\b" + instruction + r"\b", assembly)) == 1,
            f"Expected exactly one {instruction} in hardware ellipse",
        )
    block = assembly[assembly.index("fnsave") : assembly.index("frstor")]
    require(not re.search(r"\bcall\w*\b", block), "Call inside live x87 transaction")
    require("fldpi" not in assembly, "Must load the canonical binary64 Pi, not FLDPI")
    host_fp = r"\b(?:v?(?:add|sub|mul|div|sqrt)[sp][sd]|cvt\w+)\b"
    require(
        not re.search(host_fp, assembly), "Unexpected host FP arithmetic/conversion"
    )
    print(
        "PASS instructions: single FNSAVE/FRSTOR; FSIN/FCOS/FPATAN/FSQRT; no live-block calls/FLDPI/host FP",
        flush=True,
    )


def audit_architecture(rid, path, expected_machine):
    data = path.read_bytes()
    if rid.startswith("linux"):
        require(data[:6] == b"\x7fELF\x02\x01", f"{rid}: expected ELF64 little endian")
        machine = struct.unpack_from("<H", data, 18)[0]
    elif rid.startswith("win"):
        require(data[:2] == b"MZ", f"{rid}: expected PE")
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        require(data[pe : pe + 4] == b"PE\0\0", f"{rid}: expected PE signature")
        require(
            struct.unpack_from("<H", data, pe + 24)[0] == 0x20B,
            f"{rid}: expected PE32+",
        )
        machine = struct.unpack_from("<H", data, pe + 4)[0]
    else:
        require(
            data[:4] == b"\xcf\xfa\xed\xfe", f"{rid}: expected Mach-O64 little endian"
        )
        machine = struct.unpack_from("<I", data, 4)[0]
    require(machine == expected_machine, f"{rid}: wrong machine {machine:x}")


def audit_exports(rid, path):
    header = (ROOT / "native/LegacyMath/software_x87.h").read_text()
    expected = set(re.findall(r"^SOFTWARE_API \w+ (\w+)\(", header, re.M))
    if rid.endswith("arm64"):
        expected = {name for name in expected if not name.startswith("hardware_")}
    if rid.startswith("linux"):
        text = subprocess.check_output(
            [tool("llvm-readobj"), "--dyn-symbols", str(path)], text=True
        )
        names = set(re.findall(r"Name: (\w+) \(", text))
    elif rid.startswith("win"):
        text = subprocess.check_output(
            [tool("llvm-readobj"), "--coff-exports", str(path)], text=True
        )
        names = set(re.findall(r"^  Name: (\w+)$", text, re.M))
    else:
        text = subprocess.check_output(
            [tool("llvm-nm"), "--defined-only", "--extern-only", str(path)], text=True
        )
        names = set(re.findall(r"\b_((?:software|hardware|legacy)_\w+)$", text, re.M))
    require(
        names == expected,
        f"{rid} export mismatch missing={expected - names} extra={names - expected}",
    )
    print(
        f"PASS {rid}: architecture + exact {len(names)} ABI exports SHA256 {identity(path)}",
        flush=True,
    )


def artifact_paths(directory):
    if directory is None:
        return []
    paths = []
    for rid, (filename, machine) in TARGETS.items():
        path = (directory / rid / filename).resolve(strict=True)
        require(path.is_file(), f"Not a regular artifact: {path}")
        audit_architecture(rid, path, machine)
        audit_exports(rid, path)
        paths.append(path)
    print(
        "Six-RID checks are metadata only, not foreign-OS/ARM runtime validation",
        flush=True,
    )
    return paths


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "library", type=Path, help="Explicit already-built Linux x64 kernel"
    )
    parser.add_argument(
        "--artifacts",
        type=Path,
        help="Optional existing six-RID artifact directory; never builds",
    )
    parser.add_argument(
        "--benchmark",
        action="store_true",
        help="Optional bounded native timing; not game FPS",
    )
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() == "x86_64",
        "Runtime gate requires Linux x64",
    )
    library = args.library.resolve(strict=True)
    require(library.is_file(), "Kernel must be a regular file")
    before = identity(library)
    print(f"Kernel {library}\nSHA256 {before}", flush=True)
    audit_architecture("linux-x64", library, 62)
    paths = artifact_paths(args.artifacts)
    identities = {path: identity(path) for path in [library, *paths]}
    require(identities[library] == before, "Kernel changed during metadata checks")
    audit_instructions(library)
    with tempfile.TemporaryDirectory(prefix="thsss-native-ellipse-") as temporary:
        require(
            not Path(temporary).resolve().is_relative_to(ROOT),
            "Harness output must be outside workspace",
        )
        executable = Path(temporary) / "native_ellipse_probe"
        command = [
            tool("clang++"),
            "-std=c++17",
            "-O2",
            "-Wall",
            "-Wextra",
            "-Werror",
            "-ffp-contract=off",
            "-fno-fast-math",
            str(SUITE / "native_ellipse_probe.cpp"),
            "-ldl",
            "-o",
            str(executable),
        ]
        print("Compile:", " ".join(command), flush=True)
        subprocess.run(command, check=True)
        require(
            identity(library) == before, "Kernel changed during harness compilation"
        )
        subprocess.run(
            [
                str(executable),
                str(library),
                *(["--benchmark"] if args.benchmark else []),
            ],
            check=True,
        )
    for path, expected in identities.items():
        require(
            identity(path) == expected, f"Library changed during validation: {path}"
        )
    print(f"PASS supplied kernel/artifacts unchanged SHA256 {before}", flush=True)
    print(
        "Scalar-capture/raw-native gate only; no concurrent getter, historical CLR2 or Replay claim",
        flush=True,
    )


if __name__ == "__main__":
    main()
