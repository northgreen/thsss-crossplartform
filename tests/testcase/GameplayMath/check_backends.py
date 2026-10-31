#!/usr/bin/env python3
"""2026 backend ABI v1 checks; usage: check_backends.py /path/libthsss_legacy_math.so."""

import argparse
import ctypes as C
import gzip
import hashlib
import json
import math
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import tempfile

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[2]


class Ext(C.Structure):
    _fields_ = [
        ("significand", C.c_uint64),
        ("signExp", C.c_uint16),
        ("padding", C.c_uint16 * 3),
    ]


P = C.POINTER(Ext)
SIGNATURES = {
    "load_double": [C.c_double, P],
    "load_float": [C.c_float, P],
    "store_double": [P, C.POINTER(C.c_double)],
    "store_float": [P, C.POINTER(C.c_float)],
    "sin": [P, P],
    "cos": [P, P],
    "atan2": [P, P, P],
    "sqrt": [P, C.c_int, P],
    **{name: [P, P, C.c_int, P] for name in ("add", "sub", "mul", "div")},
}
CHECKS = 0


def require(condition, message):
    global CHECKS
    CHECKS += 1
    if not condition:
        raise AssertionError(message)


def bind(library, prefix):
    result = {}
    for name, signature in SIGNATURES.items():
        function = getattr(library, prefix + "_ext_" + name)
        function.argtypes, function.restype = signature, C.c_int
        result[name] = function
    return result


def raw(api, name, *args):
    out = Ext.from_buffer_copy(b"\xa5" * 16)
    require(api[name](*args, C.byref(out)) == 0, name + " failed")
    require(tuple(out.padding) == (0, 0, 0), name + " did not zero ABI padding")
    return out


def stored(api, value, kind):
    out = C.c_double() if kind == "double" else C.c_float()
    require(api["store_" + kind](C.byref(value), C.byref(out)) == 0, "store failed")
    return struct.pack("<d" if kind == "double" else "<f", out.value)


def snapshot(api, value):
    return bytes(value), stored(api, value, "double"), stored(api, value, "float")


def errors(api, label):
    a = raw(api, "load_double", 3.0)
    for name, signature in SIGNATURES.items():
        arguments = []
        for index, kind in enumerate(signature):
            if index == len(signature) - 1:
                arguments.append(None)
            elif kind == P:
                arguments.append(C.byref(a))
            else:
                arguments.append(53 if kind == C.c_int else 1.25)
        require(api[name](*arguments) == -1, label + "/" + name + " null output status")
        for index, kind in enumerate(signature[:-1]):
            invalids = (
                (None,)
                if kind == P
                else (-100, 0, 23, 25, 52, 54, 64)
                if kind == C.c_int
                else ()
            )
            for invalid in invalids:
                output_type = signature[-1]._type_
                out = output_type()
                C.memset(C.byref(out), 0xA5, C.sizeof(out))
                before = bytes(out)
                args = list(arguments)
                args[index], args[-1] = invalid, C.byref(out)
                require(
                    api[name](*args) == -1, f"{label}/{name} invalid arg {index} status"
                )
                require(
                    bytes(out) == before, f"{label}/{name} invalid arg mutated output"
                )


def results(api, a, b):
    x, y = raw(api, "load_double", a), raw(api, "load_double", b)
    out = {"load_double": snapshot(api, x)}
    single = C.c_float(a).value
    out["load_float"] = snapshot(api, raw(api, "load_float", single))
    for name in ("sin", "cos"):
        out[name] = snapshot(api, raw(api, name, C.byref(x)))
    out["atan2"] = snapshot(api, raw(api, "atan2", C.byref(x), C.byref(y)))
    for precision in (24, 53):
        out[f"sqrt/{precision}"] = snapshot(
            api, raw(api, "sqrt", C.byref(x), precision)
        )
        for name in ("add", "sub", "mul", "div"):
            out[f"{name}/{precision}"] = snapshot(
                api, raw(api, name, C.byref(x), C.byref(y), precision)
            )
    return out


def is_nan_bytes(bits, kind):
    return math.isnan(struct.unpack("<d" if kind == 1 else "<f", bits)[0])


def compare_results(expected, actual, label, special=False):
    for name, value in expected.items():
        other = actual[name]
        if value == other:
            require(True, label)
            continue
        # NaN sign/payload selection is instruction-specific, not an ULP tolerance.
        both_nan = all(
            is_nan_bytes(value[i], i) and is_nan_bytes(other[i], i) for i in (1, 2)
        )
        if special and both_nan:
            for encoding in (value[0], other[0]):
                significand, exponent = struct.unpack_from("<QH", encoding)
                require((exponent & 0x7fff) == 0x7fff and (significand & 0xc000000000000000) == 0xc000000000000000,
                        label + "/" + name + " malformed quiet ext80 NaN")
            print(
                f"NaN encoding observation {label}/{name}: software={value[0].hex()} hardware={other[0].hex()}"
            )
            continue
        require(
            False,
            f"Exact ABI bits differ {label}/{name}: expected={[v.hex() for v in value]} actual={[v.hex() for v in other]}; "
            "x87 transcendental ULPs may vary by CPU microarchitecture; no golden/tolerance changed",
        )


def fixture_cases(apis):
    directory = SUITE.parent / "MathBaseline"
    manifest = json.loads((directory / "baseline.json").read_text())
    data = (directory / "inputs.bin").read_bytes()
    require(
        hashlib.sha256(data).hexdigest() == manifest["input"]["sha256"],
        "Frozen input identity",
    )
    records = list(struct.iter_unpack("<ddff", data))[:128]
    captures = []
    for suffix in ("x86", "d3d-x86"):
        filename = "windows-clr2-" + suffix + ".csv"
        capture = gzip.decompress((directory / (filename + ".gz")).read_bytes())
        require(
            hashlib.sha256(capture).hexdigest()
            == manifest["files"][filename]["sha256"],
            "Frozen output identity",
        )
        captures.append(
            {
                (name, int(index)): int(bits, 16)
                for name, index, _, bits in (
                    line.split(",") for line in capture.decode().splitlines()
                )
                if name in ("sin", "cos", "atan2") and int(index) < len(records)
            }
        )
    for index, (a, b, _, _) in enumerate(records):
        reference = results(apis["software"], a, b)
        for label, api in apis.items():
            actual = results(api, a, b)
            compare_results(reference, actual, f"frozen[{index}]/{label}")
            for name in ("sin", "cos", "atan2"):
                bits = int.from_bytes(actual[name][1], "little")
                for capture in captures:
                    require(
                        bits == capture[name, index],
                        f"Frozen CLR2 bits {index}/{label}/{name}: {bits:016x} != {capture[name, index]:016x}",
                    )
    print(
        f"ABI v1 frozen MathBaseline: {len(records)} inputs; both immutable CLR2 captures checked"
    )


def literals(apis):
    cases = [
        (7.0, 2.0),
        (-7.0, 2.0),
        (2.0, 7.0),
        (0.75, -2.5),
        (0.0, -0.0),
        (-0.0, 1.0),
        (2.0**-1074, 2.0),
        (2.0**-1022, 2.0**-1074),
        (float("inf"), 2.0),
        (-float("inf"), float("inf")),
        (float("nan"), 1.0),
    ]
    for index, (a, b) in enumerate(cases):
        expected = results(apis["software"], a, b)
        for label, api in apis.items():
            compare_results(
                expected, results(api, a, b), f"literal[{index}]/{label}", special=True
            )
    for api in apis.values():
        a, b = raw(api, "load_double", 7.0), raw(api, "load_double", 2.0)
        for precision in (24, 53):
            for name, expected in (("sub", 5.0), ("div", 3.5)):
                value = raw(api, name, C.byref(a), C.byref(b), precision)
                require(
                    stored(api, value, "double") == struct.pack("<d", expected),
                    "Noncommutative operand order: " + name,
                )
        for value in (0.0, -0.0, 2.0**-1074, float("inf")):
            loaded = raw(api, "load_double", value)
            require(
                stored(api, loaded, "double") == struct.pack("<d", value),
                "Double load/store bits",
            )
        for bits in (0, 0x80000000, 1, 0x00800000, 0x7F800000, 0xFF800000):
            value = struct.unpack("<f", struct.pack("<I", bits))[0]
            require(
                stored(api, raw(api, "load_float", value), "float")
                == struct.pack("<I", bits),
                "Float load/store bits",
            )


def native_harnesses(library, iterations):
    if platform.system() != "Linux" or platform.machine().lower() not in (
        "x86_64",
        "amd64",
    ):
        print("Native full-state and benchmark: NOT EXECUTED (require Linux x64)")
        return
    compiler = shutil.which("clang++")
    if not compiler:
        raise RuntimeError(
            "clang++ required for Linux x64 native state proof/benchmark"
        )
    print(
        subprocess.check_output([compiler, "--version"], text=True).splitlines()[0],
        flush=True,
    )
    with tempfile.TemporaryDirectory(prefix="thsss-backends-v1-") as temporary:
        for name in ("backend_state", "backend_benchmark"):
            executable = Path(temporary) / name
            command = [
                compiler,
                "-std=c++17",
                "-O2",
                "-Wall",
                "-Wextra",
                "-Werror",
                "-I",
                str(ROOT / "native/LegacyMath"),
                str(SUITE / (name + ".cpp")),
                "-ldl",
                "-o",
                str(executable),
            ]
            print("+ " + " ".join(command), flush=True)
            subprocess.run(command, check=True)
            arguments = [str(executable), str(library)]
            if name == "backend_benchmark":
                arguments.append(str(iterations))
            subprocess.run(arguments, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    parser.add_argument("--iterations", type=int, default=100000)
    args = parser.parse_args()
    if not 10000 <= args.iterations <= 10000000:
        parser.error("iterations must be in [10000, 10000000]")
    library_path = args.library.resolve(strict=True)
    print(
        f"ABI v1 host: {platform.platform()} machine={platform.machine()} library={library_path}",
        flush=True,
    )
    if platform.system() == "Linux":
        for line in Path("/proc/cpuinfo").read_text().splitlines():
            if line.startswith("model name"):
                print("CPU: " + line.split(":", 1)[1].strip(), flush=True)
                break
    library = C.CDLL(str(library_path))
    try:
        select, selected = (
            library.legacy_math_set_backend,
            library.legacy_math_get_backend,
        )
        select.argtypes, select.restype = [C.c_int], C.c_int
        selected.argtypes, selected.restype = [], C.c_int
        apis = {
            "software": bind(library, "software"),
            "legacy": bind(library, "legacy"),
        }
    except AttributeError as error:
        raise RuntimeError(
            "ABI v1 artifact not ready: missing dispatch export; rebuild by native coder required"
        ) from error
    hardware = platform.machine().lower() in ("x86_64", "amd64")
    require(
        C.sizeof(Ext) == 16 and Ext.signExp.offset == 8 and Ext.padding.offset == 10,
        "Raw ABI layout",
    )
    try:
        require(
            select(0) == 0 and selected() == (2 if hardware else 1), "Auto resolution"
        )
        if hardware:
            apis["hardware"] = bind(library, "hardware")
        for backend in (1, 2):
            previous = selected()
            status = select(backend)
            if backend == 2 and not hardware:
                require(
                    status == -2 and selected() == previous,
                    "Unsupported hardware must not mutate selection",
                )
                continue
            require(status == 0 and selected() == backend, "Force backend selection")
            for invalid in (-2147483648, -1, 3, 2147483647):
                require(
                    select(invalid) == -1 and selected() == backend,
                    "Unknown backend must not mutate selection",
                )
            for label, api in apis.items():
                errors(api, label)
            fixture_cases(apis)
            literals(apis)
            print(
                f"ABI v1 selected={backend}: exact raw/float/double, errors, padding PASS",
                flush=True,
            )
    finally:
        require(select(0) == 0 and selected() == (2 if hardware else 1), "Reset auto")
    native_harnesses(library_path, args.iterations)
    print(f"ABI v1 PASS: {CHECKS} assertions; no golden outputs modified")


if __name__ == "__main__":
    main()
