// 2026: raw-native ellipse graph; not a historical Windows CLR2 capture.
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <dlfcn.h>
#include "../../../native/LegacyMath/software_x87.h"

namespace ellipse {
using Extended = software_ext80;
using Hit = decltype(&software_ellipse_hit); // C ABI: exactly the production scalar signature.
using Binary = decltype(&software_ext_add);
using Unary = decltype(&software_ext_sin);

inline void require(bool condition, const char *message) {
    if (condition) return;
    std::fprintf(stderr, "FAIL %s\n", message);
    std::exit(1);
}

template<class T> T symbol(void *library, const std::string &name) {
    auto address = dlsym(library, name.c_str());
    require(address != nullptr, name.c_str());
    return reinterpret_cast<T>(address);
}

struct Case {
    double direction{}, angle{};
    int flag{};
    // px1, px2, py1, py2, distanceTargetX/Y, directionOwnY/X, directionTargetX/Y.
    float coordinates[10]{};
    int region{};
    float scale{}, radius{}, width{}, length{};
};

inline int call(Hit hit, const Case &c, int precision, int *out) {
    const auto *f = c.coordinates;
    return hit(c.direction, c.angle, c.flag, f[0], f[1], f[2], f[3], f[4], f[5],
               f[6], f[7], f[8], f[9], c.region, c.scale, c.radius, c.width, c.length,
               precision, out);
}

inline float float_from_bits(uint32_t bits) {
    float result; std::memcpy(&result, &bits, sizeof(result)); return result;
}
inline double double_from_bits(uint64_t bits) {
    double result; std::memcpy(&result, &bits, sizeof(result)); return result;
}
inline uint32_t bits_of_float(float value) {
    uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); return bits;
}

struct Raw {
    decltype(&software_ext_load_float) load_float;
    decltype(&software_ext_load_double) load_double;
    decltype(&software_ext_store_float) store_float;
    decltype(&software_ext_store_double) store_double;
    Binary add, sub, mul, div;
    Unary sin, cos;
    decltype(&software_ext_sqrt) sqrt;
    decltype(&software_ext_atan2) atan;

    explicit Raw(void *library, const std::string &prefix) {
#define BIND(field, suffix) field = symbol<decltype(field)>(library, prefix + "_ext_" suffix)
        BIND(load_float, "load_float"); BIND(load_double, "load_double");
        BIND(store_float, "store_float"); BIND(store_double, "store_double");
        BIND(add, "add"); BIND(sub, "sub"); BIND(mul, "mul"); BIND(div, "div");
        BIND(sin, "sin"); BIND(cos, "cos"); BIND(sqrt, "sqrt"); BIND(atan, "atan2");
#undef BIND
    }
    Extended f(float value) const {
        Extended result; require(load_float(value, &result) == 0, "load float"); return result;
    }
    Extended d(double value) const {
        Extended result; require(load_double(value, &result) == 0, "load double"); return result;
    }
    double stored(Extended value) const {
        double result; require(store_double(&value, &result) == 0, "store double"); return result;
    }
    Extended n64(Extended value) const { return d(stored(value)); }
    Extended n32(Extended value) const {
        float result; require(store_float(&value, &result) == 0, "store float"); return f(result);
    }
    Extended op(Binary function, Extended a, Extended b, int precision) const {
        Extended result;
        require(function(&a, &b, precision, &result) == 0, "raw binary status"); return result;
    }
    Extended trig(Unary function, Extended a) const {
        Extended result; require(function(&a, &result) == 0, "raw trig status"); return result;
    }
    static bool infinite(Extended value) {
        return (value.signExp & 32767) == 32767 && value.significand == UINT64_C(0x8000000000000000);
    }
    Extended theta(const Case &c, int precision) const {
        return c.flag ? op(add, d(c.direction), d(c.angle), precision) : d(c.angle);
    }
    // Independent operation-by-operation oracle: no distance/circle/ellipse fused entrypoints.
    int hit(const Case &c, int p) const {
        const auto *v = c.coordinates;
        auto dx1 = op(sub, f(v[0]), f(v[4]), p), dx2 = op(sub, f(v[1]), f(v[4]), p);
        auto dy1 = op(sub, f(v[2]), f(v[5]), p), dy2 = op(sub, f(v[3]), f(v[5]), p);
        auto sum = n64(op(add, op(mul, dx1, dx2, p), op(mul, dy1, dy2, p), p));
        Extended distance;
        require(sqrt(&sum, p, &distance) == 0, "raw sqrt status");
        // Separate host Int32->Single conversion provides an independent RNE conversion path.
        auto extent = n64(op(mul, f(static_cast<float>(c.region)), f(c.scale), p));
        auto e = op(add, n64(distance), extent, p);
        auto ay = n64(op(sub, f(v[6]), f(v[9]), p));
        auto ax = n64(op(sub, f(v[7]), f(v[8]), p));
        Extended bearing;
        if (infinite(ay) && infinite(ax)) bearing = op(div, ax, ay, p);
        else require(atan(&ay, &ax, &bearing) == 0, "raw atan2 status");
        auto pi = d(double_from_bits(UINT64_C(0x400921fb54442d18)));
        bearing = n64(op(add, pi, bearing, p));
        const auto th = theta(c, p); // Keep ext80, including values beyond binary64's exponent range.
        auto ca = n64(n32(op(sub, th, bearing, p)));
        auto num4 = op(mul, e, trig(cos, ca), p);
        auto sa = n64(n32(op(sub, th, bearing, p)));
        auto num5 = op(mul, e, trig(sin, sa), p);
        auto num6 = n64(op(mul, f(c.radius), f(c.width), p));
        auto num7 = n64(op(mul, f(c.radius), f(c.length), p));
        auto left = op(div, op(div, op(mul, num5, num5, p), num6, p), num6, p);
        auto right = op(div, op(div, op(mul, num4, num4, p), num7, p), num7, p);
        // A PC24/53 result adjacent to 1 is exact binary64; this store preserves its ordering.
        return stored(op(add, left, right, p)) < 1.0;
    }
};

inline bool same_boolean(int expected, int actual) {
    return (expected == 0 || expected == 1) && (actual == 0 || actual == 1) && expected == actual;
}
} // namespace ellipse
