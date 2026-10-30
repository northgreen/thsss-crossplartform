// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#ifndef THSSS_SOFTWARE_X87_H
#define THSSS_SOFTWARE_X87_H
#include <stdint.h>
#if defined(_WIN32)
#define SOFTWARE_API __declspec(dllexport)
#else
#define SOFTWARE_API __attribute__((visibility("default")))
#endif
typedef struct software_ext80 {
    uint64_t significand;
    uint16_t signExp;
    uint16_t reserved[3];
} software_ext80;
// Raw operations: 0 on success, -1 on invalid pointer/precision without writing out.
// Precision is 24 or 53 significant bits; raw results retain the ext80 exponent.
// Padding is zeroed on output. Scalar invalid precision returns quiet NaN.
// No errno contract. Each call owns its rounding/exception state; no host FPU use.
#ifdef __cplusplus
extern "C" {
#endif
SOFTWARE_API double software_sin(double x);
SOFTWARE_API double software_cos(double x);
SOFTWARE_API double software_atan2(double y, double x);
SOFTWARE_API double software_sqrt(double x, int precision);
SOFTWARE_API double software_add(double a, double b, int precision);
SOFTWARE_API double software_sub(double a, double b, int precision);
SOFTWARE_API double software_mul(double a, double b, int precision);
SOFTWARE_API double software_div(double a, double b, int precision);
SOFTWARE_API float software_move(float p, float v, double angle, float scale, int precision);
SOFTWARE_API double software_direction(float px, float py, float tx, float ty, int precision);
SOFTWARE_API int software_ext_load_double(double value, software_ext80 *out);
SOFTWARE_API int software_ext_load_float(float value, software_ext80 *out);
SOFTWARE_API int software_ext_store_double(const software_ext80 *value, double *out);
SOFTWARE_API int software_ext_store_float(const software_ext80 *value, float *out);
SOFTWARE_API int software_ext_sin(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int software_ext_cos(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int software_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out);
SOFTWARE_API int software_ext_sqrt(const software_ext80 *value, int precision, software_ext80 *out);
SOFTWARE_API int software_ext_add(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int software_ext_sub(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int software_ext_mul(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int software_ext_div(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
#ifdef __cplusplus
}
#endif
#endif
