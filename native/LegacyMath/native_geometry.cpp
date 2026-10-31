// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

static bool valid_precision(int precision) { return precision == 24 || precision == 53; }

static uint32_t circle_float_bits(float value) {
    uint32_t bits;
    __builtin_memcpy(&bits, &value, sizeof(bits));
    return bits;
}

// Magnitudes are already bounded by 2^20; all shifts therefore lie in [3,23].
static uint32_t circle_truncated_magnitude(uint32_t magnitude) {
    const uint32_t exponent = magnitude >> 23;
    if (exponent < 127) return 0;
    return ((magnitude & 0x7fffff) | 0x800000) >> (150 - exponent);
}

static int32_t circle_truncated_coordinate(uint32_t bits) {
    const int32_t magnitude = int32_t(circle_truncated_magnitude(bits & 0x7fffffff));
    return (bits >> 31) ? -magnitude : magnitude;
}

static uint32_t circle_ceiling(uint32_t bits) {
    const uint32_t magnitude = bits & 0x7fffffff;
    const uint32_t integral = circle_truncated_magnitude(magnitude);
    if (!magnitude) return 0; // Both signs of zero are nonnegative here.
    const uint32_t exponent = magnitude >> 23;
    if (exponent < 127) return 1;
    return integral + ((magnitude & ((uint32_t(1) << (150 - exponent)) - 1)) != 0);
}

static bool circle_integer_reject(float px1, float px2, float py1, float py2, float tx, float ty,
                                  float radius, float scale, int target_region, float target_scale) {
    constexpr uint32_t DomainLimit = 1u << 20;
    constexpr uint32_t DomainLimitBits = 0x49800000;
    constexpr int64_t ConservativeMargin = 16;
    const uint32_t x = circle_float_bits(px1), y = circle_float_bits(py1);
    if (x != circle_float_bits(px2) || y != circle_float_bits(py2)) return false;
    const uint32_t target_x = circle_float_bits(tx), target_y = circle_float_bits(ty);
    const uint32_t coordinates[] = {x, y, target_x, target_y};
    for (uint32_t bits : coordinates)
        if ((bits & 0x7fffffff) > DomainLimitBits) return false;
    const uint32_t r = circle_float_bits(radius), s = circle_float_bits(scale), t = circle_float_bits(target_scale);
    const uint32_t nonnegative[] = {r, s, t};
    for (uint32_t bits : nonnegative)
        if ((bits & 0x7fffffff) > DomainLimitBits || ((bits >> 31) && (bits & 0x7fffffff))) return false;
    if (target_region < 0 || uint32_t(target_region) > DomainLimit) return false;
    const uint64_t radius_upper = uint64_t(circle_ceiling(r)) * circle_ceiling(s);
    const uint64_t target_upper = uint64_t(target_region) * circle_ceiling(t);
    const uint64_t upper = radius_upper + target_upper;
    if (upper > DomainLimit) return false;
    const int64_t dx = int64_t(circle_truncated_coordinate(x)) - circle_truncated_coordinate(target_x);
    const int64_t dy = int64_t(circle_truncated_coordinate(y)) - circle_truncated_coordinate(target_y);
    // Stable captures give genuine squares. In this domain PC24/53 distance error <3/4,
    // each nonnegative product error <=1/16, final subtraction <=1/4, coordinate
    // truncation <2: total adverse error <27/8 pixels. Margin 16 is conservative;
    // outside these guards the original ordered graph remains authoritative.
    const int64_t bound = int64_t(upper) + ConservativeMargin;
    return dx > bound || dx < -bound || dy > bound || dy < -bound;
}

// Convert Int32 exactly, then use the integer codec for the mandatory RNE float store.
static float region_float(int value) {
    software_ext80 integer{};
    uint64_t magnitude = value < 0 ? uint64_t(-int64_t(value)) : uint64_t(value);
    if (magnitude) {
        int shift = __builtin_clzll(magnitude);
        integer.significand = magnitude << shift;
        integer.signExp = uint16_t(16383 + 63 - shift) | (value < 0 ? 0x8000 : 0);
    }
    float result;
    software_ext_store_float(&integer, &result);
    return result;
}

static software_ext80 load_float(float value) {
    software_ext80 result;
    software_ext_load_float(value, &result);
    return result;
}

static software_ext80 double_boundary(software_ext80 value) {
    double stored;
    software_ext_store_double(&value, &stored);
    software_ext_load_double(stored, &value);
    return value;
}

static bool ordered_less(const software_ext80 &a, const software_ext80 &b) {
    if (((a.signExp & 0x7fff) == 0x7fff && (a.significand << 1)) ||
        ((b.signExp & 0x7fff) == 0x7fff && (b.significand << 1))) return false;
    if (!a.significand && !b.significand) return false;
    bool negative = (a.signExp & 0x8000) != 0;
    if (negative != ((b.signExp & 0x8000) != 0)) return negative;
    if (a.signExp == b.signExp && a.significand == b.significand) return false;
    bool smaller = (a.signExp & 0x7fff) < (b.signExp & 0x7fff) ||
        (a.signExp == b.signExp && a.significand < b.significand);
    return negative ? !smaller : smaller;
}

extern "C" int software_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out) {
    if (!out || !valid_precision(precision)) return -1;
    auto x = load_float(px1), x2 = load_float(px2), y = load_float(py1), y2 = load_float(py2);
    auto target_x = load_float(tx), target_y = load_float(ty);
    software_ext_sub(&x, &target_x, precision, &x);
    software_ext_sub(&x2, &target_x, precision, &x2);
    software_ext_mul(&x, &x2, precision, &x);
    software_ext_sub(&y, &target_y, precision, &y);
    software_ext_sub(&y2, &target_y, precision, &y2);
    software_ext_mul(&y, &y2, precision, &y);
    software_ext_add(&x, &y, precision, &x);
    x = double_boundary(x); // CLR2 materializes the Math.Sqrt argument as binary64.
    software_ext_sqrt(&x, precision, &x);
    return software_ext_store_double(&x, out);
}

extern "C" int software_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale,
                                  int target_region, float target_scale, int precision, int *out) {
    if (!out || !valid_precision(precision)) return -1;
    if (circle_integer_reject(px1, px2, py1, py2, tx, ty, radius, scale, target_region, target_scale)) {
        *out = 0;
        return 0;
    }
    double distance;
    software_distance(px1, px2, py1, py2, tx, ty, precision, &distance);
    software_ext80 left;
    software_ext_load_double(distance, &left);
    auto region = load_float(region_float(target_region)), target = load_float(target_scale);
    auto right = load_float(radius), scaling = load_float(scale);
    software_ext_mul(&region, &target, precision, &region);
    region = double_boundary(region); // conv.r8, not a Single multiplication store.
    software_ext_sub(&left, &region, precision, &left);
    software_ext_mul(&right, &scaling, precision, &right);
    right = double_boundary(right);
    *out = ordered_less(left, right) ? 1 : 0;
    return 0;
}

#if defined(__x86_64__)
// Each block owns one empty x87 stack, with all caller state restored exactly once.
#define GEOMETRY_BEGIN "fnsave %[saved]\n\tfldcw %[control]\n\t"
#define GEOMETRY_END "frstor %[saved]\n\t"
#define GEOMETRY_CLOBBERS "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc"
#define GEOMETRY_INPUTS \
    [control] "m" (control), [x] "m" (x), [x2] "m" (x2), [y] "m" (y), [y2] "m" (y2), [tx] "m" (target_x), [ty] "m" (target_y)
// Operand order matches raw hardware_ext_sub/add, including NaN propagation.
#define DISTANCE_GRAPH \
    "fldt %[tx]\n\tfldt %[x]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[tx]\n\tfldt %[x2]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[ty]\n\tfldt %[y]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[ty]\n\tfldt %[y2]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fstpl %[distance]\n\tfldl %[distance]\n\tfsqrt\n\tfstpl %[distance]\n\t"

extern "C" int hardware_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out) {
    if (!out || !valid_precision(precision)) return -1;
    auto x = load_float(px1), x2 = load_float(px2), y = load_float(py1), y2 = load_float(py2);
    auto target_x = load_float(tx), target_y = load_float(ty);
    alignas(16) unsigned char saved[108];
    const uint16_t control = precision == 24 ? 0x007f : 0x027f;
    double distance;
    __asm__ volatile(GEOMETRY_BEGIN DISTANCE_GRAPH GEOMETRY_END
        : [saved] "=m" (saved), [distance] "=m" (distance)
        : GEOMETRY_INPUTS : GEOMETRY_CLOBBERS);
    *out = distance;
    return 0;
}

extern "C" int hardware_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale,
                                  int target_region, float target_scale, int precision, int *out) {
    if (!out || !valid_precision(precision)) return -1;
    if (circle_integer_reject(px1, px2, py1, py2, tx, ty, radius, scale, target_region, target_scale)) {
        *out = 0;
        return 0;
    }
    auto x = load_float(px1), x2 = load_float(px2), y = load_float(py1), y2 = load_float(py2);
    auto target_x = load_float(tx), target_y = load_float(ty);
    auto region = load_float(region_float(target_region)), target = load_float(target_scale);
    auto right = load_float(radius), scaling = load_float(scale);
    alignas(16) unsigned char saved[108];
    const uint16_t control = precision == 24 ? 0x007f : 0x027f;
    double distance, product;
    unsigned char less, ordered;
    __asm__ volatile(GEOMETRY_BEGIN DISTANCE_GRAPH
        "fldt %[target]\n\tfldt %[region]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[product]\n\tfldl %[product]\n\tfldl %[distance]\n\t"
        "fsub %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt %[scaling]\n\tfldt %[right]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[product]\n\tfldl %[product]\n\tfxch %%st(1)\n\t"
        "fucomi %%st(1), %%st\n\tsetb %[less]\n\tsetnp %[ordered]\n\t" GEOMETRY_END
        : [saved] "=m" (saved), [distance] "=m" (distance), [product] "=m" (product),
          [less] "=qm" (less), [ordered] "=qm" (ordered)
        : GEOMETRY_INPUTS, [region] "m" (region), [target] "m" (target), [right] "m" (right), [scaling] "m" (scaling)
        : GEOMETRY_CLOBBERS);
    *out = less & ordered;
    return 0;
}
#endif

extern "C" int legacy_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2) return hardware_distance(px1, px2, py1, py2, tx, ty, precision, out);
#endif
    return software_distance(px1, px2, py1, py2, tx, ty, precision, out);
}

extern "C" int legacy_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale,
                                int target_region, float target_scale, int precision, int *out) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2)
        return hardware_circle_hit(px1, px2, py1, py2, tx, ty, radius, scale, target_region, target_scale, precision, out);
#endif
    return software_circle_hit(px1, px2, py1, py2, tx, ty, radius, scale, target_region, target_scale, precision, out);
}
