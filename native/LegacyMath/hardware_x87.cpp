// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

#if defined(__x86_64__)
static_assert(sizeof(software_ext80) == 16, "ext80 ABI must be 16 bytes");

// One asm block owns an empty stack, then restores even a full/dirty host stack.
// FNSAVE/FRSTOR do not touch MXCSR. No waiting instruction follows restoration.
#define X87_BEGIN "fnsave %[saved]\n\tfldcw %[control]\n\t"
#define X87_END "frstor %[saved]\n\t"
#define X87_CLOBBERS "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory"
#define X87_STATE \
    alignas(16) unsigned char saved[108];

// IEEE format transport shares the integer codec: saving x87 state here costs
// more than conversion itself. Arithmetic/trig below still execute native x87.
#define CODEC(name, type) \
extern "C" int hardware_ext_load_##name(type value, software_ext80 *out) { \
    return software_ext_load_##name(value, out); \
} \
extern "C" int hardware_ext_store_##name(const software_ext80 *value, type *out) { \
    return software_ext_store_##name(value, out); \
}
CODEC(float, float)
CODEC(double, double)

#define UNARY(name, instruction, parameters, guard, cw) \
extern "C" int hardware_ext_##name(const software_ext80 *value, parameters software_ext80 *out) { \
    if (!value || !out || (guard)) return -1; \
    software_ext80 result{}; \
    const uint16_t control = cw; \
    X87_STATE \
    __asm__ volatile(X87_BEGIN "fldt %[value]\n\t" instruction "\n\tfstpt %[result]\n\t" X87_END \
        : [saved] "=m" (saved), [result] "=m" (result) \
        : [control] "m" (control), [value] "m" (*value) : X87_CLOBBERS); \
    *out = result; return 0; \
}
#define PRECISION_PARAMETER int precision,
UNARY(sin, "fsin", , false, 0x037f)
UNARY(cos, "fcos", , false, 0x037f)
UNARY(sqrt, "fsqrt", PRECISION_PARAMETER, precision != 24 && precision != 53, precision == 24 ? 0x007f : 0x027f)

extern "C" int hardware_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out) {
    if (!y || !x || !out) return -1;
    software_ext80 result{};
    const uint16_t control = 0x037f;
    X87_STATE
    __asm__ volatile(X87_BEGIN "fldt %[y]\n\tfldt %[x]\n\tfpatan\n\tfstpt %[result]\n\t" X87_END
        : [saved] "=m" (saved), [result] "=m" (result)
        : [control] "m" (control), [y] "m" (*y), [x] "m" (*x) : X87_CLOBBERS);
    *out = result; return 0;
}

#define BINARY(name, instruction) \
extern "C" int hardware_ext_##name(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out) { \
    if (!a || !b || !out || (precision != 24 && precision != 53)) return -1; \
    software_ext80 result{}; \
    const uint16_t control = precision == 24 ? 0x007f : 0x027f; \
    X87_STATE \
    __asm__ volatile(X87_BEGIN "fldt %[b]\n\tfldt %[a]\n\t" instruction " %%st(1), %%st\n\tfstpt %[result]\n\t" X87_END \
        : [saved] "=m" (saved), [result] "=m" (result) \
        : [control] "m" (control), [a] "m" (*a), [b] "m" (*b) : X87_CLOBBERS); \
    *out = result; return 0; \
}
BINARY(add, "fadd")
BINARY(sub, "fsub")
BINARY(mul, "fmul")
BINARY(div, "fdiv")
#endif
