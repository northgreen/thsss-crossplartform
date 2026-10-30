// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
// THSSS freestanding memory support. No platform runtime dependencies.
#include <stddef.h>

#if defined(_WIN32)
// COFF floating-point ABI marker, not a CRT implementation or export.
extern "C" { int _fltused = 0; }
#endif

extern "C" __attribute__((visibility("hidden")))
void *memcpy(void *destination, const void *source, size_t count) {
    auto out = static_cast<unsigned char *>(destination);
    auto in = static_cast<const unsigned char *>(source);
    for (size_t i = 0; i < count; ++i) out[i] = in[i];
    return destination;
}

extern "C" __attribute__((visibility("hidden")))
void *memset(void *destination, int value, size_t count) {
    auto out = static_cast<unsigned char *>(destination);
    for (size_t i = 0; i < count; ++i) out[i] = static_cast<unsigned char>(value);
    return destination;
}

extern "C" __attribute__((visibility("hidden")))
void *memmove(void *destination, const void *source, size_t count) {
    auto out = static_cast<unsigned char *>(destination);
    auto in = static_cast<const unsigned char *>(source);
    if (reinterpret_cast<size_t>(out) <= reinterpret_cast<size_t>(in))
        return memcpy(destination, source, count);
    while (count != 0) { --count; out[count] = in[count]; }
    return destination;
}
