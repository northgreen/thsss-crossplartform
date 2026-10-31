#include <cstdint>

extern "C" std::uint32_t load_codec_get_mxcsr() {
#if defined(__x86_64__)
    std::uint32_t value;
    asm volatile("stmxcsr %0" : "=m"(value));
    return value;
#else
    return 0;
#endif
}

extern "C" void load_codec_set_mxcsr(std::uint32_t value) {
#if defined(__x86_64__)
    asm volatile("ldmxcsr %0" : : "m"(value));
#else
    (void)value;
#endif
}

extern "C" std::uint32_t load_codec_get_x87() {
#if defined(__x86_64__)
    std::uint16_t control, status;
    asm volatile("fnstcw %0; fnstsw %1" : "=m"(control), "=am"(status));
    return control | (std::uint32_t(status) << 16);
#else
    return 0;
#endif
}
