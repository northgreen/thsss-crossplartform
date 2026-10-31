// 2026: independent raw software_ext operation graph, never host float arithmetic.
#pragma once
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <dlfcn.h>
#include "../../../native/LegacyMath/software_x87.h"

inline void require(bool condition, const char *message) {
    if (!condition) { std::fprintf(stderr, "FAIL %s\n", message); std::exit(1); }
}
template<class T> T required_symbol(void *library, const std::string &name) {
    auto address = dlsym(library, name.c_str());
    require(address != nullptr, name.c_str());
    return reinterpret_cast<T>(address);
}
struct RawApi {
    decltype(&software_ext_load_float) load_float;
    decltype(&software_ext_load_double) load_double;
    decltype(&software_ext_store_float) store_float;
    decltype(&software_ext_store_double) store_double;
    decltype(&software_ext_sub) sub, mul, add;
    decltype(&software_ext_sqrt) sqrt;
    explicit RawApi(void *library, const std::string &prefix) {
#define BIND(field) field = required_symbol<decltype(field)>(library, prefix + "_ext_" #field)
        BIND(load_float); BIND(load_double); BIND(store_float); BIND(store_double);
        BIND(sub); BIND(mul); BIND(add); BIND(sqrt);
#undef BIND
    }
    software_ext80 load(float f) const {
        software_ext80 result; require(load_float(f, &result) == 0, "load float"); return result;
    }
    software_ext80 boundary(software_ext80 value) const {
        double stored;
        require(store_double(&value, &stored) == 0, "store double");
        require(load_double(stored, &value) == 0, "load double"); return value;
    }
    double distance(const float *f, int precision) const {
        software_ext80 a[6]; for (int i = 0; i < 6; ++i) a[i] = load(f[i]);
        require(sub(a, a+4, precision, a) == 0, "dx1");
        require(sub(a+1, a+4, precision, a+1) == 0, "dx2");
        require(mul(a, a+1, precision, a) == 0, "dx product");
        require(sub(a+2, a+5, precision, a+2) == 0, "dy1");
        require(sub(a+3, a+5, precision, a+3) == 0, "dy2");
        require(mul(a+2, a+3, precision, a+2) == 0, "dy product");
        require(add(a, a+2, precision, a) == 0, "sum");
        a[0] = boundary(a[0]);
        require(sqrt(a, precision, a) == 0, "sqrt");
        double result; require(store_double(a, &result) == 0, "distance return"); return result;
    }
    int hit(const float *f, int region, int precision) const {
        software_ext80 left, target;
        require(load_double(distance(f, precision), &left) == 0, "distance reload");
        // Int32->Double is exact, then integer-only codec performs the RNE Single store.
        require(load_double(static_cast<double>(region), &target) == 0, "region integer");
        float narrowed; require(store_float(&target, &narrowed) == 0, "region RNE32");
        target = load(narrowed); auto scale = load(f[8]);
        require(mul(&target, &scale, precision, &target) == 0, "target product");
        target = boundary(target);
        require(sub(&left, &target, precision, &left) == 0, "distance difference");
        auto right = load(f[6]); scale = load(f[7]);
        require(mul(&right, &scale, precision, &right) == 0, "radius product");
        return ordered_less(left, boundary(right));
    }
    static bool ordered_less(const software_ext80 &a, const software_ext80 &b) {
        if (((a.signExp & 32767) == 32767 && (a.significand << 1)) ||
            ((b.signExp & 32767) == 32767 && (b.significand << 1))) return false;
        if (!a.significand && !b.significand) return false;
        bool negative = a.signExp >> 15;
        if (negative != bool(b.signExp >> 15)) return negative;
        if (a.signExp == b.signExp && a.significand == b.significand) return false;
        bool smaller = (a.signExp & 32767) < (b.signExp & 32767) ||
            (a.signExp == b.signExp && a.significand < b.significand);
        return negative ? !smaller : smaller;
    }
};
struct GeometryApi {
    decltype(&software_distance) distance;
    decltype(&software_circle_hit) circle_hit;
    GeometryApi(void *library, const std::string &prefix)
        : distance(required_symbol<decltype(distance)>(library, prefix + "_distance")),
          circle_hit(required_symbol<decltype(circle_hit)>(library, prefix + "_circle_hit")) {}
    double measure(const float *f, int precision) const {
        double result;
        require(distance(f[0], f[1], f[2], f[3], f[4], f[5], precision, &result) == 0, "distance status");
        return result;
    }
    int hit(const float *f, int region, int precision) const {
        int result = -7;
        require(circle_hit(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], region, f[8], precision, &result) == 0,
                "circle status");
        require(result == 0 || result == 1, "canonical hit"); return result;
    }
};
inline uint64_t double_bits(double value) { uint64_t bits; std::memcpy(&bits, &value, 8); return bits; }
inline float float_bits(uint32_t bits) { float value; std::memcpy(&value, &bits, 4); return value; }
