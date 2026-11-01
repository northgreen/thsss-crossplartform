// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include <dlfcn.h>
#include <climits>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <initializer_list>

struct Extended { uint64_t sig; uint16_t exp; uint16_t padding[3]; };
using Kernel = int (*)(float, float, float, float, float, float, int, float, float, float, int, int *);
using Load = int (*)(float, Extended *);
using LoadDouble = int (*)(double, Extended *);
using StoreDouble = int (*)(const Extended *, double *);
using StoreFloat = int (*)(const Extended *, float *);
using Binary = int (*)(const Extended *, const Extended *, int, Extended *);
using Sqrt = int (*)(const Extended *, int, Extended *);
static void *library;

static void require(bool condition, const char *message) {
    if (condition) return;
    std::fprintf(stderr, "FAIL: %s\n", message);
    std::exit(1);
}

template<class T> static T symbol(const char *name) {
    void *address = dlsym(library, name);
    require(address != nullptr, name);
    return reinterpret_cast<T>(address);
}

// Independent old IL graph: only raw codecs/arithmetic, never a fused oracle call.
struct Raw {
    Load load; LoadDouble load_double; StoreDouble store_double; StoreFloat store_float;
    Binary add, sub, mul; Sqrt sqrt;
    explicit Raw(const char *prefix) {
        char name[80];
#define GET(field, suffix, type) \
        std::snprintf(name, sizeof(name), "%s_ext_%s", prefix, suffix); field = symbol<type>(name)
        GET(load, "load_float", Load); GET(load_double, "load_double", LoadDouble);
        GET(store_double, "store_double", StoreDouble); GET(store_float, "store_float", StoreFloat);
        GET(add, "add", Binary); GET(sub, "sub", Binary); GET(mul, "mul", Binary);
        GET(sqrt, "sqrt", Sqrt);
#undef GET
    }
    Extended single(float value) {
        Extended result;
        require(load(value, &result) == 0, "raw load");
        return result;
    }
    Extended n64(Extended value) {
        double stored;
        require(store_double(&value, &stored) == 0, "raw N64 store");
        require(load_double(stored, &value) == 0, "raw N64 reload");
        return value;
    }
    static int ordered_less(Extended left, Extended right) {
        if (((left.exp & 32767) == 32767 && (left.sig << 1)) ||
            ((right.exp & 32767) == 32767 && (right.sig << 1))) return 0;
        if (!left.sig && !right.sig) return 0;
        const bool negative = left.exp >> 15;
        if (negative != bool(right.exp >> 15)) return negative;
        if (left.exp == right.exp && left.sig == right.sig) return 0;
        const bool smaller = (left.exp & 32767) < (right.exp & 32767) ||
            (left.exp == right.exp && left.sig < right.sig);
        return negative ? !smaller : smaller;
    }
    int graph(const float *v, int region, int precision, bool collapse_capture = false) {
        const Extended px = single(v[0]), py = single(v[1]);
        const Extended tx1 = single(v[2]), tx2 = single(v[collapse_capture ? 2 : 3]);
        const Extended ty1 = single(v[4]), ty2 = single(v[5]);
        Extended x1, x2, y1, y2, xx, yy, sum, distance, left, extent, right;
        sub(&px, &tx1, precision, &x1); sub(&px, &tx2, precision, &x2);
        mul(&x1, &x2, precision, &xx);
        sub(&py, &ty1, precision, &y1); sub(&py, &ty2, precision, &y2);
        mul(&y1, &y2, precision, &yy); add(&xx, &yy, precision, &sum);
        sum = n64(sum);
        sqrt(&sum, precision, &distance); // No N64 on the extended return.
        Extended integer{};
        const uint64_t magnitude = region < 0 ? uint64_t(-int64_t(region)) : uint64_t(region);
        if (magnitude) {
            const int shift = __builtin_clzll(magnitude);
            integer.sig = magnitude << shift;
            integer.exp = (16383 + 63 - shift) | (region < 0 ? 0x8000 : 0);
        }
        float stored;
        store_float(&integer, &stored); // Exactly one N32, before the target product.
        const Extended reg = single(stored), scale = single(v[6]);
        mul(&reg, &scale, precision, &extent); extent = n64(extent);
        sub(&distance, &extent, precision, &left);
        const Extended radius = single(v[7]), length = single(v[8]);
        mul(&radius, &length, precision, &right); right = n64(right);
        return ordered_less(left, right);
    }
};

static uint64_t random_state = 0xf18548674a389123ULL;
static uint32_t next() {
    random_state ^= random_state << 13;
    random_state ^= random_state >> 7;
    random_state ^= random_state << 17;
    return random_state >> 16;
}
static float bits(uint32_t value) {
    float result;
    std::memcpy(&result, &value, sizeof(result));
    return result;
}
static int call(Kernel kernel, const float *v, int region, int precision, int *out) {
    return kernel(v[0], v[1], v[2], v[3], v[4], v[5], region, v[6], v[7], v[8], precision, out);
}

static void check_state(Kernel kernel, int precision) {
#if defined(__aarch64__)
    uint64_t original_control, original_status, after_control, after_status;
    __asm__ volatile("mrs %0, fpcr; mrs %1, fpsr" : "=r" (original_control), "=r" (original_status));
    const uint64_t control = (original_control & ~(3ULL << 22)) | (3ULL << 22) | (1ULL << 24) | (1ULL << 25);
    const uint64_t status = 0x9f;
    __asm__ volatile("msr fpcr, %0; msr fpsr, %1" :: "r" (control), "r" (status) : "memory");
    const float values[9] = {1, 2, 3, 4, 5, 6, 1, 8, 9};
    int out = 77;
    const int result = call(kernel, values, INT_MAX, precision, &out);
    __asm__ volatile("mrs %0, fpcr; mrs %1, fpsr" : "=r" (after_control), "=r" (after_status) :: "memory");
    __asm__ volatile("msr fpcr, %0; msr fpsr, %1" :: "r" (original_control), "r" (original_status) : "memory");
    require(result == 0 && after_control == control && after_status == status, "ARM FPCR/FPSR changed");
#else
    alignas(16) unsigned char original[108], before[108], after[108];
    uint32_t original_mxcsr, mxcsr = 0xffe5, after_mxcsr;
    const uint16_t control = 0x0f7f; // Masked, truncate, PC64; unrelated to requested PC.
    __asm__ volatile(
        "fnsave %[original]; stmxcsr %[original_mxcsr]; fninit; fldcw %[control];"
        "fldz; fldz; fdivp; fstp %%st(0);"
        "fld1; fldl2t; fldl2e; fldpi; fldlg2; fldln2; fldz; fld1; fchs;"
        "fnsave %[before]; frstor %[before]; ldmxcsr %[mxcsr]"
        : [original] "=m" (original), [original_mxcsr] "=m" (original_mxcsr),
          [before] "=m" (before)
        : [control] "m" (control), [mxcsr] "m" (mxcsr)
        : "memory", "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)");
    const float values[9] = {1, 2, 3, 4, 5, 6, 1, 8, 9};
    int out = 77;
    const int status = call(kernel, values, INT_MAX, precision, &out);
    __asm__ volatile(
        "fnsave %[after]; stmxcsr %[after_mxcsr]; frstor %[original]; ldmxcsr %[original_mxcsr]"
        : [after] "=m" (after), [after_mxcsr] "=m" (after_mxcsr)
        : [original] "m" (original), [original_mxcsr] "m" (original_mxcsr)
        : "memory", "st", "st(1)", "st(2)", "st(3)", "st(4)", "st(5)", "st(6)", "st(7)");
    require((before[4] & 1) != 0, "state fixture must have dirty invalid flag");
    const unsigned tags = unsigned(before[8]) | (unsigned(before[9]) << 8);
    for (unsigned slot = 0; slot < 8; ++slot)
        require(((tags >> (slot * 2)) & 3) != 3, "state fixture must have all eight registers occupied");
    require((unsigned(before[0]) | (unsigned(before[1]) << 8)) == control,
            "state fixture must have nondefault CW");
    require(status == 0 && std::memcmp(before, after, sizeof(before)) == 0,
            "full x87 CW/SW/TOP/tags/register state changed");
    require(mxcsr == after_mxcsr, "MXCSR changed");
#endif
}

static void check_edges(Kernel kernel) {
    float values[9] = {};
    for (int precision : {0, 23, 25, 52, 54, 64, -1}) {
        int out = 123;
        require(call(kernel, values, 0, precision, &out) == -1 && out == 123,
                "invalid PC must return -1 and preserve output");
    }
    for (int precision : {24, 53}) {
        require(call(kernel, values, 0, precision, nullptr) == -1, "null output");
        check_state(kernel, precision);
        float edge[9] = {3, 4, 0, 0, 0, 0, 1, 5, 1};
        int out = -1;
        require(call(kernel, edge, 0, precision, &out) == 0 && out == 0, "strict contact");
        edge[7] = bits(0x40a00001);
        require(call(kernel, edge, 0, precision, &out) == 0 && out == 1, "above contact");
        edge[7] = bits(0x409fffff);
        require(call(kernel, edge, 0, precision, &out) == 0 && out == 0, "below contact");
        edge[0] = 0; edge[1] = 0; edge[2] = -2; edge[3] = 2;
        edge[4] = 0; edge[5] = 0; edge[7] = 5;
        require(call(kernel, edge, 0, precision, &out) == 0 && out == 0, "distinct captures/NaN");
    }
}

int main(int argc, char **argv) {
    require(argc == 2 && argv[1][0] == '/', "expected absolute library path");
    library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 1; }
    const auto software = symbol<Kernel>("software_laser_point_hit");
#if defined(__aarch64__)
    // ARM has no hardware x87; repeat the portable comparison, not a hardware claim.
    const auto hardware = software;
    Raw sw("software"), hw("software");
#else
    const auto hardware = symbol<Kernel>("hardware_laser_point_hit");
    Raw sw("software"), hw("hardware");
#endif
    const auto dispatch = symbol<Kernel>("legacy_laser_point_hit");
    const uint32_t special[] = {0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000,
        0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc01234, 0xffc12345,
        0x7f800001, 0xff800001, 0x3f800000, 0xbf800000};
    const int regions[] = {INT_MIN, INT_MAX, 16777215, 16777216, 16777217, -16777217, 0, 1, -1};
    unsigned long cases = 0;
    for (int precision : {24, 53}) for (int index = 0; index < 100000; ++index) {
        float values[9];
        for (float &value : values) {
            uint32_t word = next();
            if (index < 30000) word = special[next() % 16];
            else if (index < 60000) word = (word & 0x807fffff) | ((120 + next() % 16) << 23);
            value = bits(word);
        }
        const int region = index < 50000 ? regions[next() % 9] : int(next());
        int a = -5, b = -5, d = -5;
        const int expected = sw.graph(values, region, precision);
        const int expected_hardware = hw.graph(values, region, precision);
        if (call(software, values, region, precision, &a) || call(hardware, values, region, precision, &b) ||
            call(dispatch, values, region, precision, &d) || a != expected || b != expected_hardware ||
            a != b || a != d) {
            std::fprintf(stderr, "FAIL index=%d PC=%d region=%d raw=%d/%d fused=%d/%d/%d\n",
                         index, precision, region, expected, expected_hardware, a, b, d);
            return 1;
        }
        ++cases;
    }
    for (Kernel kernel : {software, hardware, dispatch}) check_edges(kernel);
    const auto select = symbol<int (*)(int)>("legacy_math_set_backend");
    for (int backend : {0, 1, 2}) {
#if defined(__aarch64__)
        if (backend == 2) {
            require(select(backend) == -2, "ARM must reject hardware x87");
            continue;
        }
#endif
        require(select(backend) == 0, "backend selection");
        check_edges(dispatch);
    }
    const float witness[9] = {0, 0, -2, 2, 0, 0, 1, 5, 1};
    for (int precision : {24, 53}) for (Raw *raw : {&sw, &hw})
        require(raw->graph(witness, 0, precision) == 0 &&
                raw->graph(witness, 0, precision, true) == 1, "collapsed-capture mutation not detected");
#if defined(__aarch64__)
    std::printf("PASS ARM64 software/dispatch %lu cases, PC24/53; raw portable graph, FPCR/FPSR, hardware rejection; "
                "IEEE/Int32, contact, errors and capture mutation (no hardware x87 or FPS claim)\n", cases);
#else
    std::printf("PASS %lu cases/backend, 100000/PC24 and PC53, seed=f18548674a389123; "
                "raw software/hardware, fused software/hardware/dispatch; IEEE/Int32; "
                 "strict contact; invalid/null; full dirty x87/MXCSR; modes 0/1/2; capture mutation\n", cases);
#endif
    dlclose(library);
}
