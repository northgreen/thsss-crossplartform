#!/usr/bin/env python3
"""2026 Linux x64 fused-geometry gate; never rebuilds/replaces the supplied kernel."""

import argparse
import ctypes
import gzip
import hashlib
import json
import platform
import re
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[2]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def identity(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def audit_integer_reject(library):
    disassembler = shutil.which("objdump")
    if not disassembler:
        raise RuntimeError("Missing objdump")
    assembly = subprocess.check_output(
        [disassembler, "-d", "-C", "--no-show-raw-insn", str(library)], text=True
    )
    functions = dict(re.findall(
        r"^[0-9a-f]+ <([^\n]+)>:\n(.*?)(?=^[0-9a-f]+ <|\Z)", assembly, re.M | re.S
    ))
    helpers = [body for name, body in functions.items() if name.startswith("circle_integer_reject(")]
    require(len(helpers) == 1, "Expected shared production integer reject helper")
    # Integer SSE bit moves/XOR are permitted; no FP arithmetic, conversion,
    # comparison, x87 instruction, environment save/restore, or function call.
    integer_instructions = set("""
        mov movb movw movl movq movabs movd movdqa movdqu movzbl movzwl movslq
        pxor pand por push pop lea add sub inc dec neg not and or xor test cmp
        shl shr sar sal imul mul bsr bsf bt sbb adc cltq cdqe ret int3 nop nopl nopw
    """.split())
    instructions = re.findall(r"^\s+[0-9a-f]+:\s+(\w+)", helpers[0], re.M)
    require(instructions, "Empty reject disassembly")
    for instruction in instructions:
        require(instruction in integer_instructions or instruction.startswith(("j", "set", "cmov")),
                f"Noninteger instruction in reject helper: {instruction}")
    entry = functions["hardware_circle_hit"]
    call = entry.index("<circle_integer_reject(")
    require(call < entry.index("<software_ext_load_float@plt>") < entry.index("fnsave"),
            "Reject must precede ext80 codecs and FNSAVE")
    require(not re.search(r"\b(?:fnsave|fld\w*|fst\w*|frstor)\b", entry[:call]),
            "Hardware entry touches x87 before reject")
    print(f"PASS machine audit: shared helper {len(instructions)} integer instructions; reject before codecs/FNSAVE", flush=True)


def frozen_distance(library):
    directory = SUITE.parent / "MathBaseline"
    manifest = json.loads((directory / "baseline.json").read_text())
    data = (directory / "inputs.bin").read_bytes()
    require(
        hashlib.sha256(data).hexdigest() == manifest["input"]["sha256"],
        "Frozen input identity",
    )
    kernel = ctypes.CDLL(str(library))
    for suffix, precision in (("x86", 53), ("d3d-x86", 24)):
        name = "actual-windows-clr2-" + suffix + ".csv"
        capture = gzip.decompress((directory / (name + ".gz")).read_bytes())
        require(
            hashlib.sha256(capture).hexdigest() == manifest["files"][name]["sha256"],
            "Frozen CLR2 capture identity",
        )
        expected = {
            int(row.split(",")[1]): int(row.split(",")[3], 16)
            for row in capture.decode().splitlines()
            if row.startswith("actual.distance,")
        }
        for backend in ("software", "hardware", "legacy"):
            function = getattr(kernel, backend + "_distance")
            function.argtypes = [ctypes.c_float] * 6 + [
                ctypes.c_int,
                ctypes.POINTER(ctypes.c_double),
            ]
            function.restype = ctypes.c_int
            for index, (_, __, x, y) in enumerate(struct.iter_unpack("<ddff", data)):
                if index >= 4096:
                    break
                result = ctypes.c_double()
                require(
                    function(
                        123.456,
                        123.456,
                        234.567,
                        234.567,
                        x,
                        y,
                        precision,
                        ctypes.byref(result),
                    )
                    == 0,
                    "Frozen distance status",
                )
                bits = struct.unpack("<Q", bytes(result))[0]
                require(
                    bits == expected[index],
                    f"Frozen CLR2 {suffix}/{backend}/{index}: {bits:016x} != {expected[index]:016x}",
                )
        print(
            f"PASS frozen actual CLR2 {suffix}, PC{precision}: 4096 x three entrypoints",
            flush=True,
        )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "library", type=Path, help="Explicit already-built Linux x64 kernel"
    )
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() == "x86_64",
        "Requires Linux x64",
    )
    library = args.library.resolve(strict=True)
    require(library.is_file(), "Kernel must be a regular file")
    compiler = shutil.which("clang++")
    require(compiler, "Missing clang++")
    before = identity(library)
    print(f"Kernel {library}\nSHA256 {before}", flush=True)
    audit_integer_reject(library)
    with tempfile.TemporaryDirectory(prefix="thsss-native-geometry-") as temporary:
        require(
            not Path(temporary).resolve().is_relative_to(ROOT),
            "Build output must be outside workspace",
        )
        executable = Path(temporary) / "native_geometry_probe"
        # Compile the exact production integer helper for direct branch assertions;
        # all public operation results still come from the explicit supplied kernel.
        source = (ROOT / "native/LegacyMath/native_geometry.cpp").read_text()
        helper = Path(temporary) / "coarse_helper.h"
        start = source.index("static uint32_t circle_float_bits")
        end = source.index("// Convert Int32 exactly", start)
        helper.write_text("#include <cstdint>\n" + source[start:end])
        command = [
            compiler,
            "-std=c++17",
            "-O2",
            "-Wall",
            "-Wextra",
            "-Werror",
            "-include", str(helper),
            str(SUITE / "native_geometry_probe.cpp"),
            "-ldl",
            "-Wl,-rpath," + str(library.parent),
            "-o",
            str(executable),
        ]
        print("Compile:", " ".join(command), flush=True)
        subprocess.run(command, check=True)
        require(identity(library) == before, "Kernel changed during compilation")
        subprocess.run([str(executable), str(library)], check=True)
        frozen_distance(library)
    require(
        identity(library) == before, "Kernel changed during validation; result invalid"
    )
    print(f"PASS supplied kernel unchanged SHA256 {before}", flush=True)


if __name__ == "__main__":
    main()
