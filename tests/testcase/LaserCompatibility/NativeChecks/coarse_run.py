# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Isolated coarse-rejection differential and fail-closed Linux x64 machine gate."""

import argparse
import re
import subprocess
import tempfile
from pathlib import Path

import run

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]


def disassemble(library):
    text = subprocess.check_output(
        [run.tool("llvm-objdump"), "-d", "--no-show-raw-insn", str(library)], text=True
    )
    functions = {}
    current = None
    for line in text.splitlines():
        match = re.match(r"^[0-9a-f]+ <(.*)>:$", line)
        if match:
            current = functions.setdefault(match[1], [])
        match = re.match(r"\s*([0-9a-f]+):\s+(\w+)\s*(.*?)\s*$", line)
        if match and current is not None:
            current.append(
                (int(match[1], 16), match[2], match[3].split("#", 1)[0].strip())
            )
    return functions


def target(operands):
    return int(operands.split()[0], 16)


def integer_only(rows):
    # Explicit instruction inventory: unknown compiler shapes fail closed.
    allowed = set(
        "pxor movd movl movq movabsq movss movaps movdqa movzbl movslq movzwl pushq popq retq int3 nopl nopw nop shrl shrq shll shlq sarl sarq andl andq orl orq xorl xorq notl notq negl negq addl addq subl subq imulq imull leal leaq cmpl cmpq testl testq testb incl incq decl decq setb seta setl setg setne sete cmovbl cmovael cmovbel cmovnel cmoveq cmovneq cmovnsq cmovsl cmovsql cmovlq".split()
    )
    # Reviewed integer byte operations, sign extension and condition-code moves.
    allowed.update("setns sets andb orb subb movb cltq cmovnsl sbbl".split())
    branches = set("ja jae jb jbe je jne jg jge jl jle js jns jmp".split())
    for _, name, _ in rows:
        run.require(name in allowed | branches, f"unproved integer instruction: {name}")


def dominance(rows, call_index, save_index):
    positions = {row[0]: i for i, row in enumerate(rows)}
    pending, visited = [0], set()
    while pending:
        i = pending.pop()
        if i in visited or i == call_index:
            continue
        run.require(i != save_index, "FNSAVE reachable without guard")
        visited.add(i)
        _, name, operands = rows[i]
        if name in {"retq", "int3"}:
            continue
        if name.startswith("j"):
            run.require(target(operands) in positions, "external control flow")
            pending.append(positions[target(operands)])
            if name == "jmp":
                continue
        run.require(i + 1 < len(rows), "unterminated control flow")
        pending.append(i + 1)


def fast_exit(rows, call_index, hardware):
    # Match actual compiler's true-return arm, including the zero-output store.
    positions = {row[0]: i for i, row in enumerate(rows)}
    i = call_index + 1
    expected = (
        [("testb", "%al, %al"), ("je", None)]
        if hardware
        else [
            ("movl", "%eax, %ecx"),
            ("xorl", "%eax, %eax"),
            ("movl", "$0x0, %edx"),
            ("testb", "%cl, %cl"),
            ("jne", None),
        ]
    )
    for name, operand in expected:
        run.require(
            rows[i][1] == name and (operand is None or rows[i][2] == operand),
            "unknown guard-result branch shape",
        )
        i += 1
    if not hardware:
        i = positions[target(rows[i - 1][2])]
    visited, path = set(), []
    while i not in visited:
        visited.add(i)
        row = rows[i]
        path.append(row)
        _, name, operands = row
        if name == "retq":
            break
        if name == "jmp":
            i = positions[target(operands)]
        else:
            run.require(not name.startswith("j"), "unknown fast-exit branch")
            i += 1
    run.require(path[-1][1] == "retq", "fast exit does not return")
    integer_only(path)
    store = ("movl", "$0x0, (%rbx)") if hardware else ("movl", "%edx, (%r14)")
    run.require(
        sum((n, o) == store for _, n, o in path) == 1,
        "fast exit must write zero exactly once",
    )
    if hardware:
        run.require(
            any(n == "xorl" and o == "%eax, %eax" for _, n, o in path),
            "fast status not zero",
        )
    return path


def validation_gate(rows, call_index):
    """Prove null/invalid-PC branches return -1 without reaching the helper."""
    positions = {row[0]: i for i, row in enumerate(rows)}
    run.require([(n, o) for _, n, o in rows[:2]] ==
                [('movl', '$0xffffffff, %eax'), ('testq', '%rdx, %rdx')],
                'unknown null validation shape')
    run.require(rows[2][1] == 'je', 'missing null error branch')
    pc = next(i for i, r in enumerate(rows[:call_index])
              if r[1] == 'cmpl' and r[2].startswith('$0x35,'))
    run.require(rows[pc + 1][1] == 'je' and rows[pc + 2][1] == 'cmpl' and
                rows[pc + 2][2].startswith('$0x18,') and rows[pc + 3][1] == 'jne',
                'unknown PC validation shape')
    run.require(target(rows[pc + 1][2]) == rows[pc + 4][0], 'PC53 bypasses validation boundary')
    # Nothing in the validation prefix may overwrite the -1 return register.
    for _, name, operands in rows[1:pc + 4]:
        run.require(not operands.endswith(('%eax', '%ax', '%al', '%rax')),
                    'error status overwritten before validation')
    for branch in [2, pc + 3]:
        i = positions[target(rows[branch][2])]
        while rows[i][1] != 'retq':
            _, name, operands = rows[i]
            run.require(name in {'addq', 'popq'} and
                        not operands.endswith(('%eax', '%rax')), 'unknown error-return arm')
            i += 1


def machine_gate(library):
    functions = disassemble(library)
    helpers = [
        (name, rows)
        for name, rows in functions.items()
        if "laser_integer_reject" in name
    ]
    run.require(len(helpers) == 1, "one private compiled helper required")
    helper, rows = helpers[0]
    integer_only(rows)
    try:
        integer_only(rows + [(0, "addss", "%xmm0, %xmm1")])
    except RuntimeError:
        pass
    else:
        raise RuntimeError("FP mutation escaped")
    for name in ["software_laser_point_hit", "hardware_laser_point_hit"]:
        rows = functions[name]
        calls = [i for i, r in enumerate(rows) if r[1] == "callq" and helper in r[2]]
        run.require(len(calls) == 1, "one guard call required")
        index = calls[0]
        integer_only(rows[:index])
        validation_gate(rows, index)
        prefix = [(n, o) for _, n, o in rows[:index]]
        run.require(
            ("testq", "%rdx, %rdx") in prefix
            and any(n == "cmpl" and o.startswith("$0x35,") for n, o in prefix)
            and any(n == "cmpl" and o.startswith("$0x18,") for n, o in prefix),
            "missing API validation",
        )
        fast_exit(rows, index, name.startswith("hardware"))
        for operation, row in enumerate(rows):
            if row[1] == 'callq' and operation != index:
                dominance(rows, index, operation)
        if name.startswith("hardware"):
            save = next(i for i, r in enumerate(rows) if r[1] == "fnsave")
            dominance(rows, index, save)
            mutant = list(rows)
            mutant[0] = (rows[0][0], "jmp", hex(rows[save][0]))
            try:
                dominance(mutant, index, save)
            except RuntimeError:
                pass
            else:
                raise RuntimeError("guard bypass mutation escaped")
    print(
        "PASS coarse machine: private integer-only helper, validation prefix, true zero-return arm, FNSAVE dominance; FP/bypass mutations rejected",
        flush=True,
    )


def exports(root):
    header = run.HEADER.read_text()
    expected = set(re.findall(r"^SOFTWARE_API \w+ (\w+)\(", header, re.MULTILINE))
    for rid in [
        "linux-x64",
        "linux-arm64",
        "win-x64",
        "win-arm64",
        "osx-x64",
        "osx-arm64",
    ]:
        filename = (
            "thsss_legacy_math.dll"
            if rid.startswith("win")
            else "libthsss_legacy_math.dylib"
            if rid.startswith("osx")
            else "libthsss_legacy_math.so"
        )
        library = root / rid / filename
        if rid.startswith("win"):
            text = subprocess.check_output(
                [run.tool("llvm-readobj"), "--coff-exports", str(library)], text=True
            )
            actual = set(
                re.findall(r"Name: ((?:software_|hardware_|legacy_)\w+)", text)
            )
        else:
            flags = ["--defined-only", "--extern-only"] + (
                [] if rid.startswith("osx") else ["--dynamic"]
            )
            text = subprocess.check_output(
                [run.tool("llvm-nm"), *flags, str(library)], text=True
            )
            actual = {
                line.split()[-1].lstrip("_")
                for line in text.splitlines()
                if line.split()
            }
            actual = {
                n for n in actual if n.startswith(("software_", "hardware_", "legacy_"))
            }
        wanted = {
            n
            for n in expected
            if not (rid.endswith("arm64") and n.startswith("hardware_"))
        }
        run.require(
            actual == wanted and len(actual) == (46 if rid.endswith("arm64") else 63),
            f"{rid} exports changed",
        )
        print(f"PASS {rid} exports={len(actual)}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--arm-runtime", action="store_true")
    args = parser.parse_args()
    library = args.artifacts.resolve() / "linux-x64/libthsss_legacy_math.so"
    before = run.sha256(library)
    exports(args.artifacts)
    run.machine_gate(library)
    machine_gate(library)
    source = (ROOT / "native/LegacyMath/native_laser.cpp").read_text()
    helper = source[
        source.index("static uint32_t laser_float_bits") : source.index(
            "static software_ext80 load_float"
        )
    ]
    with tempfile.TemporaryDirectory(prefix="thsss-laser-coarse-") as directory:
        directory = Path(directory)
        support = (HERE / 'check.cpp').read_text().split('int main(int argc, char **argv) {')[0]
        run.require('struct Raw' in support and 'check_edges' in support, 'raw support extraction changed')
        (directory / 'raw_support.h').write_text(support)
        probe = directory / "probe.cpp"
        probe.write_text(
            "#include <stdint.h>\n"
            + helper
            + """
extern "C" int diagnostic_laser_reject(float px, float py, float tx1, float tx2,
float ty1, float ty2, int region, float scale, float radius, float length, int, int *) {
return laser_integer_reject(tx1, tx2, ty1, ty2, px, py, radius, length, region, scale);
}
"""
        )
        executable = directory / "coarse"
        subprocess.run(
            [
                run.tool("clang++"),
                "-std=c++17",
                "-O2",
                "-Wall",
                "-Wextra",
                "-Werror",
                "-I" + str(directory),
                str(HERE / "coarse.cpp"),
                str(probe),
                "-ldl",
                "-o",
                str(executable),
            ],
            check=True,
        )
        subprocess.run([str(executable), str(library)], check=True)
        if args.arm_runtime:
            arm_library = args.artifacts.resolve() / 'linux-arm64/libthsss_legacy_math.so'
            arm = directory / 'coarse-arm64'
            subprocess.run([run.tool('aarch64-linux-gnu-g++'), '-std=c++17', '-O2',
                            '-Wall', '-Wextra', '-Werror', '-I' + str(directory),
                            str(HERE / 'coarse.cpp'), str(probe), '-ldl', '-o', str(arm)], check=True)
            subprocess.run([run.tool('qemu-aarch64'), '-L', '/usr/aarch64-linux-gnu',
                            str(arm), str(arm_library)], check=True)
    run.require(run.sha256(library) == before, "artifact mutated")
    print(f"PASS supplied library unchanged SHA256={before}", flush=True)


if __name__ == "__main__":
    main()
