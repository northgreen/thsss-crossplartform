// 2026 Linux x64 ellipse gate. Only scalar captures are supplied: no concurrency claim.
#include "native_ellipse_cases.h"
#include <algorithm>
#include <chrono>
#include <type_traits>

// Reuse the established dirty/full x87 and MXCSR preservation fixture unchanged.
#define main gameplay_backend_state_probe_main
#include "../GameplayMath/backend_state.cpp"
#undef main

using ellipse::Case;
using ellipse::Hit;
using ellipse::Raw;
static_assert(std::is_same_v<Hit, decltype(&hardware_ellipse_hit)>);
static_assert(std::is_same_v<Hit, decltype(&legacy_ellipse_hit)>);

static void check_oracle_edges(const Raw &raw, int precision) {
    Case c;
    c.flag = 1; c.direction = c.angle = ellipse::double_from_bits(UINT64_C(0x7fefffffffffffff));
    auto theta = raw.theta(c, precision);
    ellipse::require(!Raw::infinite(theta) && Raw::infinite(raw.n64(theta)),
                     "oracle theta retains ext80 overflow instead of premature N64");
    c.direction = ellipse::double_from_bits(UINT64_C(0x43b0000000000000));
    c.angle = ellipse::double_from_bits(UINT64_C(0xc3afffffffffffff));
    ellipse::require(raw.stored(raw.theta(c, precision)) == 128.0,
                     "oracle cancellation occurs before operand precision rounding");
    c = Case{}; c.radius = c.width = c.length = 1;
    c.direction = ellipse::double_from_bits(UINT64_C(0x7ff0000000000001));
    ellipse::require(raw.hit(c, precision) == 1, "flag zero ignores signaling NaN direction");
    c.flag = -1;
    ellipse::require(raw.hit(c, precision) == 0, "negative nonzero flag uses NaN direction");
    c.flag = 0;
    c.coordinates[6] = c.coordinates[7] = ellipse::float_from_bits(0x7f800000);
    ellipse::require(raw.hit(c, precision) == 0, "both infinite axes use division, not FPATAN");
    c = Case{}; c.radius = c.width = c.length = 1;
    c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = ellipse::float_from_bits(0x3f7fffff);
    ellipse::require(raw.hit(c, precision) == 1, "one-ULP inside threshold");
    c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = ellipse::float_from_bits(0x3f800001);
    ellipse::require(raw.hit(c, precision) == 0, "one-ULP outside threshold");
}

static void check_states(Hit hit, const std::vector<Case> &cases, int &count) {
    for (int precision : {24, 53, 0, 64, -1}) {
        for (int index : {0, 75000, 80000, 99999}) {
            int result = 123;
            bool valid = precision == 24 || precision == 53;
            ellipse::require(preserved("ellipse success/error", [&] {
                int status = ellipse::call(hit, cases[index], precision, &result);
                return status == (valid ? 0 : -1) &&
                    (valid ? (result == 0 || result == 1) : result == 123) ? 0 : 1;
            }), "ellipse caller state and status/output");
            ++count;
        }
        ellipse::require(preserved("ellipse null output", [&] {
            return ellipse::call(hit, cases[0], precision, nullptr) == -1 ? 0 : 1;
        }), "null-output caller state and error status");
        ++count;
    }
}

static void compare_all(void *library, const std::vector<Case> &cases) {
    const auto select = ellipse::symbol<decltype(&legacy_math_set_backend)>(library, "legacy_math_set_backend");
    const auto dispatch = ellipse::symbol<Hit>(library, "legacy_ellipse_hit");
    std::vector<int> software_expected[2];
    int state_count = 0;
    for (const char *backend : {"software", "hardware"}) {
        Raw raw(library, backend);
        auto hit = ellipse::symbol<Hit>(library, std::string(backend) + "_ellipse_hit");
        bool software = backend[0] == 's';
        ellipse::require(select(software ? 1 : 2) == 0, "select backend");
        for (int precision : {24, 53}) {
            check_oracle_edges(raw, precision);
            auto &expected_software = software_expected[precision == 24 ? 0 : 1];
            uint64_t hash = UINT64_C(1469598103934665603);
            int hits = 0;
            for (size_t i = 0; i < cases.size(); ++i) {
                int expected = raw.hit(cases[i], precision), actual = -1, dispatched = -1;
                if (software) expected_software.push_back(expected);
                ellipse::require(ellipse::same_boolean(expected_software[i], expected), "raw backend bool parity");
                int status = ellipse::call(hit, cases[i], precision, &actual);
                int dispatch_status = ellipse::call(dispatch, cases[i], precision, &dispatched);
                if (status || dispatch_status || !ellipse::same_boolean(expected, actual) ||
                    !ellipse::same_boolean(expected, dispatched)) {
                    std::fprintf(stderr, "FAIL %s PC%d case%zu status%d/%d expected%d actual%d dispatch%d\n",
                                 backend, precision, i, status, dispatch_status, expected, actual, dispatched);
                    std::exit(1);
                }
                if (i < ellipse::OriginalCases) {
                    hits += actual;
                    hash = (hash ^ uint64_t(actual)) * UINT64_C(1099511628211);
                }
            }
            const uint64_t expected_hash = precision == 24 ? UINT64_C(0xf863cc87e98d88c4) : UINT64_C(0xd296421446bfd659);
            ellipse::require(hash == expected_hash, "preserved original raw-graph bool fingerprint");
            std::printf("PASS %s PC%d: %zu cases direct+dispatch; original192305 hits%d hash%016llx\n",
                        backend, precision, cases.size(), hits, static_cast<unsigned long long>(hash));
        }
        check_states(hit, cases, state_count);
        check_states(dispatch, cases, state_count);
    }
    // Flip exactly bit zero of a real false oracle result, using the live comparator.
    const auto &expected = software_expected[0];
    auto zero = std::find(expected.begin(), expected.end(), 0);
    ellipse::require(zero != expected.end() && !ellipse::same_boolean(*zero, *zero ^ 1),
                     "comparator must reject a one-bit false->true mutation");
    ellipse::require(!ellipse::same_boolean(1, 3), "comparator rejects noncanonical booleans");
    ellipse::require(select(0) == 0, "restore automatic backend");
    std::printf("PASS comparator rejects exact one-bit false->true mutation; %d full dirty-state/status/output checks\n", state_count);
}

static void benchmark(void *library, const std::vector<Case> &cases) {
    const Raw raw(library, "hardware");
    const auto hit = ellipse::symbol<Hit>(library, "hardware_ellipse_hit");
    constexpr int Count = 400000;
    for (int precision : {24, 53}) {
        int raw_sum = 0, fused_sum = 0;
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < Count; ++i) raw_sum += raw.hit(cases[i % 256], precision);
        auto middle = std::chrono::steady_clock::now();
        for (int i = 0; i < Count; ++i) {
            int result = -1;
            ellipse::require(ellipse::call(hit, cases[i % 256], precision, &result) == 0, "benchmark status");
            fused_sum += result;
        }
        auto end = std::chrono::steady_clock::now();
        ellipse::require(raw_sum == fused_sum, "benchmark fixed payload checksum");
        double raw_ns = std::chrono::duration<double, std::nano>(middle - start).count() / Count;
        double fused_ns = std::chrono::duration<double, std::nano>(end - middle).count() / Count;
        std::printf("BENCH PC%d raw %.2f ns/block fused %.2f ns/block checksum%d/%d calls%d same256payload; not FPS\n",
                    precision, raw_ns, fused_ns, raw_sum, fused_sum, Count);
    }
}

int main(int argc, char **argv) {
    if (argc < 2 || argc > 3 || (argc == 3 && std::strcmp(argv[2], "--benchmark"))) {
        std::fprintf(stderr, "Usage: native_ellipse_probe /absolute/library.so [--benchmark]\n"); return 2;
    }
    void *library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library) { std::fprintf(stderr, "%s\n", dlerror()); return 2; }
    auto cases = ellipse::original_cases();
    ellipse::append_graph_edges(cases);
    std::printf("Corpus original=%zu additional=%zu seed=c5a12488d9a753ef; raw-native graph, NOT CLR2 capture\n",
                ellipse::OriginalCases, cases.size() - ellipse::OriginalCases);
    compare_all(library, cases);
    if (argc == 3) benchmark(library, cases);
    dlclose(library);
    return 0;
}
