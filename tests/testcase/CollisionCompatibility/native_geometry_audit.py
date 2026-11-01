"""2026 x86-64 objdump audit; deliberately rejects unsupported compiler shapes."""

import re
import shutil
import subprocess

INTEGER = {
    "mov", "movb", "movw", "movl", "movq", "movabs", "movd", "movdqa", "movdqu", "movzbl", "movzwl", "movslq",
    "pxor", "pand", "por", "push", "pop", "lea", "add", "sub", "inc", "dec", "neg", "not", "and", "or", "xor", "test", "cmp",
    "shl", "shr", "sar", "sal", "imul", "mul", "bsr", "bsf", "bt", "sbb", "adc", "cltq", "cdqe", "ret", "int3", "nop", "nopl", "nopw"
}
MOVES = {"movss", "movsd", "movaps", "movups"}
JUMPS = {
    "jmp", "je", "jne", "jz", "jnz", "ja", "jae", "jb", "jbe", "jg", "jge", "jl", "jle", "jo", "jno", "js", "jns", "jp", "jnp", "jc", "jnc", "jpe", "jpo"
}
PREFIX = (
    {m for m in INTEGER if m.startswith("mov")}
    | MOVES
    | {"push", "pop", "lea", "test", "cmp", "ret", "nop", "nopl", "nopw"}
)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def parse(body):
    rows = []
    for address, text in re.findall(r"^\s*([0-9a-f]+):\s+([^\n]+)", body, re.MULTILINE):
        # GNU objdump prints alignment NOPs with these prefixes (not opcodes).
        text = re.sub(r"^(?:(?:data16|cs)\s+)+(?=nop)", "", text.strip())
        parts = text.split(None, 1)
        rows.append(
            (
                int(address, 16),
                parts[0],
                parts[1].split("#")[0].strip() if len(parts) == 2 else "",
            )
        )
    require(rows, "Empty disassembly")
    require(len({a for a, _, _ in rows}) == len(rows), "Duplicate instruction address")
    return rows


def integer(mnemonic):
    return (
        mnemonic in INTEGER or mnemonic in JUMPS or mnemonic.startswith(("set", "cmov"))
    )


def successors(rows):
    indices = {a: i for i, (a, _, _) in enumerate(rows)}
    edges = []
    for i, (_, mnemonic, operands) in enumerate(rows):
        padding_end = i == len(rows) - 1 and mnemonic in {"nop", "nopl", "nopw", "int3"}
        following = [] if mnemonic == "ret" or padding_end else [i + 1]
        if mnemonic in JUMPS:
            target = re.match(r"^([0-9a-f]+)\s", operands + " ")
            if target is None:
                raise RuntimeError("Indirect/unsupported jump")
            address = int(target[1], 16)
            require(address in indices, "Jump leaves audited function")
            following = [indices[address]] + ([] if mnemonic == "jmp" else [i + 1])
        require(all(j < len(rows) for j in following), "Unterminated function")
        edges.append(following)
    return edges


def reachable(edges, start, stop=None):
    seen, pending = set(), [start]
    while pending:
        i = pending.pop()
        if i in seen or i == stop:
            continue
        seen.add(i)
        pending.extend(edges[i])
    return seen


def bookkeeping(mnemonic, operands):
    compact = operands.replace(" ", "")
    return (
        mnemonic in {"add", "sub"} and re.fullmatch(r"\$0x[0-9a-f]+,%rsp", compact)
    ) or (mnemonic == "xor" and compact == "%eax,%eax")


def reject_return(rows, edges, start):
    seen, zero_status, zero_output = set(), False, False
    i = start
    while i not in seen:
        seen.add(i)
        _, mnemonic, operands = rows[i]
        compact = operands.replace(" ", "")
        store = mnemonic == "movl" and re.fullmatch(r"\$0x0,\(%r[a-z0-9]+\)", compact)
        require(
            store
            or bookkeeping(mnemonic, operands)
            or mnemonic in {"jmp", "pop", "ret", "nop", "nopl", "nopw"},
            "Reject path executes arithmetic/FPU/call or unsupported epilogue: "
            + mnemonic,
        )
        zero_output |= bool(store)
        zero_status |= mnemonic == "xor" and compact == "%eax,%eax"
        require(
            not (mnemonic == "pop" and compact in {"%rax", "%eax"}),
            "Reject overwrites zero status",
        )
        if mnemonic == "ret":
            require(
                zero_status and zero_output,
                "Reject must return zero status and store false",
            )
            return
        require(len(edges[i]) == 1, "Unsupported reject branch")
        i = edges[i][0]
    raise RuntimeError("Reject path loops instead of returning")


def transaction(rows, edges, call):
    pending, seen, ran = [(0, False)], set(), False
    while pending:
        i, active = pending.pop()
        if (i, active) in seen:
            continue
        seen.add((i, active))
        _, mnemonic, operands = rows[i]
        if mnemonic == "fnsave":
            require(not active, "Nested FNSAVE")
            active, ran = True, True
        elif mnemonic == "frstor":
            require(active, "FRSTOR without FNSAVE")
            active = False
        elif mnemonic.startswith("f"):
            require(active, "x87 instruction without dominating FNSAVE: " + mnemonic)
        elif mnemonic == "call":
            require(not active, "Call inside FPU transaction")
            require(
                i == call
                or re.search(r"<(?:software_ext_(?:load|store)_float(?:@plt)?|"
                             r"extF80_to_f32\(extFloat80M, softfloat_status_t\*\))>", operands),
                "Unexpected arithmetic call: " + operands,
            )
        else:
            require(
                integer(mnemonic) or mnemonic in MOVES or mnemonic == "xorps",
                "Unsupported/host FP instruction: " + mnemonic,
            )
        if mnemonic == "ret":
            require(not active, "Return without FRSTOR")
        require(edges[i] or mnemonic == "ret", "Reachable non-return terminal")
        pending.extend((j, active) for j in edges[i])
    require(ran, "No reachable FPU transaction")


def prove(rows, helper):
    require(all(integer(m) for _, m, _ in helper), "Reject helper is not integer-only")
    helper_edges = successors(helper)
    require(all(helper_edges[i] or helper[i][1] == "ret"
                for i in reachable(helper_edges, 0)), "Helper has non-return terminal")
    edges = successors(rows)
    calls = [
        i
        for i, (_, m, a) in enumerate(rows)
        if m == "call" and "<circle_integer_reject(" in a
    ]
    require(len(calls) == 1, "Expected one canonical shared reject call")
    call = calls[0]
    target = re.match(r"([0-9a-f]+)\s", rows[call][2])
    require(
        target is not None and int(target[1], 16) == helper[0][0],
        "Reject call does not target canonical helper",
    )
    require(call in reachable(edges, 0), "Unreachable reject call")
    for i in reachable(edges, 0, stop=call):
        _, m, a = rows[i]
        require(
            m in PREFIX or m in JUMPS or bookkeeping(m, a),
            "Unguarded codec/arithmetic/FPU/call: " + m,
        )
    require(call + 2 < len(rows), "Missing Boolean guard branch")
    # C++ bool ABI returns 0/1; prove control flow, not the helper's arithmetic.
    _, test, operands = rows[call + 1]
    branch = rows[call + 2][1]
    require(
        test == "test"
        and operands.replace(" ", "") in {"%al,%al", "%eax,%eax"}
        and branch in {"je", "jne", "jz", "jnz"},
        "Unsupported Boolean return test",
    )
    reject = edges[call + 2][0 if branch in {"jne", "jnz"} else 1]
    reject_return(rows, edges, reject)
    saves = [a for _, m, a in rows if m == "fnsave"]
    restores = [a for _, m, a in rows if m == "frstor"]
    require(
        len(saves) == 1 and restores == saves, "Expected one matching FNSAVE/FRSTOR"
    )
    transaction(rows, edges, call)
    return len(helper)


def audit_assembly(assembly):
    functions = re.findall(
        r"^[0-9a-f]+ <([^\n]+)>:\n(.*?)(?=^[0-9a-f]+ <|\Z)", assembly, re.MULTILINE | re.DOTALL
    )
    helpers = [
        body for name, body in functions if name.startswith("circle_integer_reject(")
    ]
    entries = [body for name, body in functions if name == "hardware_circle_hit"]
    require(
        len(helpers) == len(entries) == 1,
        "Expected unique hardware entry and shared helper",
    )
    return prove(parse(entries[0]), parse(helpers[0]))


def audit_integer_reject(library):
    objdump = shutil.which("objdump")
    if objdump is None:
        raise RuntimeError("Missing objdump")
    assembly = subprocess.check_output(
        [objdump, "-d", "-C", "--no-show-raw-insn", str(library)], text=True
    )
    count = audit_assembly(assembly)
    print(
        f"PASS x64 CFG machine audit: shared helper {count} integer instructions; "
        "guard dominates FNSAVE; true reject returns before codecs/FPU; "
        "call-free FPU transaction restored before return",
        flush=True,
    )
