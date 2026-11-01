"""2026 Linux x64 software ellipse gate; supplied kernel is never rebuilt or replaced."""

import argparse
import hashlib
import platform
import shutil
import subprocess
import tempfile
from pathlib import Path

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[2]
FLAGS = [
    "-std=c++17",
    "-O2",
    "-Wall",
    "-Wextra",
    "-Werror",
    "-ffp-contract=off",
    "-fno-fast-math",
    "-flto=thin",
    "-fuse-ld=lld",
]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def identity(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def replace_once(source, old, new):
    require(source.count(old) == 1, f"Instrumentation anchor changed: {old}")
    return source.replace(old, new, 1)


def instrument(source, disable_guard=False):
    source = replace_once(
        source,
        'extern "C" int software_ellipse_hit',
        """
#include <atomic>
static std::atomic<uint64_t> fast_count{0}, full_count{0};
extern "C" SOFTWARE_API void shortcut_reset() { fast_count = 0; full_count = 0; }
extern "C" SOFTWARE_API uint64_t shortcut_fast() { return fast_count.load(); }
extern "C" SOFTWARE_API uint64_t shortcut_full() { return full_count.load(); }
extern "C" int software_ellipse_hit""",
    )
    source = replace_once(
        source,
        "if (!ordered_less_than_one(num4)) {",
        "if (false) {"
        if disable_guard
        else "if (!ordered_less_than_one(num4)) {\n ++fast_count;",
    )
    return replace_once(
        source,
        "    software_ext_sub(&theta, &bearing, precision, &argument); // Preserve",
        "    ++full_count;\n    software_ext_sub(&theta, &bearing, precision, &argument); // Preserve",
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "library", type=Path, help="Explicit already-built Linux x64 library"
    )
    parser.add_argument(
        "--source-root",
        type=Path,
        default=ROOT,
        help="Root supplying native/LegacyMath/native_ellipse.cpp for diagnostic copy",
    )
    args = parser.parse_args()
    require(
        platform.system() == "Linux" and platform.machine() == "x86_64",
        "Requires Linux x64",
    )
    compiler = shutil.which("clang++")
    if compiler is None:
        raise RuntimeError("clang++ required")
    library = args.library.resolve(strict=True)
    native = args.source_root.resolve(strict=True) / "native/LegacyMath"
    source_path = native / "native_ellipse.cpp"
    require(library.is_file(), "Library must be a regular file")
    protected = [
        library,
        source_path,
        native / "software_x87.h",
        SUITE / "native_ellipse_cases.h",
        SUITE / "native_ellipse_oracle.h",
        SUITE / "../GameplayMath/backend_state.cpp",
    ]
    before = {path: identity(path) for path in protected}
    print(f"Kernel {library}\nSHA256 {before[library]}", flush=True)
    try:
        with tempfile.TemporaryDirectory(prefix="thsss-software-ellipse-") as directory:
            temporary = Path(directory)
            require(
                not temporary.is_relative_to(ROOT),
                "Build outputs must be outside workspace",
            )
            probe = temporary / "probe"
            subprocess.run(
                [
                    compiler,
                    *FLAGS,
                    str(SUITE / "software_ellipse_shortcut_probe.cpp"),
                    "-pthread",
                    "-ldl",
                    "-o",
                    str(probe),
                ],
                check=True,
            )
            for negative in (False, True):
                copied = temporary / "native_ellipse.cpp"
                copied.write_text(instrument(source_path.read_text(), negative))
                diagnostic = temporary / (
                    "disabled.so" if negative else "diagnostic.so"
                )
                # Only this throwaway copy has counters/renamed entries; primitives use the supplied library.
                subprocess.run(
                    [
                        compiler,
                        *FLAGS,
                        "-shared",
                        "-fPIC",
                        "-I",
                        str(native),
                        "-Dsoftware_ellipse_hit=shortcut_ellipse_hit",
                        "-Dhardware_ellipse_hit=shortcut_hardware_hit",
                        "-Dlegacy_ellipse_hit=shortcut_dispatch_hit",
                        str(copied),
                        str(library),
                        "-Wl,-z,defs",
                        "-Wl,-Bsymbolic-functions",
                        f"-Wl,-rpath,{library.parent}",
                        "-o",
                        str(diagnostic),
                    ],
                    check=True,
                )
                command = [str(probe), str(library), str(diagnostic)]
                if not negative:
                    subprocess.run(command, check=True)
                    continue
                control = subprocess.run(
                    [*command, "--self-test"], check=False, capture_output=True, text=True
                )
                require(
                    control.returncode != 0
                    and "literal actual guard counters vs oracle R" in control.stderr,
                    f"Disabled-shortcut negative control failed to fail correctly: {control.stderr}",
                )
                print(
                    "PASS disabled-guard negative control rejected by actual counters",
                    flush=True,
                )
    finally:
        require(
            all(identity(path) == digest for path, digest in before.items()),
            "Supplied library/source/frozen oracle/corpus/FPU fixture changed during gate",
        )
        print("PASS protected SHA256 identities unchanged", flush=True)
    print(
        "Diagnostic extra exports are TEST ONLY; no normal ABI/performance/ARM/game-loop claim."
    )


if __name__ == "__main__":
    main()
