// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

#define MOTION_PARAMETERS float ox1, int left1, float oy1, int top1, \
    float velocity_x, double direction_x, float scale_x, \
    float ox2, int left2, float oy2, int top2, \
    float velocity_y, double direction_y, float scale_y, \
    int set_left, int set_top, int precision, float *out_x, float *out_y
#define MOTION_ARGUMENTS ox1, left1, oy1, top1, velocity_x, direction_x, scale_x, \
    ox2, left2, oy2, top2, velocity_y, direction_y, scale_y, \
    set_left, set_top, precision, out_x, out_y

static software_ext80 load_float(float value) {
    software_ext80 result;
    software_ext_load_float(value, &result);
    return result;
}

static software_ext80 load_double(double value) {
    software_ext80 result;
    software_ext_load_double(value, &result);
    return result;
}

static software_ext80 boundary32(software_ext80 value) {
    float stored;
    software_ext_store_float(&value, &stored);
    return load_float(stored);
}

static software_ext80 boundary64(software_ext80 value) {
    double stored;
    software_ext_store_double(&value, &stored);
    return load_double(stored);
}

static software_ext80 load_offset(int value) {
    software_ext80 integer{};
    const uint64_t magnitude = value < 0 ? uint64_t(-int64_t(value)) : uint64_t(value);
    if (magnitude) {
        const int shift = __builtin_clzll(magnitude);
        integer.significand = magnitude << shift;
        integer.signExp = uint16_t(16383 + 63 - shift) | (value < 0 ? 0x8000 : 0);
    }
    return boundary32(integer); // Int32 -> binary32 RNE, independent of host CW/PC.
}

// All IEEE inputs are decoded/quieted before the hardware transaction.
#define MOTION_INPUTS \
    load_float(ox1), load_offset(left1), load_float(oy1), load_offset(top1), \
    load_float(velocity_x), load_double(direction_x), load_float(scale_x), \
    load_float(ox2), load_offset(left2), load_float(oy2), load_offset(top2), \
    load_float(velocity_y), load_double(direction_y), load_float(scale_y), \
    load_offset(set_left), load_offset(set_top)

static void getter(const software_ext80 *inputs, int precision, software_ext80 *point) {
    software_ext80 x, y;
    software_ext_add(inputs, inputs + 1, precision, &x);
    software_ext_add(inputs + 2, inputs + 3, precision, &y);
    point[0] = boundary32(x);
    point[1] = boundary32(y);
}

static software_ext80 move_coordinate(software_ext80 position, const software_ext80 *inputs,
                                     int precision, bool horizontal) {
    auto p = boundary64(position), velocity = boundary64(inputs[0]);
    auto direction = boundary64(inputs[1]), scale = boundary64(inputs[2]);
    software_ext80 trig, displacement;
    if (horizontal) software_ext_cos(&direction, &trig);
    else software_ext_sin(&direction, &trig);
    software_ext_mul(&velocity, &trig, precision, &displacement);
    software_ext_mul(&displacement, &scale, precision, &displacement);
    software_ext_add(&p, &displacement, precision, &p);
    return boundary32(p);
}

extern "C" int software_motion_step(MOTION_PARAMETERS) {
    if (!out_x || !out_y || (precision != 24 && precision != 53)) return -1;
    const software_ext80 inputs[] = {MOTION_INPUTS};
    software_ext80 first[2], second[2], u, v;
    getter(inputs, precision, first); // Keep the unused Y and its PointF store.
    const auto x = move_coordinate(first[0], inputs + 4, precision, true);
    getter(inputs + 7, precision, second); // Keep the unused X and its PointF store.
    const auto y = move_coordinate(second[1], inputs + 11, precision, false);
    software_ext_sub(&x, inputs + 14, precision, &u);
    software_ext_sub(&y, inputs + 15, precision, &v);
    float result_x, result_y;
    software_ext_store_float(&u, &result_x);
    software_ext_store_float(&v, &result_y);
    *out_x = result_x;
    *out_y = result_y;
    return 0;
}

#if defined(__x86_64__)
// ST0 is always the left operand: NaN propagation must retain IL operand order.
#define MOTION_GETTER(OX, LEFT, OY, TOP, X, Y) \
    "fldt " LEFT "(%[in])\n\tfldt " OX "(%[in])\n\t" \
    "fadd %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt " TOP "(%[in])\n\tfldt " OY "(%[in])\n\t" \
    "fadd %%st(1), %%st\n\tfstp %%st(1)\n\tfxch %%st(1)\n\t" \
    "fstps " X "(%[point])\n\tfstps " Y "(%[point])\n\t"
#define MOTION_COORDINATE(POS, VEL, DIR, SCALE, TRIG, RESULT) \
    "flds " POS "(%[point])\n\tfstpl %[boundary]\n\tfldl %[boundary]\n\t" \
    "fldt " VEL "(%[in])\n\tfstpl %[boundary]\n\tfldl %[boundary]\n\t" \
    "fldt " DIR "(%[in])\n\tfstpl %[boundary]\n\tfldl %[boundary]\n\t" \
    "fldcw %[trig_control]\n\t" TRIG "\n\tfldcw %[control]\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt " SCALE "(%[in])\n\tfstpl %[boundary]\n\tfldl %[boundary]\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fstps " RESULT "(%[point])\n\t"

extern "C" int hardware_motion_step(MOTION_PARAMETERS) {
    if (!out_x || !out_y || (precision != 24 && precision != 53)) return -1;
    const software_ext80 inputs[] = {MOTION_INPUTS};
    const uint16_t control = precision == 24 ? 0x007f : 0x027f, trig_control = 0x037f;
    alignas(16) unsigned char saved[108];
    float point[6], result_x, result_y;
    double boundary;
    // One save/restore covers both getters, both movements and the setter; <=3 registers.
    __asm__ volatile(
        "fnsave %[saved]\n\tfldcw %[control]\n\t"
        MOTION_GETTER("0", "16", "32", "48", "0", "4")
        MOTION_COORDINATE("0", "64", "80", "96", "fcos", "16")
        MOTION_GETTER("112", "128", "144", "160", "8", "12")
        MOTION_COORDINATE("12", "176", "192", "208", "fsin", "20")
        "fldt 224(%[in])\n\tflds 16(%[point])\n\t"
        "fsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 240(%[in])\n\tflds 20(%[point])\n\t"
        "fsub %%st(1), %%st\n\tfstp %%st(1)\n\tfxch %%st(1)\n\t"
        "fstps %[result_x]\n\tfstps %[result_y]\n\tfrstor %[saved]\n\t"
        : [saved] "=m" (saved), [boundary] "=m" (boundary),
          [result_x] "=m" (result_x), [result_y] "=m" (result_y), "=m" (point)
        : [in] "r" (inputs), [point] "r" (point), [control] "m" (control),
          [trig_control] "m" (trig_control)
        : "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc");
    *out_x = result_x;
    *out_y = result_y;
    return 0;
}
#endif

extern "C" int legacy_motion_step(MOTION_PARAMETERS) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2) return hardware_motion_step(MOTION_ARGUMENTS);
#endif
    return software_motion_step(MOTION_ARGUMENTS);
}
