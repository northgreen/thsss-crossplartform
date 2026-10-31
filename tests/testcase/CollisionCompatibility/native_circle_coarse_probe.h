// 2026 conservative-reject gate. The runner injects the exact production helper.
#pragma once
#include "native_geometry_oracle.h"
#include <chrono>
#define main existing_scalar_state_main
#include "../GameplayMath/backend_state.cpp"
#undef main

// Definitions are injected from native_geometry.cpp by run_native.py, not copied.
uint32_t circle_float_bits(float value);
bool circle_integer_reject(float px1, float px2, float py1, float py2, float tx, float ty,
                          float radius, float scale, int target_region, float target_scale);
static uint64_t random_state = 42;
static uint32_t random_bits() {
    random_state ^= random_state << 13; random_state ^= random_state >> 7;
    random_state ^= random_state << 17; return uint32_t(random_state);
}

static bool coarse_reject(const float *f, int region) {
    return circle_integer_reject(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],region,f[8]);
}

static void coarse_case(const RawApi &raw, const GeometryApi *apis, const float *f,
                        int region, int precision, int expected_fast, int &fast, int &fallback) {
    const bool rejected = coarse_reject(f,region);
    if (expected_fast >= 0) require(rejected == bool(expected_fast), "coarse guard branch");
    const int expected = raw.hit(f,region,precision);
    require(!rejected || expected == 0, "integer fast path can only reject a raw-graph miss");
    for (int i = 0; i < 3; ++i) require(apis[i].hit(f,region,precision) == expected, "coarse exact raw graph");
    if (rejected) ++fast; else ++fallback;
}

static void coarse_gate(const RawApi &raw, const GeometryApi &software,
                        const GeometryApi &hardware, const GeometryApi &dispatch) {
    const GeometryApi apis[] = {software,hardware,dispatch};
    int fast = 0, fallback = 0, directed = 0;
    for (int p : {24,53}) {
        // Actual hit/equality boundary, then both sides of the integer margin.
        for (float x : {1.9999999f,2.f,2.0000002f,17.999998f,18.f,18.999998f,19.f,-19.f}) {
            float f[] = {x,x,0,0,0,0,2,1,0};
            coarse_case(raw,apis,f,0,p,(x == 19.f || x == -19.f) ? 1 : 0,fast,fallback); ++directed;
            float y[] = {0,0,x,x,0,0,2,1,0};
            coarse_case(raw,apis,y,0,p,(x == 19.f || x == -19.f) ? 1 : 0,fast,fallback); ++directed;
        }
        // Every float argument sees each special value, including signaling NaNs.
        const uint32_t edges[] = {0,0x80000000,1,0x80000001,0x007fffff,0x00800000,
            0xbf800000,0x497fffff,0x49800000,0x49800001,0xc97fffff,0xc9800000,0xc9800001,
            0x7f7fffff,0xff7fffff,0x7f800000,0xff800000,0x7fc00001,0xffc12345,0x7f800001,0xff800001};
        for (int slot = 0; slot < 9; ++slot) for (uint32_t bits : edges) {
            float f[] = {100,100,200,200,0,0,2,1,1};
            f[slot] = float_bits(bits);
            const bool coordinate_domain = (bits & 0x7fffffff) <= 0x49800000;
            const bool small_nonnegative = bits == 0 || bits == 0x80000000 || bits == 1 ||
                bits == 0x007fffff || bits == 0x00800000;
            const int expected_fast = slot < 4 ? 0 : slot < 6 ? coordinate_domain : small_nonnegative;
            coarse_case(raw,apis,f,1,p,expected_fast,fast,fallback); ++directed;
            if (slot < 4) {
                f[slot ^ 1] = f[slot]; // Also test the actual domain guard, not only capture mismatch.
                coarse_case(raw,apis,f,1,p,coordinate_domain,fast,fallback); ++directed;
            }
        }
        // U exactly at/below/above 2^20, and products individually as large as 2^40.
        for (int r : {-1,0,1,1048575,1048576,1048577,INT32_MAX,INT32_MIN})
            for (uint32_t radius : {0u,0x497ffff0u,0x49800000u,0x49800001u})
                for (float scale : {0.f,1.f,1048576.f}) {
                    float f[] = {-1048576,-1048576,0,0,1048576,0,float_bits(radius),scale,1};
                    const uint64_t radius_ceil = radius == 0 ? 0 : radius == 0x497ffff0 ? 1048575 : 1048576;
                    const bool expected_fast = r >= 0 && r <= 1048576 && radius <= 0x49800000 &&
                        radius_ceil * uint64_t(scale) + uint64_t(r) <= 1048576;
                    coarse_case(raw,apis,f,r,p,expected_fast,fast,fallback); ++directed;
                }
        for (int n = 0; n < 50000; ++n) {
            float f[9];
            // Finite dyadics inside the proof domain, with frequent genuine near hits.
            f[0] = float(int32_t(random_bits()%16777217)-8388608)/8;
            f[2] = float(int32_t(random_bits()%16777217)-8388608)/8;
            f[4] = n%2 ? f[0]+float(int(random_bits()%65)-32)/8 : -f[0];
            f[5] = n%2 ? f[2]+float(int(random_bits()%65)-32)/8 : -f[2];
            f[1] = f[0]; f[3] = f[2];
            f[6] = float(random_bits()%257)/8; f[7] = float(random_bits()%33)/8;
            f[8] = float(random_bits()%33)/8;
            int region = int(random_bits()%17);
            if (n%11 == 0) f[1] = float_bits(circle_float_bits(f[1]) ^ 1); // Changed capture fallback.
            if (n%13 == 0) f[6] = float_bits(0x80000000); // Accepted negative zero.
            if (n%17 == 0) f[7] = float_bits(1); // Positive subnormal ceil=1.
            coarse_case(raw,apis,f,region,p,-1,fast,fallback);
        }
    }
    require(fast > 40000 && fallback > 40000, "both coarse branches exercised");
    std::printf("PASS coarse: 100000 randomized + %d directed x software/hardware/legacy; fast=%d fallback=%d\n",
                directed,fast,fallback);
}

static void coarse_state(const GeometryApi &api, const char *name) {
    int count = 0;
    for (int p : {24,53}) for (int mode = 0; mode < 4; ++mode) {
        float f[] = {100,100,200,200,0,0,2,1,1};
        if (mode == 1) f[0] = 1; // Changed capture fallback.
        if (mode == 2) f[6] = float_bits(0x7f800001);
        if (mode == 3) f[6] = -1;
        int hit = 73;
        require(preserved(name,[&] { return api.circle_hit(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],1,f[8],p,&hit); }),
                "coarse full FPU state"); ++count;
        require(preserved(name,[&] { return coarse_reject(f,1) == (mode == 0) ? 0 : -1; }), "integer helper full state"); ++count;
    }
    for (int p : {-1,0,23,25,52,54,64}) {
        int hit = 73;
        require(preserved(name,[&] { return api.circle_hit(100,100,200,200,0,0,2,1,1,1,p,&hit) == -1 ? 0 : -1; }),
                "coarse invalid precision before fast reject"); ++count;
        require(hit == 73, "coarse error does not write output");
    }
    for (int p : {-1,24,53}) {
        require(preserved(name,[&] { return api.circle_hit(100,100,200,200,0,0,2,1,1,1,p,nullptr) == -1 ? 0 : -1; }),
                "coarse null before fast reject"); ++count;
    }
    for (int p : {24,53}) for (int slot = 0; slot < 9; ++slot)
        for (uint32_t special : {0x7f800001u,0xff800001u,0x7fc00001u,0x7f800000u}) {
            float f[] = {100,100,200,200,0,0,2,1,1}; f[slot] = float_bits(special);
            int hit;
            require(preserved(name,[&] { return api.circle_hit(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],1,f[8],p,&hit); }),
                    "all float positions preserve state with SNaN/NaN/Inf"); ++count;
        }
    std::printf("PASS coarse state/errors %s: %d operations\n",name,count);
}

static void coarse_timings(const RawApi &raw, const GeometryApi &fused) {
    const float f[] = {100,100,200,200,0,0,2,1,1};
    volatile int sink = 1;
    for (int p : {24,53}) for (int mode : {0,1}) {
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < 200000; ++i) sink = mode ? fused.hit(f,1,p) : raw.hit(f,1,p);
        const double ns = std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-start).count()/200000;
        std::printf("INFO timing far-circle PC%d %s %.2f ns/block (no game-FPS assertion)\n",p,mode?"coarse":"raw hardware",ns);
    }
    require(sink == 0,"coarse benchmark result");
}
