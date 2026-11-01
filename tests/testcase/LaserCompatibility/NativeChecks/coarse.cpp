// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "raw_support.h"

extern "C" int diagnostic_laser_reject(float, float, float, float, float, float,
                                      int, float, float, float, int, int *);
static unsigned rejected = 0, fallback = 0;
static Kernel state_kernel;
#if defined(__aarch64__)
static constexpr const char *HardwareKernel = "software_laser_point_hit";
static constexpr const char *HardwareRaw = "software";
static constexpr int RandomBackend = 1;
#else
static constexpr const char *HardwareKernel = "hardware_laser_point_hit";
static constexpr const char *HardwareRaw = "hardware";
static constexpr int RandomBackend = 2;
#endif
static int far_state(float, float, float, float, float, float, int, float, float, float,
                     int precision, int *out) {
    return state_kernel(1000, -1000, 0, 0, 0, 0, 1, 1, 1, 1, precision, out);
}

static void verify_case(const float *v, int region, int expected_guard = -1) {
    const bool reject = call(diagnostic_laser_reject, v, region, 24, nullptr);
    require(expected_guard < 0 || reject == bool(expected_guard), "guard domain/boundary expectation");
    reject ? ++rejected : ++fallback;
    Raw sw("software"), hw(HardwareRaw);
    for (int precision : {24, 53}) {
        const int expected = sw.graph(v, region, precision);
        require(expected == hw.graph(v, region, precision), "raw backend mismatch");
        require(!reject || !expected, "coarse rejection removed raw hit");
        for (const char *name : {"software_laser_point_hit", HardwareKernel, "legacy_laser_point_hit"}) {
            int out = -9;
            require(call(symbol<Kernel>(name), v, region, precision, &out) == 0 && out == expected,
                    "coarse corpus fused/raw mismatch");
        }
    }
}

int main(int argc, char **argv) {
    require(argc == 2, "library path required");
    library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    require(library != nullptr, "dlopen");
    const auto select = symbol<int (*)(int)>("legacy_math_set_backend");
    // Stable captures, fractional coordinates, signed subnormals and varied bounds.
    for (unsigned i = 0; i < 120000; ++i) {
        float v[9];
        for (unsigned j = 0; j < 6; ++j)
            v[j] = bits((next() & 0x807fffff) | ((110 + next() % 37) << 23));
        v[3] = v[2]; v[5] = v[4];
        v[6] = bits((next() & 0x7fffff) | ((next() % 133) << 23));
        v[7] = bits((next() & 0x7fffff) | ((next() % 133) << 23));
        v[8] = bits((next() & 0x7fffff) | ((next() % 133) << 23));
        require(select(i & 1 ? 1 : RandomBackend) == 0, "dispatch selection");
        verify_case(v, next() % 4096);
    }
    for (int axis : {0, 1}) for (int sign : {-1, 1}) for (int delta : {-1, 0, 1}) {
        float v[9] = {0, 0, 0, 0, 0, 0, 1, 5, 1};
        v[axis] = float(sign * (21 + delta));
        verify_case(v, 0, delta > 0);
        v[axis] += float(sign) * 0.5f;
        verify_case(v, 0, delta > 0);
    }
    for (uint32_t coordinate : {0u, 0x80000000u, 1u, 0x80000001u, 0x3f000000u, 0xbf000000u}) {
        float v[9] = {1000, 0, bits(coordinate), bits(coordinate), 0, 0, 0, bits(1), bits(1)};
        verify_case(v, 0, 1);
        v[3] = bits(coordinate ^ 0x80000000);
        verify_case(v, 0, 0);
    }
    const uint32_t bad[] = {0xbf800000, 0x80000001, 0x49800001, 0x7f800000,
        0xff800000, 0x7fc12345, 0x7f800001, 0xff7fffff};
    for (unsigned slot : {6u, 7u, 8u}) for (uint32_t word : bad) {
        float v[9] = {1000, 0, 0, 0, 0, 0, 1, 1, 1};
        v[slot] = bits(word); verify_case(v, 0, 0);
    }
    for (unsigned slot : {6u, 7u, 8u}) {
        float v[9] = {1000, 0, 0, 0, 0, 0, 1, 1, 1};
        v[slot] = bits(0x80000000); verify_case(v, 0, 1);
    }
    for (uint32_t word : {0x7f800000u, 0xff800000u, 0x7fc01234u, 0x7f800001u, 0x49800001u}) {
        for (unsigned slot : {0u, 1u, 2u, 4u}) {
            float v[9] = {1000, 0, 0, 0, 0, 0, 1, 1, 1};
            v[slot] = bits(word);
            v[3] = v[2]; v[5] = v[4];
            verify_case(v, 0, 0);
        }
    }
    for (int region : {-1, INT_MIN, INT_MAX, 1048577}) {
        float v[9] = {1000, 0, 0, 0, 0, 0, 0, 0, 0};
        verify_case(v, region, 0);
    }
    for (uint32_t word : {0x49800000u, 0x49800001u}) {
        float v[9] = {bits(word), 0, 0, 0, 0, 0, 0, 0, 0};
        verify_case(v, 0, word == 0x49800000u);
    }
    float limit[9] = {1048576, 0, -1048576, -1048576, 0, 0, 1, 0, 1};
    verify_case(limit, 1048576, 1);
    limit[7] = 1; verify_case(limit, 1048576, 0);
    limit[6] = 0; limit[7] = 1048576; verify_case(limit, 0, 1);
    limit[8] = bits(0x3f800001); verify_case(limit, 0, 0);
    // Distinct captures must retain the old non-square graph.
    float mutable_capture[9] = {1000, 1000, 0, 0, 0, 0, 1, 1, 1};
    for (int slot : {2, 3, 4, 5}) {
        mutable_capture[slot] = 2000; verify_case(mutable_capture, 0, 0);
        mutable_capture[slot] = 0;
    }
    float contact[9] = {3, 4, 0, 0, 0, 0, 1, 5, 1};
    for (uint32_t radius : {0x409fffffu, 0x40a00000u, 0x40a00001u}) {
        contact[7] = bits(radius); verify_case(contact, 0, 0);
    }
    for (Kernel kernel : {symbol<Kernel>("software_laser_point_hit"),
                          symbol<Kernel>(HardwareKernel), symbol<Kernel>("legacy_laser_point_hit")}) {
        state_kernel = kernel;
        check_edges(kernel);
        for (int mode : {0, 1, 2}) {
#if defined(__aarch64__)
            if (mode == 2) {
                require(select(mode) == -2, "ARM hardware rejection");
                continue;
            }
#endif
            require(select(mode) == 0, "state dispatch selection");
            for (int precision : {24, 53}) {
                check_state(far_state, precision);
                require(far_state(0,0,0,0,0,0,0,0,0,0,precision,nullptr) == -1, "far null");
            }
            for (int precision : {-1, 0, 23, 54, 64}) {
                int out = 73;
                require(far_state(0,0,0,0,0,0,0,0,0,0,precision,&out) == -1 && out == 73,
                        "far invalid precision must precede rejection");
            }
        }
    }
    require(rejected > 10000 && fallback > 1000, "vacuous guard corpus");
#if defined(__aarch64__)
    std::printf("PASS ARM coarse portable raw/dispatch PC24/53; reject=%u fallback=%u; fast FPCR/FPSR/errors (no x87 hardware claim)\n", rejected, fallback);
#else
    std::printf("PASS coarse 120000 random + edges, both PCs/raw backends/dispatch; reject=%u fallback=%u; fast full dirty x87/MXCSR/errors\n", rejected, fallback);
#endif
    dlclose(library);
}
