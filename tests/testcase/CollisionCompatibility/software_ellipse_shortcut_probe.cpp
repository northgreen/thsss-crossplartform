// 2026 supplied-library correctness gate; no timings or game-loop claims.
#include "software_ellipse_shortcut_cases.h"
#include <thread>
#define main gameplay_backend_state_probe_main
#include "../GameplayMath/backend_state.cpp"
#undef main

namespace shortcut {
// Observe the last division of the UNMODIFIED full oracle: its internal right term R.
static Binary oracle_div;
static Extended oracle_right;
static int capture_div(const Extended *a, const Extended *b, int p, Extended *out) {
    int status = oracle_div(a, b, p, out);
    oracle_right = *out;
    return status;
}
struct Counters {
    void (*reset)();
    uint64_t (*fast)(), (*full)();
    explicit Counters(void *library) {
        reset = ellipse::symbol<decltype(reset)>(library, "shortcut_reset");
        fast = ellipse::symbol<decltype(fast)>(library, "shortcut_fast");
        full = ellipse::symbol<decltype(full)>(library, "shortcut_full");
    }
};
static int compare(const Raw &raw, Hit direct, Hit dispatch, Hit diagnostic,
                   const Case &c, int pc, uint64_t &fast, uint64_t &full) {
    int expected = raw.hit(c, pc);
    if (!(raw.stored(oracle_right) < 1.0)) ++fast; else ++full;
    for (Hit fn : {direct, dispatch, diagnostic}) {
        int actual = -1;
        require(call(fn, c, pc, &actual) == 0 && same_boolean(expected, actual),
                "actual direct/dispatch/diagnostic differs from old full graph");
    }
    return expected;
}
static void gate(void *library, void *instrument, bool self_test) {
    Raw raw(library, "software");
    oracle_div = raw.div; raw.div = capture_div;
    auto direct = ellipse::symbol<Hit>(library, "software_ellipse_hit");
    auto dispatch = ellipse::symbol<Hit>(library, "legacy_ellipse_hit");
    auto diagnostic = ellipse::symbol<Hit>(instrument, "shortcut_ellipse_hit");
    Counters counters(instrument);
    auto corpus = self_test ? std::vector<Case>{} : cases();
    for (int pc : {24, 53}) {
        uint64_t fast = 0, full = 0, hash = UINT64_C(1469598103934665603);
        counters.reset();
        for (const auto &sample : literals())
            require(compare(raw, direct, dispatch, diagnostic, sample.input, pc, fast, full) == sample.expected,
                    "literal expected boolean (independent of old graph)");
        require(fast > 0 && full > 0, "literal FAST and FULL non-vacuous coverage");
        require(counters.fast() == fast && counters.full() == full, "literal actual guard counters vs oracle R");
        counters.reset(); fast = full = 0;
        for (size_t i = 0; i < corpus.size(); ++i) {
            int expected = compare(raw, direct, dispatch, diagnostic, corpus[i], pc, fast, full);
            if (i < OriginalCases) hash = (hash ^ uint64_t(expected)) * UINT64_C(1099511628211);
        }
        if (self_test) continue;
        require(hash == (pc == 24 ? UINT64_C(0xf863cc87e98d88c4) : UINT64_C(0xd296421446bfd659)),
                "unchanged original192305 boolean fingerprint");
        require(fast && full && counters.fast() == fast && counters.full() == full,
                "actual temporary guard FAST/FULL must equal oracle internal R classification");
        std::printf("PASS PC%d cases=%zu comparisons=%zu FAST=%llu FULL=%llu literals=%zu\n",
                    pc, corpus.size(), corpus.size() * 3, (unsigned long long)fast,
                    (unsigned long long)full, literals().size());
    }
    require(!same_boolean(0, 1) && !same_boolean(1, 3), "boolean mutation/noncanonical negative controls");
}
static void thread_states(void *library) {
    auto direct = ellipse::symbol<Hit>(library, "software_ellipse_hit");
    auto dispatch = ellipse::symbol<Hit>(library, "legacy_ellipse_hit");
    std::thread threads[4];
    int counts[4]{};
    for (int t = 0; t < 4; ++t) threads[t] = std::thread([&, t] {
        for (Hit fn : {direct, dispatch}) {
            for (int pc : {24, 53, 0, 64, -1}) {
                for (uint32_t distance : {0x3f000000u, 0x40000000u, 0x7fc12345u, 0x7f800001u}) {
                    const auto c = axis(distance);
                    const bool valid = pc == 24 || pc == 53;
                    int result = 123;
                    require(preserved("software thread dirty/full state + output", [&] {
                        int status = call(fn, c, pc, &result);
                        int expected = distance == 0x3f000000u ? 1 : 0;
                        return status == (valid ? 0 : -1) && result == (valid ? expected : 123) ? 0 : 1;
                    }), "thread success/error FPU and MXCSR");
                    ++counts[t];
                }
                require(preserved("software thread null output", [&] {
                    return call(fn, axis(0), pc, nullptr) == -1 ? 0 : 1;
                }), "thread null status and FPU/MXCSR");
                ++counts[t];
            }
            for (int pc : {24, 53}) for (uint32_t distance : {0x3f000000u, 0x40000000u}) {
                int result = -1;
                require(preserved("thread known full/fast", [&] {
                    return call(fn, axis(distance), pc, &result);
                }) && result == (distance == 0x3f000000u ? 1 : 0), "thread known branches");
                ++counts[t];
            }
        }
    });
    for (auto &thread : threads) thread.join();
    int total = 0; for (int count : counts) total += count;
    require(total == 232, "four-thread state count");
    std::printf("PASS four threads: %d dirty/full-x87+MXCSR/status/output checks\n", total);
}
} // namespace shortcut

int main(int argc, char **argv) {
    ellipse::require(argc == 3 || (argc == 4 && std::strcmp(argv[3], "--self-test") == 0),
                     "usage: probe supplied.so diagnostic.so [--self-test]");
    void *library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 2; }
    void *instrument = dlopen(argv[2], RTLD_NOW | RTLD_LOCAL);
    if (!instrument) { std::fprintf(stderr, "%s\n", dlerror()); return 2; }
    auto select = ellipse::symbol<decltype(&legacy_math_set_backend)>(library, "legacy_math_set_backend");
    ellipse::require(select(1) == 0, "select software before threads");
    shortcut::gate(library, instrument, argc == 4);
    if (argc == 3) shortcut::thread_states(library);
    ellipse::require(select(0) == 0, "restore backend");
    dlclose(instrument); dlclose(library);
    return 0;
}
