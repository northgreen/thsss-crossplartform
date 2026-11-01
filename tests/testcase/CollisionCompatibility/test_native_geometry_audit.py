"""2026 CFG regression controls; body fixtures need neither objdump nor a kernel."""

import os
import re
import subprocess
import unittest
from pathlib import Path

from native_geometry_audit import audit_assembly, parse, prove

HELPER = """
100: movd %xmm0,%eax
104: and $0x7fffffff,%eax
108: setne %al
10c: ret
110: data16 cs nopw 0x0(%rax,%rax,1)
"""
ENTRY = """
200: mov $0xffffffff,%eax
204: test %rdx,%rdx
208: je 2f0 <hardware_circle_hit+0xf0>
20c: push %rbx
210: sub $0x20,%rsp
214: mov %rdx,%rbx
218: cmp $0x35,%esi
21c: je 228 <hardware_circle_hit+0x28>
220: cmp $0x18,%esi
224: jne 2e0 <hardware_circle_hit+0xe0>
228: mov %esi,%ebp
22c: call 100 <circle_integer_reject(float)>
230: test %al,%al
234: je 248 <hardware_circle_hit+0x48>
238: movl $0x0,(%rbx)
23c: jmp 2d0 <hardware_circle_hit+0xd0>
248: lea 0x10(%rsp),%rdi
24c: call 500 <software_ext_load_float@plt>
250: movss %xmm0,(%rsp)
254: fnsave 0x10(%rsp)
258: fldcw (%rsp)
25c: fldt (%rsp)
260: fsqrt
264: frstor 0x10(%rsp)
268: movl $0x1,(%rbx)
26c: jmp 2d0 <hardware_circle_hit+0xd0>
2d0: xor %eax,%eax
2e0: add $0x20,%rsp
2e4: pop %rbx
2f0: ret
"""


def assembly(entry=ENTRY, helper=HELPER):
    return (
        "00000100 <circle_integer_reject(float)>:\n"
        + helper
        + "00000200 <hardware_circle_hit>:\n"
        + entry
    )


def mutation(rows, index, mnemonic, operands):
    changed = rows.copy()
    changed[index] = (rows[index][0], mnemonic, operands)
    return changed


def negative_controls(rows, helper):
    call = next(
        i
        for i, (_, m, a) in enumerate(rows)
        if m == "call" and "<circle_integer_reject(" in a
    )
    save = next(i for i, (_, m, _) in enumerate(rows) if m == "fnsave")
    restore = next(i for i, (_, m, _) in enumerate(rows) if m == "frstor")
    save_target = f"{rows[save][0]:x} <hardware_circle_hit>"
    valid_precision = next(i for i in range(1, call) if rows[i][1] == "je"
                           and rows[i - 1][1] == "cmp")
    # Keep the original helper call in the body: bypass must fail by reachability,
    # not by the trivial 'helper call missing' check.
    prefix = call - 1
    return {
        "fnsave before coarse call": (
            mutation(rows, prefix, "fnsave", "(%rsp)"),
            helper,
        ),
        "normal args bypass coarse call": (
            mutation(rows, prefix, "jmp", save_target),
            helper,
        ),
        "PC53 bypass while PC24 still reaches helper": (
            mutation(rows, valid_precision, "je", save_target), helper,
        ),
        "true reject enters fallback": (
            mutation(
                rows,
                call + 2,
                "jne" if rows[call + 2][1] == "je" else "je",
                rows[call + 2][2],
            ),
            helper,
        ),
        "codec before helper": (
            mutation(rows, prefix, "call", "500 <software_ext_load_float@plt>"),
            helper,
        ),
        "helper FP arithmetic": (rows, mutation(helper, 0, "addss", "%xmm1,%xmm0")),
        "helper FP conversion": (rows, mutation(helper, 0, "cvtss2sd", "%xmm1,%xmm0")),
        "helper x87 status": (rows, mutation(helper, 0, "fnstsw", "%ax")),
        "helper call": (rows, mutation(helper, 0, "call", "500 <other>")),
        "FPU bypasses save": (mutation(rows, save, "nop", ""), helper),
        "x87 before existing save": (mutation(rows, save - 1, "fld1", ""), helper),
        "FPU returns unrestored": (mutation(rows, restore, "nop", ""), helper),
        "call inside FPU transaction": (
            mutation(rows, save + 1, "call", "500 <software_ext_load_float@plt>"),
            helper,
        ),
        "wrong save area": (mutation(rows, restore, "frstor", "0x777(%rsp)"), helper),
    }


class GeometryAuditTests(unittest.TestCase):
    def assert_controls(self, rows, helper, label=None):
        prove(rows, helper)
        for name, (changed, changed_helper) in negative_controls(rows, helper).items():
            with self.subTest(control=name):
                with self.assertRaises(RuntimeError):
                    prove(changed, changed_helper)
                if label:
                    print(f"PASS binary mutation rejected: {label}: {name}", flush=True)

    def test_non_lto_and_controls(self):
        self.assertEqual(audit_assembly(assembly()), 5)
        self.assert_controls(parse(ENTRY), parse(HELPER))

    def test_lto_inlined_codec_and_controls(self):
        entry = ENTRY.replace(
            "call 500 <software_ext_load_float@plt>", "and $0x7fffff,%eax"
        )
        self.assertEqual(audit_assembly(assembly(entry)), 5)
        self.assert_controls(parse(entry), parse(HELPER))

    def test_eax_boolean_and_true_taken_edge(self):
        entry = ENTRY.replace("test %al,%al", "test %eax,%eax").replace(
            "je 248 <hardware_circle_hit+0x48>", "jne 238 <hardware_circle_hit+0x38>"
        )
        # Relocate the false-edge dispatch between branch and reject store.
        entry = entry.replace(
            "238: movl", "236: jmp 248 <hardware_circle_hit+0x48>\n238: movl"
        )
        audit_assembly(assembly(entry))

    def test_high_address_argument_return_before_fnsave_text(self):
        entry = ENTRY.replace("je 2f0", "je 244").replace(
            "248: lea", "244: ret\n248: lea")
        audit_assembly(assembly(entry))

    def test_reject_must_store_false_and_return_zero(self):
        for old, new in [
            ("xor %eax,%eax", "mov $0x1,%eax"),
            ("movl $0x0,(%rbx)", "nop"),
        ]:
            with self.subTest(old=old), self.assertRaises(RuntimeError):
                audit_assembly(assembly(ENTRY.replace(old, new)))

    def test_reject_codec_bookkeeping_is_not_allowed(self):
        for instruction in ["shr $0x17,%eax", "and $0x7fffff,%eax", "xor %ecx,%ecx"]:
            with self.subTest(instruction=instruction), self.assertRaises(RuntimeError):
                audit_assembly(
                    assembly(
                        ENTRY.replace("238: movl $0x0,(%rbx)", "238: " + instruction)
                    )
                )

    def test_canonical_helper_target_and_unique_symbol(self):
        with self.assertRaises(RuntimeError):
            audit_assembly(assembly(ENTRY.replace("call 100", "call 104")))
        with self.assertRaises(RuntimeError):
            audit_assembly(
                assembly() + "00000600 <circle_integer_reject(other)>:\n600: ret\n"
            )

    def test_unsupported_jump_and_boolean_fail_closed(self):
        for old, new in [
            ("jmp 2d0 <hardware_circle_hit+0xd0>", "jmp *%rax"),
            ("test %al,%al", "test %cl,%cl"),
            ("jmp 2d0 <hardware_circle_hit+0xd0>", "jmp 999 <outside>"),
        ]:
            with self.subTest(new=new), self.assertRaises(RuntimeError):
                audit_assembly(assembly(ENTRY.replace(old, new)))

    @unittest.skipUnless(
        os.environ.get("NATIVE_GEOMETRY_AUDIT_LIBRARIES"),
        "Set NATIVE_GEOMETRY_AUDIT_LIBRARIES for actual binary controls",
    )
    def test_actual_binary_controls(self):
        for filename in os.environ["NATIVE_GEOMETRY_AUDIT_LIBRARIES"].split(os.pathsep):
            with self.subTest(library=filename):
                text = subprocess.check_output(
                    ["objdump", "-d", "-C", "--no-show-raw-insn", str(Path(filename))],
                    text=True,
                )
                audit_assembly(text)
                functions = dict(
                    re.findall(
                        r"^[0-9a-f]+ <([^\n]+)>:\n(.*?)(?=^[0-9a-f]+ <|\Z)",
                        text,
                        re.MULTILINE | re.DOTALL,
                    )
                )
                helper = next(
                    body
                    for name, body in functions.items()
                    if name.startswith("circle_integer_reject(")
                )
                self.assert_controls(
                    parse(functions["hardware_circle_hit"]), parse(helper), filename
                )


if __name__ == "__main__":
    unittest.main()
