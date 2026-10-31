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
// No errno contract. Software calls own their rounding/exception state without
// host FPU use; hardware arithmetic saves/restores the complete caller x87 state.
#ifdef __cplusplus
extern "C" {
#endif
// Geometry preserves four independent source-coordinate captures, PC24/53,
// binary64 sqrt argument/return and conv.r8 products. Invalid args leave out unchanged.
SOFTWARE_API int software_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out);
SOFTWARE_API int legacy_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out);
SOFTWARE_API int software_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale, int target_region, float target_scale, int precision, int *out);
SOFTWARE_API int legacy_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale, int target_region, float target_scale, int precision, int *out);
// Ellipse keeps ten independent coordinate captures and all explicit IEEE boundaries.
SOFTWARE_API int software_ellipse_hit(double direction, double angle, int angle_flag, float px1, float px2, float py1, float py2, float dtx, float dty, float own_y, float own_x, float gtx, float gty, int target_region, float target_scale, float radius, float width, float length, int precision, int *out);
SOFTWARE_API int legacy_ellipse_hit(double direction, double angle, int angle_flag, float px1, float px2, float py1, float py2, float dtx, float dty, float own_y, float own_x, float gtx, float gty, int target_region, float target_scale, float radius, float width, float length, int precision, int *out);
SOFTWARE_API double software_sin(double x);
// Motion returns final OriginalPosition, with independent getter captures and IEEE stores.
// Invalid precision or either null output leaves both unchanged. Aliased outputs: Y wins.
SOFTWARE_API int software_motion_step(float ox1, int left1, float oy1, int top1, float velocity_x, double direction_x, float scale_x, float ox2, int left2, float oy2, int top2, float velocity_y, double direction_y, float scale_y, int set_left, int set_top, int precision, float *out_x, float *out_y);
SOFTWARE_API int legacy_motion_step(float ox1, int left1, float oy1, int top1, float velocity_x, double direction_x, float scale_x, float ox2, int left2, float oy2, int top2, float velocity_y, double direction_y, float scale_y, int set_left, int set_top, int precision, float *out_x, float *out_y);
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
// Dispatch: 0=auto, 1=software, 2=hardware. Getter returns resolved 1/2.
// Setter returns -1 for unknown, -2 for unavailable; selection is unchanged.
SOFTWARE_API int legacy_math_set_backend(int backend);
SOFTWARE_API int legacy_math_get_backend(void);
SOFTWARE_API int legacy_ext_load_double(double value, software_ext80 *out);
SOFTWARE_API int legacy_ext_load_float(float value, software_ext80 *out);
SOFTWARE_API int legacy_ext_store_double(const software_ext80 *value, double *out);
SOFTWARE_API int legacy_ext_store_float(const software_ext80 *value, float *out);
SOFTWARE_API int legacy_ext_sin(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int legacy_ext_cos(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int legacy_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out);
SOFTWARE_API int legacy_ext_sqrt(const software_ext80 *value, int precision, software_ext80 *out);
SOFTWARE_API int legacy_ext_add(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int legacy_ext_sub(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int legacy_ext_mul(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int legacy_ext_div(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
#if defined(__x86_64__)
SOFTWARE_API int hardware_motion_step(float ox1, int left1, float oy1, int top1, float velocity_x, double direction_x, float scale_x, float ox2, int left2, float oy2, int top2, float velocity_y, double direction_y, float scale_y, int set_left, int set_top, int precision, float *out_x, float *out_y);
SOFTWARE_API int hardware_distance(float px1, float px2, float py1, float py2, float tx, float ty, int precision, double *out);
SOFTWARE_API int hardware_circle_hit(float px1, float px2, float py1, float py2, float tx, float ty, float radius, float scale, int target_region, float target_scale, int precision, int *out);
SOFTWARE_API int hardware_ellipse_hit(double direction, double angle, int angle_flag, float px1, float px2, float py1, float py2, float dtx, float dty, float own_y, float own_x, float gtx, float gty, int target_region, float target_scale, float radius, float width, float length, int precision, int *out);
SOFTWARE_API int hardware_ext_load_double(double value, software_ext80 *out);
SOFTWARE_API int hardware_ext_load_float(float value, software_ext80 *out);
SOFTWARE_API int hardware_ext_store_double(const software_ext80 *value, double *out);
SOFTWARE_API int hardware_ext_store_float(const software_ext80 *value, float *out);
SOFTWARE_API int hardware_ext_sin(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int hardware_ext_cos(const software_ext80 *value, software_ext80 *out);
SOFTWARE_API int hardware_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out);
SOFTWARE_API int hardware_ext_sqrt(const software_ext80 *value, int precision, software_ext80 *out);
SOFTWARE_API int hardware_ext_add(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int hardware_ext_sub(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int hardware_ext_mul(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
SOFTWARE_API int hardware_ext_div(const software_ext80 *a, const software_ext80 *b, int precision, software_ext80 *out);
#endif
#ifdef __cplusplus
}
#endif
#endif
