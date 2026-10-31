// 2026 Linux x64 geometry gate; library is loaded only from the explicit CLI path.
#include "native_circle_coarse_probe.h"
static void compare_case(const RawApi &oracle, const GeometryApi &api, const float *f, int region, int precision) {
    double expected = oracle.distance(f, precision), actual = api.measure(f, precision);
    if (double_bits(expected) != double_bits(actual)) {
        std::fprintf(stderr, "distance PC%d expected=%016llx actual=%016llx\n", precision,
                     (unsigned long long)double_bits(expected), (unsigned long long)double_bits(actual));
        require(false, "exact distance bits, including NaN payload");
    }
    require(oracle.hit(f, region, precision) == api.hit(f, region, precision), "circle raw oracle");
}
static void randomized(const RawApi &oracle, const GeometryApi &software, const GeometryApi &hardware) {
    const uint32_t edges[] = {0,0x80000000,1,0x80000001,0x7f7fffff,0xff7fffff,0x7f800000,
        0xff800000,0x7fc00001,0xffc12345,0x7f800001,0x00800000,0x3f800000,0xbf800000};
    for (int precision : {24,53}) for (int n = 0; n < 52000; ++n) {
        float f[9];
        for (float &value : f) {
            uint32_t bits = n < 2000 ? edges[random_bits()%14] : random_bits();
            if (n >= 2000 && (bits & 0x7f800000) == 0x7f800000) bits ^= 0x00800000;
            value = float_bits(bits);
        }
        if (n%2 == 0) { f[1] = f[0]; f[3] = f[2]; }
        int region = n%3 == 0 ? INT32_MAX : n%3 == 1 ? INT32_MIN : int32_t(random_bits());
        compare_case(oracle, software, f, region, precision);
        compare_case(oracle, hardware, f, region, precision);
    }
    std::puts("PASS randomized: 104000 cases x software/hardware; exact NaN bits; seed=42");
}
static void boundaries(const RawApi &oracle, const GeometryApi &software, const GeometryApi &hardware) {
    int count = 0;
    for (int p : {24,53}) for (int r : {0,1,-1,16777217,-16777217,INT32_MAX,INT32_MIN})
        for (uint32_t scale : {0u,0x80000000u,0x3f800000u,0xbf800000u,0x7f800000u,0xff800000u,0x7fc12345u,1u,0x7f7fffffu})
            for (uint32_t radius : {0x409fffffu,0x40a00000u,0x40a00001u,0u,0x80000000u,0x7fc00001u,0x7f800000u}) {
                float f[] = {3,3,4,4,0,0,float_bits(radius),1,float_bits(scale)};
                compare_case(oracle, software, f, r, p); compare_case(oracle, hardware, f, r, p); ++count;
            }
    float equality[] = {3,3,4,4,0,0,5,1,1};
    require(hardware.hit(equality,0,53) == 0, "strict equality does not hit");
    equality[6] = float_bits(0x40a00001);
    require(hardware.hit(equality,0,53) == 1, "next radius does hit");
    std::printf("PASS circle boundaries: %d cases x software/hardware\n", count);
}
static void state_and_errors(const GeometryApi &api, const char *name) {
    for (int p : {24,53}) for (float value : {0.f,1.f,-3.f,__builtin_inff(),__builtin_nanf("1")}) {
        double result; int hit;
        require(preserved(name, [&] { return api.distance(value,2,3,4,5,6,p,&result); }), "distance state");
        require(preserved(name, [&] { return api.circle_hit(value,2,3,4,5,6,value,value,INT32_MIN,value,p,&hit); }), "circle state");
    }
    for (int p : {-100,0,23,25,52,54,64}) {
        double result = 73; int hit = 73;
        require(preserved(name, [&] { return api.distance(1,2,3,4,5,6,p,&result) == -1 ? 0 : -1; }), "invalid distance state");
        require(result == 73, "invalid distance unchanged output");
        require(preserved(name, [&] { return api.circle_hit(1,2,3,4,5,6,1,1,INT32_MAX,1,p,&hit) == -1 ? 0 : -1; }), "invalid circle state");
        require(hit == 73, "invalid circle unchanged output");
    }
    for (int p : {-1,24,53}) {
        require(preserved(name, [&] { return api.distance(1,2,3,4,5,6,p,nullptr) == -1 ? 0 : -1; }), "null distance state");
        require(preserved(name, [&] { return api.circle_hit(1,2,3,4,5,6,1,1,0,1,p,nullptr) == -1 ? 0 : -1; }), "null circle state");
    }
    std::printf("PASS state/errors %s: 40 full-stack CW/SW/tags/8regs/MXCSR operations\n", name);
}
static void timings(const RawApi &raw, const GeometryApi &fused) {
    const float f[] = {2,2,3,3,5,7,2,1,1}; volatile double sink = 0;
    for (int p : {24,53}) for (int mode : {0,1}) {
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < 200000; ++i) sink = mode ? fused.measure(f,p) : raw.distance(f,p);
        double ns = std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-start).count()/200000;
        std::printf("INFO timing distance PC%d %s %.2f ns/block (no performance assertion)\n", p,mode?"fused":"raw hardware",ns);
    }
    require(double_bits(sink) == double_bits(5.0), "timing result");
    for (int p : {24,53}) for (int mode : {0,1}) {
        auto start = std::chrono::steady_clock::now();
        for (int i = 0; i < 200000; ++i) sink = mode ? fused.hit(f,3,p) : raw.hit(f,3,p);
        double ns = std::chrono::duration<double,std::nano>(std::chrono::steady_clock::now()-start).count()/200000;
        std::printf("INFO timing circle PC%d %s %.2f ns/block (no performance assertion)\n", p,mode?"fused":"raw hardware",ns);
    }
    require(sink == 0, "circle timing strict equality");
}
int main(int argc, char **argv) {
    if (argc != 2) { std::fprintf(stderr,"Usage: native_geometry_probe /absolute/kernel.so\n"); return 2; }
    void *library = dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);
    if (!library) { std::fprintf(stderr,"%s\n",dlerror()); return 2; }
    std::printf("Loaded explicit kernel: %s\n",argv[1]);
    RawApi oracle(library,"software"), raw_hardware(library,"hardware");
    GeometryApi software(library,"software"), hardware(library,"hardware"), dispatch(library,"legacy");
    randomized(oracle,software,hardware); boundaries(oracle,software,hardware);
    coarse_gate(oracle,software,hardware,dispatch);
    state_and_errors(software,"software"); state_and_errors(hardware,"hardware");
    coarse_state(software,"software"); coarse_state(hardware,"hardware");
    auto select = required_symbol<decltype(&legacy_math_set_backend)>(library,"legacy_math_set_backend");
    for (int backend : {1,2,0}) {
        require(select(backend) == 0,"backend selection"); state_and_errors(dispatch,"legacy");
        coarse_state(dispatch,"legacy");
        const float f[] = {2,4,3,5,1,2,4,1,1};
        for (int p : {24,53}) compare_case(oracle,dispatch,f,16777217,p);
    }
    timings(raw_hardware,hardware); coarse_timings(raw_hardware,hardware);
    dlclose(library); std::puts("PASS native geometry gate");
}
