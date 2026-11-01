// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

#define ELLIPSE_PARAMETERS double direction, double angle, int angle_flag, \
    float px1, float px2, float py1, float py2, float dtx, float dty, \
    float own_y, float own_x, float gtx, float gty, int target_region, \
    float target_scale, float radius, float width, float length, int precision, int *out
#define ELLIPSE_ARGUMENTS direction, angle, angle_flag, px1, px2, py1, py2, dtx, dty, \
    own_y, own_x, gtx, gty, target_region, target_scale, radius, width, length, precision, out

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

static software_ext80 boundary64(software_ext80 value) {
    double stored;
    software_ext_store_double(&value, &stored);
    return load_double(stored);
}

static software_ext80 boundary32(software_ext80 value) {
    float stored;
    software_ext_store_float(&value, &stored);
    return load_float(stored);
}

static software_ext80 load_region(int value) {
    software_ext80 integer{};
    const uint64_t magnitude = value < 0 ? uint64_t(-int64_t(value)) : uint64_t(value);
    if (magnitude) {
        const int shift = __builtin_clzll(magnitude);
        integer.significand = magnitude << shift;
        integer.signExp = uint16_t(16383 + 63 - shift) | (value < 0 ? 0x8000 : 0);
    }
    return boundary32(integer); // Int32 -> binary32 RNE, independent of PC.
}

static bool is_infinite(const software_ext80 &value) {
    return (value.signExp & 0x7fff) == 0x7fff && value.significand == UINT64_C(0x8000000000000000);
}

static bool ordered_less_than_one(const software_ext80 &value) {
    if ((value.signExp & 0x7fff) == 0x7fff && (value.significand << 1)) return false;
    return (value.signExp & 0x8000) || (value.signExp & 0x7fff) < 16383;
}

static double binary64_pi() {
    const uint64_t bits = UINT64_C(0x400921fb54442d18);
    double result;
    __builtin_memcpy(&result, &bits, sizeof(result));
    return result;
}

extern "C" int software_ellipse_hit(ELLIPSE_PARAMETERS) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    auto theta = load_double(angle);
    if (angle_flag) {
        auto heading = load_double(direction);
        software_ext_add(&heading, &theta, precision, &theta);
    }
    double distance;
    software_distance(px1, px2, py1, py2, dtx, dty, precision, &distance);
    auto e = load_double(distance), region = load_region(target_region), scale = load_float(target_scale);
    software_ext_mul(&region, &scale, precision, &region);
    region = boundary64(region);
    software_ext_add(&e, &region, precision, &e);

    auto ay = load_float(own_y), ax = load_float(own_x), gx = load_float(gtx), gy = load_float(gty);
    software_ext_sub(&ay, &gy, precision, &ay);
    ay = boundary64(ay);
    software_ext_sub(&ax, &gx, precision, &ax);
    ax = boundary64(ax);
    software_ext80 bearing;
    if (is_infinite(ay) && is_infinite(ax)) software_ext_div(&ax, &ay, precision, &bearing);
    else software_ext_atan2(&ay, &ax, &bearing);
    auto pi = load_double(binary64_pi());
    software_ext_add(&pi, &bearing, precision, &bearing);
    bearing = boundary64(bearing);

    software_ext80 argument, num4, num5;
    software_ext_sub(&theta, &bearing, precision, &argument);
    argument = boundary64(boundary32(argument));
    software_ext_cos(&argument, &num4);
    software_ext_mul(&e, &num4, precision, &num4);
    auto r = load_float(radius), num7 = load_float(length);
    software_ext_mul(&r, &num7, precision, &num7);
    num7 = boundary64(num7);
    software_ext_mul(&num4, &num4, precision, &num4);
    software_ext_div(&num4, &num7, precision, &num4);
    software_ext_div(&num4, &num7, precision, &num4);
    // The other squared term is nonnegative or NaN, so it cannot rescue this miss.
    if (!ordered_less_than_one(num4)) {
        *out = 0;
        return 0;
    }
    software_ext_sub(&theta, &bearing, precision, &argument); // Preserve the repeated argument graph.
    argument = boundary64(boundary32(argument));
    software_ext_sin(&argument, &num5);
    software_ext_mul(&e, &num5, precision, &num5);

    auto num6 = load_float(width);
    software_ext_mul(&r, &num6, precision, &num6);
    num6 = boundary64(num6);
    software_ext_mul(&num5, &num5, precision, &num5);
    software_ext_div(&num5, &num6, precision, &num5);
    software_ext_div(&num5, &num6, precision, &num5);
    software_ext_add(&num5, &num4, precision, &num5);
    *out = ordered_less_than_one(num5) ? 1 : 0;
    return 0;
}

#if defined(__x86_64__)
static uint32_t float_bits(float value) {
    uint32_t bits;
    __builtin_memcpy(&bits, &value, sizeof(bits));
    return bits;
}

// Finite binary32 differences cannot overflow binary64. Same-sign inf-inf is NaN.
static bool infinite_difference(float a, float b) {
    const uint32_t x = float_bits(a), y = float_bits(b);
    const uint32_t ax = x & 0x7fffffff, ay = y & 0x7fffffff;
    if (ax > 0x7f800000 || ay > 0x7f800000) return false;
    return (ax == 0x7f800000 || ay == 0x7f800000) && x != y;
}

// Private copy of the existing distance graph; explicit stores are part of the IL contract.
#define ELLIPSE_DISTANCE_GRAPH \
    "fldt %[tx]\n\tfldt %[x]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[tx]\n\tfldt %[x2]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[ty]\n\tfldt %[y]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fldt %[ty]\n\tfldt %[y2]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fxch %%st(1)\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fstpl %[distance]\n\tfldl %[distance]\n\tfsqrt\n\tfstpl %[distance]\n\t"
#define ELLIPSE_ARGUMENT_GRAPH \
    "fldl %[bearing]\n\tfldt %[theta]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\t" \
    "fstps %[argument]\n\tflds %[argument]\n\tfstpl %[boundary]\n\tfldl %[boundary]\n\t"

extern "C" int hardware_ellipse_hit(ELLIPSE_PARAMETERS) {
    if (!out || (precision != 24 && precision != 53)) return -1;
    const auto heading = angle_flag ? load_double(direction) : software_ext80{};
    const auto rotation = load_double(angle);
    const auto x = load_float(px1), x2 = load_float(px2), y = load_float(py1), y2 = load_float(py2);
    const auto target_x = load_float(dtx), target_y = load_float(dty);
    const auto oy = load_float(own_y), ox = load_float(own_x), gx = load_float(gtx), gy = load_float(gty);
    const auto region = load_region(target_region), scale = load_float(target_scale);
    const auto r = load_float(radius), w = load_float(width), l = load_float(length);
    const int both_infinite = infinite_difference(own_y, gty) && infinite_difference(own_x, gtx);
    const uint16_t control = precision == 24 ? 0x007f : 0x027f;
    const double pi = binary64_pi();
    alignas(16) unsigned char saved[108];
    software_ext80 theta, extent, num4, num5, num6, num7;
    double distance, boundary, dy, dx, bearing;
    float argument;
    unsigned char less, ordered;
    // One FPU transaction; at most four live registers, no calls or host FP arithmetic.
    __asm__ volatile(
        "fnsave %[saved]\n\tfldcw %[control]\n\t"
        "fldt %[rotation]\n\tcmpl $0, %[flag]\n\tje 1f\n\t"
        "fldt %[heading]\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "1:\n\tfstpt %[theta]\n\t"
        ELLIPSE_DISTANCE_GRAPH
        "fldt %[scale]\n\tfldt %[region]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[boundary]\n\tfldl %[boundary]\n\tfldl %[distance]\n\t"
        "fadd %%st(1), %%st\n\tfstp %%st(1)\n\tfstpt %[extent]\n\t"
        "fldt %[gy]\n\tfldt %[oy]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\tfstpl %[dy]\n\t"
        "fldt %[gx]\n\tfldt %[ox]\n\tfsub %%st(1), %%st\n\tfstp %%st(1)\n\tfstpl %[dx]\n\t"
        "fldl %[dy]\n\tfldl %[dx]\n\tcmpl $0, %[both_infinite]\n\tje 2f\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\tjmp 3f\n\t"
        "2:\n\tfpatan\n\t3:\n\tfldl %[pi]\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\tfstpl %[bearing]\n\t"
        ELLIPSE_ARGUMENT_GRAPH
        "fcos\n\tfldt %[extent]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\tfstpt %[num4]\n\t"
        ELLIPSE_ARGUMENT_GRAPH
        "fsin\n\tfldt %[extent]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\tfstpt %[num5]\n\t"
        "fldt %[w]\n\tfldt %[r]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[boundary]\n\tfldl %[boundary]\n\tfstpt %[num6]\n\t"
        "fldt %[l]\n\tfldt %[r]\n\tfmul %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fstpl %[boundary]\n\tfldl %[boundary]\n\tfstpt %[num7]\n\t"
        "fldt %[num6]\n\tfldt %[num5]\n\tfmul %%st, %%st\n\tfdiv %%st(1), %%st\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fldt %[num7]\n\tfldt %[num4]\n\tfmul %%st, %%st\n\tfdiv %%st(1), %%st\n\t"
        "fdiv %%st(1), %%st\n\tfstp %%st(1)\n\t"
        "fxch %%st(1)\n\tfadd %%st(1), %%st\n\tfstp %%st(1)\n\tfld1\n\tfxch %%st(1)\n\t"
        "fucomi %%st(1), %%st\n\tsetb %[less]\n\tsetnp %[ordered]\n\tfrstor %[saved]\n\t"
        : [saved] "=m" (saved), [theta] "=m" (theta), [extent] "=m" (extent),
          [num4] "=m" (num4), [num5] "=m" (num5), [num6] "=m" (num6), [num7] "=m" (num7),
          [distance] "=m" (distance), [boundary] "=m" (boundary), [dy] "=m" (dy), [dx] "=m" (dx),
          [bearing] "=m" (bearing), [argument] "=m" (argument), [less] "=qm" (less), [ordered] "=qm" (ordered)
        : [control] "m" (control), [flag] "m" (angle_flag), [heading] "m" (heading), [rotation] "m" (rotation),
          [x] "m" (x), [x2] "m" (x2), [y] "m" (y), [y2] "m" (y2), [tx] "m" (target_x), [ty] "m" (target_y),
          [oy] "m" (oy), [ox] "m" (ox), [gx] "m" (gx), [gy] "m" (gy), [region] "m" (region),
          [scale] "m" (scale), [r] "m" (r), [w] "m" (w), [l] "m" (l), [both_infinite] "m" (both_infinite), [pi] "m" (pi)
        : "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)", "memory", "cc");
    *out = less & ordered;
    return 0;
}
#endif

extern "C" int legacy_ellipse_hit(ELLIPSE_PARAMETERS) {
#if defined(__x86_64__)
    if (legacy_math_get_backend() == 2) return hardware_ellipse_hit(ELLIPSE_ARGUMENTS);
#endif
    return software_ellipse_hit(ELLIPSE_ARGUMENTS);
}
