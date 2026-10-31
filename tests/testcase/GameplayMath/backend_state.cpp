// 2026 backend ABI v1: Linux x64 full caller x87-state preservation gate.
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <initializer_list>
#include "../../../native/LegacyMath/software_x87.h"

#if !defined(__linux__) || !defined(__x86_64__)
#error This harness requires Linux x64 and explicit x87 assembly.
#endif

struct X87State { unsigned char bytes[108]; };
static_assert(sizeof(software_ext80) == 16, "ABI v1 raw ext80 size");

struct CallerState {
    X87State original{};
    uint32_t mxcsr{};
    CallerState() {
        asm volatile("fnsave %0; frstor %0; stmxcsr %1"
                     : "=m"(original), "=m"(mxcsr) : : "memory");
    }
    ~CallerState() {
        asm volatile("frstor %0; ldmxcsr %1" : : "m"(original), "m"(mxcsr) : "memory");
    }
};

template<class T> static T symbol(void *library, const char *name) {
    auto address = dlsym(library, name);
    if (!address) { std::fprintf(stderr, "Missing ABI v1 symbol: %s\n", name); return nullptr; }
    return reinterpret_cast<T>(address);
}

static void seed_state(X87State &before, uint32_t &mxcsr) {
    const uint16_t cw = 0x0b7f; // Extended precision, upward rounding, masked exceptions.
    const software_ext80 sentinels[] = {
        {0x8000000000000000ULL, 0x3fff, {}}, {0x8000000000000000ULL, 0x4000, {}},
        {0xc000000000000000ULL, 0x4000, {}}, {0x8000000000000000ULL, 0x4001, {}},
        {0xa000000000000000ULL, 0x4001, {}}, {0xc000000000000000ULL, 0x4001, {}},
        {0xe000000000000000ULL, 0x4001, {}}, {0x8000000000000000ULL, 0x4002, {}}
    };
    asm volatile("fninit; fldcw %0; fldz; fldz; fdivp; fstp %%st(0)"
                 : : "m"(cw) : "memory");
    for (const auto &value : sentinels) asm volatile("fldt %0" : : "m"(value) : "memory");
    asm volatile("stmxcsr %0" : "=m"(mxcsr));
    mxcsr = (mxcsr & ~0x6000u) | 0x4001u;
    asm volatile("ldmxcsr %1; fnsave %0; frstor %0"
                 : "=m"(before) : "m"(mxcsr) : "memory");
}

static bool same_state(const X87State &before, const X87State &after) {
    // FNSAVE contains undefined/reserved environment bytes and changing instruction pointers.
    // Compare the actual CW, SW, full tags and all eight 80-bit register payloads.
    return std::memcmp(before.bytes, after.bytes, 2) == 0 &&
           std::memcmp(before.bytes + 4, after.bytes + 4, 2) == 0 &&
           std::memcmp(before.bytes + 8, after.bytes + 8, 2) == 0 &&
           std::memcmp(before.bytes + 28, after.bytes + 28, 80) == 0;
}

template<class Operation> static bool preserved(const char *name, Operation operation) {
    X87State before{}, after{};
    uint32_t mxcsr_before{}, mxcsr_after{};
    int status;
    bool valid_seed;
    {
        CallerState restore_even_on_failure;
        seed_state(before, mxcsr_before);
        uint16_t cw{}, sw{}, tags{};
        std::memcpy(&cw, before.bytes, 2);
        std::memcpy(&sw, before.bytes + 4, 2);
        std::memcpy(&tags, before.bytes + 8, 2);
        valid_seed = cw == 0x0b7f && (sw & 1) && tags == 0;
        status = operation();
        asm volatile("fnsave %0; stmxcsr %1" : "=m"(after), "=m"(mxcsr_after) : : "memory");
    }
    if (valid_seed && status == 0 && same_state(before, after) && mxcsr_before == mxcsr_after)
        return true;
    std::fprintf(stderr, "FAIL state %s: seeded=%d status=%d x87_equal=%d MXCSR=%08x/%08x\n",
                 name, valid_seed, status, same_state(before, after), mxcsr_before, mxcsr_after);
    return false;
}

static bool check_operations(void *library, const char *prefix, int &count) {
    using LoadD = int(*)(double, software_ext80*);
    using LoadF = int(*)(float, software_ext80*);
    using StoreD = int(*)(const software_ext80*, double*);
    using StoreF = int(*)(const software_ext80*, float*);
    using Unary = int(*)(const software_ext80*, software_ext80*);
    using Sqrt = int(*)(const software_ext80*, int, software_ext80*);
    using Atan = int(*)(const software_ext80*, const software_ext80*, software_ext80*);
    using Binary = int(*)(const software_ext80*, const software_ext80*, int, software_ext80*);
    software_ext80 a{0xc000000000000000ULL, 0x3ffe, {}}, b{0xa000000000000000ULL, 0x4000, {}}, out{};
    double d{}; float f{}; char name[96];
    bool ok = true;
#define CHECK(type, suffix, ...) do { \
    std::snprintf(name, sizeof name, "%s_ext_%s", prefix, suffix); \
    auto fn = symbol<type>(library, name); \
    if (!fn) return false; \
    ok = preserved(name, [&] { return fn(__VA_ARGS__); }) && ok; ++count; \
} while (false)
    CHECK(LoadD, "load_double", 0.75, &out);
    CHECK(LoadF, "load_float", 0.75f, &out);
    CHECK(StoreD, "store_double", &a, &d);
    CHECK(StoreF, "store_float", &a, &f);
    CHECK(Unary, "sin", &a, &out);
    CHECK(Unary, "cos", &a, &out);
    CHECK(Atan, "atan2", &a, &b, &out);
    for (int precision : {24, 53}) {
        CHECK(Sqrt, "sqrt", &a, precision, &out);
        CHECK(Binary, "add", &a, &b, precision, &out);
        CHECK(Binary, "sub", &a, &b, precision, &out);
        CHECK(Binary, "mul", &a, &b, precision, &out);
        CHECK(Binary, "div", &a, &b, precision, &out);
    }
#undef CHECK
    return ok;
}

int main(int argc, char **argv) {
    if (argc != 2) { std::fprintf(stderr, "Usage: backend_state library.so\n"); return 2; }
    void *library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 2; }
    auto select = symbol<int(*)(int)>(library, "legacy_math_set_backend");
    if (!select) return 2;
    int count = 0; bool ok = true;
    for (int backend : {1, 2}) {
        if (select(backend) != 0) { ok = false; break; }
        ok = check_operations(library, "legacy", count) && ok;
    }
    ok = check_operations(library, "hardware", count) && ok;
    ok = check_operations(library, "software", count) && ok;
    ok = select(0) == 0 && ok;
    dlclose(library);
    std::printf("ABI v1 Linux x64 FNSAVE/FRSTOR state: %d operations, %s\n", count, ok ? "PASS" : "FAIL");
    return ok ? 0 : 1;
}
