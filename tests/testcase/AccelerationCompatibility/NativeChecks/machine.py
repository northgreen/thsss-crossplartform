# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Exact acceleration transaction whitelist and rejection diagnostics."""

import re
import subprocess


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


EXPECTED = "fnsave fldcw fldt fldt fdiv fstp fldt fxch fmul fstp fstpl fldl fldcw fcos fstps flds fldcw fldt fmul fstp fstps fldl fldcw fsin fstps flds fldcw fldt fmul fstp fstps frstor".split()


def audit(rows):
    ops = [op for op, _ in rows]
    require(ops.count("fnsave") == ops.count("frstor") == 1, "one save/restore")
    a, b = ops.index("fnsave"), ops.index("frstor")
    trans = rows[a : b + 1]
    require([op for op, _ in trans] == EXPECTED, "unexpected transaction sequence")
    require(trans[0][1] == trans[-1][1], "restore address")
    for i in (4, 8, 18, 28):
        require(trans[i][1].replace(" ", "") == "%st(1),%st", "NaN operand order")
    for i in (5, 9, 19, 29):
        require(trans[i][1] == "%st(1)", "pop operand")
    require(trans[7][1] == "%st(1)", "num multiply exchange")
    require(trans[10][1] == trans[11][1] == trans[21][1], "shared N64 trig argument")
    require(trans[14][1] == trans[15][1] == trans[24][1] == trans[25][1], "N32 reload")
    require(trans[1][1] == trans[16][1] == trans[26][1], "PC24/53 arithmetic control")
    require(trans[12][1] == trans[22][1] != trans[1][1], "PC64 trig control")
    # Inputs are direction, 180, binary64 pi, accX, accY: disallow accX reuse.
    require(
        trans[2][1] == "0x10(%rax)"
        and trans[3][1] == "(%rax)"
        and trans[6][1] == "0x20(%rax)",
        "num operands",
    )
    require(
        trans[17][1] == "0x30(%rax)" and trans[27][1] == "0x40(%rax)",
        "distinct acceleration operands",
    )
    require(
        len({trans[14][1], trans[20][1], trans[30][1]}) == 3,
        "distinct temporary/result stores",
    )
    return trans


def machine_gate(library, output):
    text = subprocess.check_output(
        [
            "llvm-objdump",
            "-d",
            "--no-show-raw-insn",
            "--disassemble-symbols=hardware_script_acceleration",
            str(library),
        ],
        text=True,
    )
    output.write_text(text)
    rows = []
    for line in text.splitlines():
        match = re.match(r"\s*[0-9a-f]+:\s+(\w+)\s*(.*?)\s*$", line)
        if match:
            rows.append((match[1], match[2].split("#", 1)[0].strip()))
    trans = audit(rows)
    a = next(i for i, row in enumerate(rows) if row[0] == "fnsave")
    b = next(i for i, row in enumerate(rows) if row[0] == "frstor")
    allowed = set(
        "testq sete movl orb jne cmpl je movabsq movq shrq andq andw andl shlq orq cmoveq orl jmp bsrq xorl addb movzbl subl movswl addl subq movzwl movw movd notl testl shrl bsrl shll setne leaq movss addq retq int3".split()
    )
    require(
        all(op in allowed for op, _ in rows[:a] + rows[b + 1 :]),
        "unknown wrapper op, call, or host FP arithmetic",
    )
    require(
        ("movw", "$0x37f, " + trans[12][1]) in rows[:a], "trig control must be PC64 RNE"
    )
    mutants = {
        "omit N64": trans[:10] + trans[12:],
        "omit N32": trans[:14] + trans[16:],
        "swap NaN multiply operands": trans[:18]
        + [("fmul", "%st, %st(1)")]
        + trans[19:],
        "call inside": trans[:18] + [("callq", "diagnostic")] + trans[18:],
        "missing restore": trans[:-1],
        "reuse accX": trans[:27] + [("fldt", trans[17][1])] + trans[28:],
    }
    for label, mutant in mutants.items():
        try:
            audit(mutant)
        except (RuntimeError, ValueError):
            print("PASS static negative: " + label, flush=True)
            continue
        raise RuntimeError("escaped mutation: " + label)
    print(
        "PASS exact acceleration machine transaction; no calls/host SSE arithmetic",
        flush=True,
    )
