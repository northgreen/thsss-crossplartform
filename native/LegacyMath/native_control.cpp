// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

static software_ext80 load_double(double value) {
    software_ext80 result;
    software_ext_load_double(value, &result);
    return result;
}

static software_ext80 boundary64(software_ext80 value) {
    double stored;
    software_ext_store_double(&value, &stored);
    return load_double(stored);
}

static software_ext80 load_constant(uint64_t bits) {
    double value;
    __builtin_memcpy(&value, &bits, sizeof(value));
    return load_double(value);
}

static void prepare_inputs(double radians, float delta, software_ext80 *inputs) {
    inputs[0] = load_double(radians);
    software_ext80 rate;
    software_ext_load_float(delta, &rate);
    inputs[1] = boundary64(rate); // Explicit Ctrl conv.r8, including NaN payload transport.
    inputs[2] = load_constant(UINT64_C(0x400921fb54442d18));
    inputs[3] = load_constant(UINT64_C(0x4066800000000000));
}

extern "C" int software_degree_increment(double radians, float delta, int precision, double *out) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    software_ext80 inputs[4], degree, sum, result;
    prepare_inputs(radians, delta, inputs);
    software_ext_div(inputs, inputs + 2, precision, &degree);
    software_ext_mul(&degree, inputs + 3, precision, &degree);
    degree = boundary64(degree);
    software_ext_add(&degree, inputs + 1, precision, &sum);
    sum = boundary64(sum);
    software_ext_mul(&sum, inputs + 2, precision, &result);
    software_ext_div(&result, inputs + 3, precision, &result);
    return software_ext_store_double(&result, out);
}

#if defined(__x86_64__)
extern "C" int hardware_degree_increment(double radians, float delta, int precision, double *out) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    const uint16_t control = precision == 24 ? 0x007f : 0x027f;
    software_ext80 inputs[4];
    prepare_inputs(radians, delta, inputs); // Integer codecs run before the one FPU transaction.
    alignas(16) unsigned char saved[108];
    double boundary, result;
    // ST0 is the left operand throughout, preserving the raw NaN propagation order.
    __asm__ volatile(
        "fnsave %[saved]\n\tfldcw %[control]\n\t"
        "fldt 32(%[in])\n\tfldt 0(%[in])\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 48(%[in])\n\tfxch %%st(1)\n\t"
        "fmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[boundary]\n\tfldl %[boundary]\n\t" // Degree getter N64.
        "fldt 16(%[in])\n\tfxch %%st(1)\n\t"
        "fadd %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[boundary]\n\tfldl %[boundary]\n\t" // Ctrl sum N64.
        "fldt 32(%[in])\n\tfxch %%st(1)\n\t"
        "fmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 48(%[in])\n\tfxch %%st(1)\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[result]\n\tfrstor %[saved]\n\t"
        : [saved] "=m" (saved), [boundary] "=m" (boundary), [result] "=m" (result)
        : [in] "r" (inputs), [control] "m" (control)
        : "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc");
    *out = result;
    return 0;
}
#endif

extern "C" int legacy_degree_increment(double radians, float delta, int precision, double *out) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2) return hardware_degree_increment(radians, delta, precision, out);
#endif
    return software_degree_increment(radians, delta, precision, out);
}

static software_ext80 boundary32(software_ext80 value) {
    float stored;
    software_ext_store_float(&value, &stored);
    software_ext80 result;
    software_ext_load_float(stored, &result);
    return result;
}

static void prepare_acceleration_inputs(double direction, float acceleration_x,
                                        float acceleration_y, software_ext80 *inputs) {
    inputs[0] = load_double(direction);
    inputs[1] = load_constant(UINT64_C(0x4066800000000000));
    inputs[2] = load_constant(UINT64_C(0x400921fb54442d18));
    software_ext_load_float(acceleration_x, inputs + 3);
    software_ext_load_float(acceleration_y, inputs + 4);
}

extern "C" int software_script_acceleration(double direction, float acceleration_x,
                                            float acceleration_y, int precision,
                                            float *out_x, float *out_y) {
    if (!out_x || !out_y || (precision != 24 && precision != 53)) return -1;
    software_ext80 inputs[5], argument, cosine, sine, x, y;
    prepare_acceleration_inputs(direction, acceleration_x, acceleration_y, inputs);
    software_ext_div(inputs, inputs + 1, precision, &argument);
    software_ext_mul(&argument, inputs + 2, precision, &argument);
    argument = boundary64(argument); // Cos/Sin independently N64 the unchanged num.
    software_ext_cos(&argument, &cosine);
    cosine = boundary32(cosine);
    software_ext_mul(inputs + 3, &cosine, precision, &x);
    software_ext_sin(&argument, &sine);
    sine = boundary32(sine);
    software_ext_mul(inputs + 4, &sine, precision, &y);
    float result_x, result_y;
    software_ext_store_float(&x, &result_x);
    software_ext_store_float(&y, &result_y);
    *out_x = result_x;
    *out_y = result_y;
    return 0;
}

#if defined(__x86_64__)
extern "C" int hardware_script_acceleration(double direction, float acceleration_x,
                                            float acceleration_y, int precision,
                                            float *out_x, float *out_y) {
    if (!out_x || !out_y || (precision != 24 && precision != 53)) return -1;
    software_ext80 inputs[5];
    prepare_acceleration_inputs(direction, acceleration_x, acceleration_y, inputs);
    const uint16_t control = precision == 24 ? 0x007f : 0x027f, trig_control = 0x037f;
    alignas(16) unsigned char saved[108];
    double argument;
    float trig, result_x, result_y;
    // Raw FCOS/FSIN at PC64 retain their unchanged-argument C2 range behavior.
    // N32 is essential; acceleration remains the left multiplication operand.
    __asm__ volatile(
        "fnsave %[saved]\n\tfldcw %[control]\n\t"
        "fldt 16(%[in])\n\tfldt 0(%[in])\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 32(%[in])\n\tfxch %%st(1)\n\t"
        "fmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[argument]\n\tfldl %[argument]\n\t"
        "fldcw %[trig_control]\n\tfcos\n\tfstps %[trig]\n\tflds %[trig]\n\t"
        "fldcw %[control]\n\tfldt 48(%[in])\n\t"
        "fmul %%st(1), %%st\n\tfstp %%st(1)\n\tfstps %[result_x]\n\t"
        "fldl %[argument]\n\tfldcw %[trig_control]\n\tfsin\n\t"
        "fstps %[trig]\n\tflds %[trig]\n\tfldcw %[control]\n\t"
        "fldt 64(%[in])\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstps %[result_y]\n\tfrstor %[saved]\n\t"
        : [saved] "=m" (saved), [argument] "=m" (argument), [trig] "=m" (trig),
          [result_x] "=m" (result_x), [result_y] "=m" (result_y)
        : [in] "r" (inputs), [control] "m" (control), [trig_control] "m" (trig_control)
        : "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc");
    *out_x = result_x;
    *out_y = result_y;
    return 0;
}
#endif

extern "C" int legacy_script_acceleration(double direction, float acceleration_x,
                                          float acceleration_y, int precision,
                                          float *out_x, float *out_y) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2)
        return hardware_script_acceleration(direction, acceleration_x, acceleration_y, precision, out_x, out_y);
#endif
    return software_script_acceleration(direction, acceleration_x, acceleration_y, precision, out_x, out_y);
}
