// 2026 backend ABI v1: native hot-loop timings, not Python/ctypes or game FPS.
#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include "../../../native/LegacyMath/software_x87.h"

#if !defined(__linux__) || !defined(__x86_64__)
#error This benchmark requires Linux x64 hardware_ext ABI exports.
#endif

struct Api {
    int (*load_double)(double, software_ext80*);
    int (*load_float)(float, software_ext80*);
    int (*store_double)(const software_ext80*, double*);
    int (*store_float)(const software_ext80*, float*);
    int (*sin)(const software_ext80*, software_ext80*);
    int (*mul)(const software_ext80*, const software_ext80*, int, software_ext80*);
    int (*add)(const software_ext80*, const software_ext80*, int, software_ext80*);
};

template<class T> static void bind(void *library, const char *prefix, const char *suffix, T &function) {
    char name[96]; std::snprintf(name, sizeof name, "%s_ext_%s", prefix, suffix);
    function = reinterpret_cast<T>(dlsym(library, name));
    if (!function) { std::fprintf(stderr, "Missing ABI v1 symbol %s\n", name); std::exit(2); }
}

static Api load_api(void *library, const char *prefix) {
    Api api{};
#define BIND(name) bind(library, prefix, #name, api.name)
    BIND(load_double); BIND(load_float); BIND(store_double); BIND(store_float);
    BIND(sin); BIND(mul); BIND(add);
#undef BIND
    return api;
}

struct Result { software_ext80 raw{}; uint64_t double_bits{}; uint32_t float_bits{}; };
static Result finish(const Api &api, const software_ext80 &raw) {
    Result result{raw}; double d{}; float f{};
    if (api.store_double(&raw, &d) || api.store_float(&raw, &f)) std::exit(1);
    std::memcpy(&result.double_bits, &d, 8); std::memcpy(&result.float_bits, &f, 4);
    if ((result.double_bits & 0x7ff0000000000000ULL) == 0x7ff0000000000000ULL ||
        (result.float_bits & 0x7f800000U) == 0x7f800000U) {
        std::fprintf(stderr, "Nonfinite benchmark output\n"); std::exit(1);
    }
    return result;
}

static Result mixed(const Api &api, int precision, int iterations) {
    software_ext80 angles[16]{}, scale{}, position{}, out{};
    for (int i = 0; i < 16; ++i) if (api.load_double((i + 1) * 0.03125, &angles[i])) std::exit(1);
    if (api.load_double(3.7, &scale) || api.load_double(123.456, &position)) std::exit(1);
    int status = 0;
    for (int i = 0; i < iterations; ++i) {
        status |= api.sin(&angles[i & 15], &out);
        status |= api.mul(&out, &scale, precision, &out);
        status |= api.add(&out, &position, precision, &out);
    }
    if (status) std::exit(1);
    return finish(api, out);
}

static Result conversion(const Api &api, int, int iterations) {
    float f = 0.75f; double d{}; software_ext80 out{}; int status = 0;
    for (int i = 0; i < iterations; ++i) {
        status |= api.load_float(f, &out);
        status |= api.store_double(&out, &d);
        status |= api.load_double(d, &out);
        status |= api.store_float(&out, &f);
    }
    if (status) std::exit(1);
    return finish(api, out);
}

static bool equal(const Result &a, const Result &b) {
    return std::memcmp(&a.raw, &b.raw, 16) == 0 && a.double_bits == b.double_bits && a.float_bits == b.float_bits;
}

using Work = Result(*)(const Api&, int, int);
static double measure(Work work, const Api &api, int precision, int iterations, int calls, const Result &expected) {
    work(api, precision, 1024);
    std::array<double, 7> samples{};
    for (double &sample : samples) {
        auto start = std::chrono::steady_clock::now();
        auto result = work(api, precision, iterations);
        auto end = std::chrono::steady_clock::now();
        if (!equal(result, expected)) { std::fprintf(stderr, "Benchmark raw/float/double bit mismatch\n"); std::exit(1); }
        sample = std::chrono::duration<double, std::nano>(end - start).count() / (iterations * calls);
    }
    std::sort(samples.begin(), samples.end());
    return samples[3];
}

int main(int argc, char **argv) {
    if (argc < 2 || argc > 3) { std::fprintf(stderr, "Usage: backend_benchmark library.so [iterations]\n"); return 2; }
    int iterations = argc == 3 ? std::atoi(argv[2]) : 100000;
    if (iterations < 10000 || iterations > 10000000) return 2;
    void *library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 2; }
    auto select = reinterpret_cast<int(*)(int)>(dlsym(library, "legacy_math_set_backend"));
    if (!select) return 2;
    Api software = load_api(library, "software"), hardware = load_api(library, "hardware");
    Api dispatch = load_api(library, "legacy");
    std::printf("ABI v1 native benchmark Linux x64: 7 samples, %d iterations/sample; median ns/public ABI call\n", iterations);
    for (int precision : {24, 53}) {
        for (int workload = 0; workload < 2; ++workload) {
            Work work = workload == 0 ? mixed : conversion;
            int calls = workload == 0 ? 3 : 4;
            auto expected = work(software, precision, iterations);
            double soft = measure(work, software, precision, iterations, calls, expected);
            double hard = measure(work, hardware, precision, iterations, calls, expected);
            if (select(1)) return 1;
            double dispatch_soft = measure(work, dispatch, precision, iterations, calls, expected);
            if (select(2)) return 1;
            double dispatch_hard = measure(work, dispatch, precision, iterations, calls, expected);
            std::printf("PC%d %-11s software=%.2f hardware=%.2f speedup=%.2fx dispatch_soft=%.2f dispatch_x87=%.2f raw=%04x:%016llx f=%08x d=%016llx\n",
                        precision, workload == 0 ? "SinMulAdd" : "conversion", soft, hard, soft / hard,
                        dispatch_soft, dispatch_hard, expected.raw.signExp,
                        static_cast<unsigned long long>(expected.raw.significand), expected.float_bits,
                        static_cast<unsigned long long>(expected.double_bits));
        }
    }
    int status = select(0); dlclose(library); return status == 0 ? 0 : 1;
}
