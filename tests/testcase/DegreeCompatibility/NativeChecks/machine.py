# SPDX-License-Identifier: LGPL-2.1-or-later
# Copyright (C) 2026 THSSS contributors.
"""Strict supplied x64 artifact transaction and diagnostic mutation gate."""

import re
import subprocess


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


EXPECTED = "fnsave fldcw fldt fldt fdiv fstp fldt fxch fmul fstp fstpl fldl fldt fxch fadd fstp fstpl fldl fldt fxch fmul fstp fldt fxch fdiv fstp fstpl frstor".split()


def audit(rows):
    ops = [r[0] for r in rows]
    require(
        ops.count("fnsave") == ops.count("frstor") == 1, "one save/restore required"
    )
    a, b = ops.index("fnsave"), ops.index("frstor")
    trans = rows[a : b + 1]
    require([r[0] for r in trans] == EXPECTED, f"unknown transaction shape: {trans}")
    require(trans[0][1] == trans[-1][1], "save/restore address differs")
    for index, (op, arg) in enumerate(trans):
        if op in {"fadd", "fmul", "fdiv"}:
            require(arg.replace(" ", "") == "%st(1),%st", "reversed arithmetic")
        if op == "fstp":
            require(arg == "%st(1)", "unexpected pop")
        if op == "fstpl" and index != len(trans) - 2:
            require(trans[index + 1] == ("fldl", arg), "missing N64 reload")
    require(sum(op == "fstpl" for op, _ in trans) == 3, "getter/sum/final stores")
    return trans


def machine_gate(library, output):
    text = subprocess.check_output(
        [
            "llvm-objdump",
            "-d",
            "--no-show-raw-insn",
            "--disassemble-symbols=hardware_degree_increment",
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
    # Explicit compiler shape: integer codecs, bitwise XMM transport only; no
    # host arithmetic or conversion before/after the protected x87 transaction.
    allowed = set(
        "movl testq je pushq subq movq cmpl jne movabsq xorl setne shll orl movw shrq andq andw andl shlq orq cmoveq jmp bsrq addb movzbl subl movswl addl movzwl movd notl testl shrl bsrl leaq callq movsd addq popq retq int3".split()
    )
    a = next(i for i, row in enumerate(rows) if row[0] == "fnsave")
    b = next(i for i, row in enumerate(rows) if row[0] == "frstor")
    outside = rows[:a] + rows[b + 1 :]
    require(
        all(op in allowed for op, _ in outside),
        f"unknown codec/wrapper instruction: {outside}",
    )
    calls = [(i, arg) for i, (op, arg) in enumerate(rows) if op == "callq"]
    require(
        len(calls) == 1
        and calls[0][0] < a
        and "<_Z13extF80_to_f6411extFloat80MP18softfloat_status_t>" in calls[0][1],
        "expected only pretransaction software delta N64 codec call",
    )
    mutants = {
        "omit getter N64": trans[:10] + trans[12:],
        "omit sum N64": trans[:16] + trans[18:],
        "call inside transaction": trans[:5] + [("callq", "diagnostic")] + trans[5:],
        "reverse add": [
            (op, "%st,%st(1)" if op == "fadd" else arg) for op, arg in trans
        ],
        "missing restore": trans[:-1],
    }
    for label, mutant in mutants.items():
        try:
            audit(mutant)
        except (RuntimeError, ValueError):
            continue
        raise RuntimeError("escaped mutation: " + label)
    print(
        "PASS exact machine graph: one transaction, div/mul/add/mul/div, getter/sum/final stores; predecoded delta N64; 5 static mutations rejected",
        flush=True,
    )
