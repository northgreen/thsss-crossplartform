// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "software_x87.h"

#if defined(__x86_64__)
static constexpr int automatic_backend = 2;
#else
static constexpr int automatic_backend = 1;
#endif
static int selected_backend = automatic_backend;

extern "C" int legacy_math_set_backend(int backend) {
    if (backend < 0 || backend > 2) return -1;
    if (backend == 2 && automatic_backend != 2) return -2;
    __atomic_store_n(&selected_backend, backend == 0 ? automatic_backend : backend, __ATOMIC_RELAXED);
    return 0;
}
extern "C" int legacy_math_get_backend(void) {
    return __atomic_load_n(&selected_backend, __ATOMIC_RELAXED);
}

#if defined(__x86_64__)
#define DISPATCH(name, arguments) \
    if (legacy_math_get_backend() == 2) return hardware_ext_##name arguments; \
    return software_ext_##name arguments;
#else
#define DISPATCH(name, arguments) return software_ext_##name arguments;
#endif
#define CONVERT(name, type) \
extern "C" int legacy_ext_load_##name(type value, software_ext80 *out) { \
    DISPATCH(load_##name, (value, out)) \
} \
extern "C" int legacy_ext_store_##name(const software_ext80 *value, type *out) { \
    DISPATCH(store_##name, (value, out)) \
}
CONVERT(float, float)
CONVERT(double, double)
#define UNARY(name) \
extern "C" int legacy_ext_##name(const software_ext80 *value, software_ext80 *out) { \
    DISPATCH(name, (value, out)) \
}
UNARY(sin)
UNARY(cos)
extern "C" int legacy_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out) {
    DISPATCH(atan2, (y, x, out))
}
extern "C" int legacy_ext_sqrt(const software_ext80 *value, int precision, software_ext80 *out) {
    DISPATCH(sqrt, (value, precision, out))
}
#define BINARY(name) \
extern "C" int legacy_ext_##name(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out) { \
    DISPATCH(name, (a, b, precision, out)) \
}
BINARY(add)
BINARY(sub)
BINARY(mul)
BINARY(div)
