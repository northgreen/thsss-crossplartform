// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

#define LASER_PARAMETERS float px, float py, float tx1, float tx2, float ty1, float ty2, \
    int target_region, float target_scale, float radius, float scale_length, int precision, int *out
#define LASER_ARGUMENTS px, py, tx1, tx2, ty1, ty2, target_region, target_scale, radius, scale_length, precision, out

static uint32_t laser_float_bits(float value) {
    uint32_t bits;
    __builtin_memcpy(&bits, &value, sizeof(bits));
    return bits;
}

// Bounded magnitudes imply shifts in [3,23]; subnormals truncate to zero.
static uint32_t laser_truncated_magnitude(uint32_t magnitude) {
    const uint32_t exponent = magnitude >> 23;
    if (exponent < 127) return 0;
    return ((magnitude & 0x7fffff) | 0x800000) >> (150 - exponent);
}

static int32_t laser_truncated_coordinate(uint32_t bits) {
    const int32_t magnitude = int32_t(laser_truncated_magnitude(bits & 0x7fffffff));
    return (bits >> 31) ? -magnitude : magnitude;
}

static uint32_t laser_ceiling(uint32_t bits) {
    const uint32_t magnitude = bits & 0x7fffffff;
    const uint32_t integral = laser_truncated_magnitude(magnitude);
    if (!magnitude) return 0;
    const uint32_t exponent = magnitude >> 23;
    if (exponent < 127) return 1;
    return integral + ((magnitude & ((uint32_t(1) << (150 - exponent)) - 1)) != 0);
}

// Deliberately local circle-formula duplicate: the laser proof requires stable
// target captures and has no binary64 store on the sqrt return.
static __attribute__((noinline)) bool laser_integer_reject(
    float tx1, float tx2, float ty1, float ty2, float px, float py,
    float radius, float scale_length, int target_region, float target_scale) {
    constexpr uint32_t DomainLimit = 1u << 20;
    constexpr uint32_t DomainLimitBits = 0x49800000;
    const uint32_t x = laser_float_bits(tx1), y = laser_float_bits(ty1);
    if (x != laser_float_bits(tx2) || y != laser_float_bits(ty2)) return false;
    const uint32_t point_x = laser_float_bits(px), point_y = laser_float_bits(py);
    const uint32_t coordinates[] = {x, y, point_x, point_y};
    for (uint32_t bits : coordinates)
        if ((bits & 0x7fffffff) > DomainLimitBits) return false;
    const uint32_t r = laser_float_bits(radius), s = laser_float_bits(scale_length);
    const uint32_t t = laser_float_bits(target_scale);
    const uint32_t nonnegative[] = {r, s, t};
    for (uint32_t bits : nonnegative)
        if ((bits & 0x7fffffff) > DomainLimitBits || ((bits >> 31) && (bits & 0x7fffffff))) return false;
    if (target_region < 0 || uint32_t(target_region) > DomainLimit) return false;
    const uint64_t upper = uint64_t(laser_ceiling(r)) * laser_ceiling(s) +
        uint64_t(target_region) * laser_ceiling(t);
    if (upper > DomainLimit) return false;
    const int64_t dx = int64_t(laser_truncated_coordinate(x)) - laser_truncated_coordinate(point_x);
    const int64_t dy = int64_t(laser_truncated_coordinate(y)) - laser_truncated_coordinate(point_y);
    // PC24/53 total adverse error, including truncation, is <25/8 pixels.
    // Margin 16 is conservative; negative factors and all other unproved inputs
    // fall through to the unchanged ordered graph, never to an acceptance test.
    const int64_t bound = int64_t(upper) + 16;
    return dx > bound || dx < -bound || dy > bound || dy < -bound;
}

static software_ext80 load_float(float value) {
    software_ext80 result;
    software_ext_load_float(value, &result);
    return result;
}

static software_ext80 region_single(int value) {
    software_ext80 integer{};
    const uint64_t magnitude = value < 0 ? uint64_t(-int64_t(value)) : uint64_t(value);
    if (magnitude) {
        const int shift = __builtin_clzll(magnitude);
        integer.significand = magnitude << shift;
        integer.signExp = uint16_t(16383 + 63 - shift) | (value < 0 ? 0x8000 : 0);
    }
    float stored;
    software_ext_store_float(&integer, &stored);
    return load_float(stored);
}

static software_ext80 boundary64(software_ext80 value) {
    double stored;
    software_ext_store_double(&value, &stored);
    software_ext_load_double(stored, &value);
    return value;
}

static bool ordered_less(const software_ext80 &a, const software_ext80 &b) {
    if (((a.signExp & 0x7fff) == 0x7fff && (a.significand << 1)) ||
        ((b.signExp & 0x7fff) == 0x7fff && (b.significand << 1))) return false;
    if (!a.significand && !b.significand) return false;
    const bool negative = (a.signExp & 0x8000) != 0;
    if (negative != ((b.signExp & 0x8000) != 0)) return negative;
    if (a.signExp == b.signExp && a.significand == b.significand) return false;
    const bool smaller = (a.signExp & 0x7fff) < (b.signExp & 0x7fff) ||
        (a.signExp == b.signExp && a.significand < b.significand);
    return negative ? !smaller : smaller;
}

// Decode/quiet IEEE inputs and round the integer to Single without host FP state.
#define LASER_INPUTS load_float(px), load_float(py), load_float(tx1), load_float(tx2), \
    load_float(ty1), load_float(ty2), region_single(target_region), load_float(target_scale), \
    load_float(radius), load_float(scale_length)

extern "C" int software_laser_point_hit(LASER_PARAMETERS) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    if (laser_integer_reject(tx1, tx2, ty1, ty2, px, py, radius, scale_length, target_region, target_scale)) {
        *out = 0;
        return 0;
    }
    const software_ext80 inputs[] = {LASER_INPUTS};
    software_ext80 x1, x2, y1, y2, distance, extent, right;
    software_ext_sub(inputs, inputs + 2, precision, &x1);
    software_ext_sub(inputs, inputs + 3, precision, &x2);
    software_ext_mul(&x1, &x2, precision, &x1);
    software_ext_sub(inputs + 1, inputs + 4, precision, &y1);
    software_ext_sub(inputs + 1, inputs + 5, precision, &y2);
    software_ext_mul(&y1, &y2, precision, &y1);
    software_ext_add(&x1, &y1, precision, &distance);
    distance = boundary64(distance);
    software_ext_sqrt(&distance, precision, &distance);
    software_ext_mul(inputs + 6, inputs + 7, precision, &extent);
    extent = boundary64(extent);
    software_ext_sub(&distance, &extent, precision, &distance);
    software_ext_mul(inputs + 8, inputs + 9, precision, &right);
    right = boundary64(right);
    *out = ordered_less(distance, right) ? 1 : 0;
    return 0;
}

#if defined(__x86_64__)
extern "C" int hardware_laser_point_hit(LASER_PARAMETERS) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    if (laser_integer_reject(tx1, tx2, ty1, ty2, px, py, radius, scale_length, target_region, target_scale)) {
        *out = 0;
        return 0;
    }
    const software_ext80 inputs[] = {LASER_INPUTS};
    const uint16_t control = precision == 24 ? 0x007f : 0x027f;
    alignas(16) unsigned char saved[108];
    double argument, extent, right;
    unsigned char less, ordered;
    // ST0 is the left operand of every binary operation, including NaN propagation.
    // Only the sqrt argument and the two threshold products have binary64 stores.
    __asm__ volatile(
        "fnsave %[saved]\n\tfldcw %[control]\n\t"
        "fldt 32(%[in])\n\tfldt 0(%[in])\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 48(%[in])\n\tfldt 0(%[in])\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 64(%[in])\n\tfldt 16(%[in])\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 80(%[in])\n\tfldt 16(%[in])\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fxch %%st(1)\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[argument]\n\tfldl %[argument]\n\tfsqrt\n\t"
        "fldt 112(%[in])\n\tfldt 96(%[in])\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[extent]\n\tfldl %[extent]\n\tfxch %%st(1)\n\t"
        "fsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt 144(%[in])\n\tfldt 128(%[in])\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[right]\n\tfldl %[right]\n\tfxch %%st(1)\n\t"
        "fucomi %%st(1), %%st\n\tsetb %[less]\n\tsetnp %[ordered]\n\tfrstor %[saved]\n\t"
        : [saved] "=m" (saved), [argument] "=m" (argument), [extent] "=m" (extent),
          [right] "=m" (right), [less] "=qm" (less), [ordered] "=qm" (ordered)
        : [in] "r" (inputs), [control] "m" (control)
        : "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc");
    *out = less & ordered;
    return 0;
}
#endif

extern "C" int legacy_laser_point_hit(LASER_PARAMETERS) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2) return hardware_laser_point_hit(LASER_ARGUMENTS);
#endif
    return software_laser_point_hit(LASER_ARGUMENTS);
}
