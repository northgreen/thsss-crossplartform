# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
# THSSS SDK-free portable kernel build, 2026.
import argparse
import hashlib
import json
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TARGETS = {
    "linux-x64": ("x86_64-unknown-linux-gnu", "elf", "libthsss_legacy_math.so"),
    "linux-arm64": ("aarch64-unknown-linux-gnu", "elf", "libthsss_legacy_math.so"),
    "win-x64": ("x86_64-pc-windows-msvc", "coff", "thsss_legacy_math.dll"),
    "win-arm64": ("aarch64-pc-windows-msvc", "coff", "thsss_legacy_math.dll"),
    "osx-x64": ("x86_64-apple-macos11", "macho", "libthsss_legacy_math.dylib"),
    "osx-arm64": ("arm64-apple-macos11", "macho", "libthsss_legacy_math.dylib"),
}


class BuildError(Exception):
    pass


class UnsupportedRID(BuildError):
    pass


class MissingTool(BuildError):
    pass


class BadSourceHash(BuildError):
    pass


class InvalidManifest(BuildError):
    pass


def tool(name):
    found = shutil.which(name)
    if not found:
        raise MissingTool(name)
    return found


def source_path(relative):
    path = (ROOT / relative).resolve()
    if not path.is_relative_to(ROOT) or not path.is_file():
        raise InvalidManifest(f"Not a local source file: {relative}")
    return path


def sources():
    inventory = json.loads((ROOT / "license-inventory.json").read_text())
    origin = json.loads((ROOT / "origin.json").read_text())
    revision = "104a66e5c939077a5cb1fc744bc21b74a4f77f64"
    if inventory["revision"] != revision or origin["revision"] != revision:
        raise InvalidManifest("Unexpected upstream revision")
    entries = {"vendor/" + entry["path"]: entry for entry in inventory["files"]}
    for name, entry in entries.items():
        data = source_path(name).read_bytes()
        if (
            len(data) != entry["bytes"]
            or hashlib.sha256(data).hexdigest() != entry["sha256"]
        ):
            raise BadSourceHash(name)
    for entry in inventory["license_texts"]:
        if hashlib.sha256(source_path(entry["path"]).read_bytes()).hexdigest() != entry["sha256"]:
            raise BadSourceHash(entry["path"])
    closure = json.loads((ROOT / "closure.json").read_text())
    if len(closure) != len(set(closure)):
        raise InvalidManifest("Duplicate translation unit")
    for name in closure:
        if name not in entries and name not in (
            "software_x87.cpp", "hardware_x87.cpp", "legacy_dispatch.cpp", "memory.cpp"
        ):
            raise InvalidManifest(f"Uninventoried translation unit: {name}")
    return [source_path(name) for name in closure]


def run(command):
    print(shlex.join(map(str, command)), flush=True)
    subprocess.run(list(map(str, command)), check=True)


def link_command(kind, linker, triple, objects, output, temporary):
    if kind == "coff":
        return [
            linker,
            "/dll",
            "/noentry",
            "/nodefaultlib",
            "/Brepro",
            "/timestamp:0",
            "/opt:ref",
            "/opt:icf",
            "/out:" + str(output),
            "/implib:" + str(temporary / "exports.lib"),
            *objects,
        ]
    if kind == "macho":
        architecture = "arm64" if triple.startswith("arm64") else "x86_64"
        exports = temporary / "exports.txt"
        header = (ROOT / "software_x87.h").read_text().splitlines()
        names = [
            match.group(1)
            for line in header
            if (match := re.match(r"SOFTWARE_API \w+ (\w+)\(", line))
            and (architecture == "x86_64" or not match.group(1).startswith("hardware_"))
        ]
        exports.write_text("".join("_" + name + "\n" for name in names))
        return [
            linker,
            "-dylib",
            "-arch",
            architecture,
            "-platform_version",
            "macos",
            "11.0",
            "11.0",
            "-no_uuid",
            "-install_name",
            "@rpath/" + output.name,
            "-exported_symbols_list",
            exports,
            "-o",
            output,
            *objects,
        ]
    exports = temporary / "exports.map"
    exports.write_text("{ global: software_*; hardware_ext_*; legacy_*; local: *; };\n")
    return [
        linker,
        "-shared",
        "--no-undefined",
        "--gc-sections",
        "--build-id=none",
        "-z",
        "noexecstack",
        "-z",
        "relro",
        "-z",
        "now",
        "--version-script=" + str(exports),
        "-soname",
        output.name,
        "-o",
        output,
        *objects,
    ]


def build(rid, directory):
    if rid not in TARGETS:
        raise UnsupportedRID(f"{rid}; supported: {', '.join(TARGETS)}")
    triple, kind, filename = TARGETS[rid]
    compiler = tool("clang++")
    linker = tool({"elf": "ld.lld", "coff": "lld-link", "macho": "ld64.lld"}[kind])
    units = sources()
    directory = directory.resolve()
    source_tree = next((path for path in ROOT.parents if (path / ".git").exists()), ROOT)
    if directory.is_relative_to(source_tree) and "obj" not in directory.relative_to(source_tree).parts:
        raise BuildError("Output must be outside the source tree or inside an ignored obj directory")
    directory.mkdir(parents=True, exist_ok=True)
    flags = [
        "--target=" + triple,
        "-std=c++17",
        "-O2",
        "-ffreestanding",
        "-nostdinc++",
        "-fno-exceptions",
        "-fno-rtti",
        "-fvisibility=hidden",
        "-fno-stack-protector",
        "-fno-unwind-tables",
        "-fno-asynchronous-unwind-tables",
        "-ffunction-sections",
        "-fdata-sections",
        "-ffp-contract=off",
        "-DNDEBUG",
        "-DSOFTFLOAT_FAST_INT64",
        "-DSOFTFLOAT_FAST_DIV64TO32",
        "-DSOFTFLOAT_FAST_DIV32TO16",
        "-DINLINE_LEVEL=5",
        "-ffile-prefix-map=" + str(ROOT) + "=.",
        "-Werror=date-time",
    ]
    if kind != "coff":
        flags += ["-fPIC"]
    includes = [
        ROOT / path
        for path in (
            "platform",
            ".",
            "vendor",
            "vendor/fpu",
            "vendor/softfloat3e/include",
            "vendor/softfloat3e/8086-SSE",
        )
    ]
    flags += ["-I" + str(path) for path in includes]
    with tempfile.TemporaryDirectory(prefix="thsss-legacy-math-") as workspace:
        temporary = Path(workspace)
        objects = []
        for source in units:
            obj = temporary / source.relative_to(ROOT).with_suffix(".o")
            obj.parent.mkdir(parents=True, exist_ok=True)
            run([compiler, *flags, "-c", source, "-o", obj])
            objects.append(obj)
        artifact = temporary / filename
        run(link_command(kind, linker, triple, objects, artifact, temporary))
        # Publish a complete artifact: concurrent readers must not observe a partial shared library.
        with tempfile.NamedTemporaryFile(prefix=filename + ".", dir=directory, delete=False) as staged:
            staged_path = Path(staged.name)
        try:
            shutil.copyfile(artifact, staged_path)
            staged_path.replace(directory / filename)
        finally:
            staged_path.unlink(missing_ok=True)
    print(directory / filename, flush=True)


def main():
    parser = argparse.ArgumentParser(description="Build SDK-free THSSS legacy math")
    parser.add_argument("--rid", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        build(args.rid, args.output)
    except (
        BuildError,
        OSError,
        ValueError,
        KeyError,
        subprocess.CalledProcessError,
    ) as error:
        print(f"{type(error).__name__}: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
