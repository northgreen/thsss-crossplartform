// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
// Derivative of Bochs/SoftFloat; upstream notices retained unchanged.
#include "fpu_trans.h"
#include "software_x87.h"
static softfloat_status_t state(int precision = 64) {
    softfloat_status_t s{};
    s.softfloat_exceptionMasks = 0x3f;
    s.extF80_roundingPrecision = precision == 24 ? 32 : precision == 53 ? 64 : 80;
    return s;
}
static floatx80 load(double value, softfloat_status_t &s) {
    uint64_t bits;
    __builtin_memcpy(&bits, &value, sizeof bits);
    return f64_to_extF80(bits, &s);
}
static double store(floatx80 value, softfloat_status_t &s) {
    uint64_t bits = extF80_to_f64(value, &s);
    double result;
    __builtin_memcpy(&result, &bits, sizeof result);
    return result;
}
static double invalid_precision() {
    uint64_t bits = UINT64_C(0x7ff8000000000000);
    double result;
    __builtin_memcpy(&result, &bits, sizeof result);
    return result;
}
extern "C" double software_sin(double x) {
    auto s = state(); auto a = load(x, s); fsin(a, s); return store(a, s);
}
extern "C" double software_cos(double x) {
    auto s = state(); auto a = load(x, s); fcos(a, s); return store(a, s);
}
extern "C" double software_atan2(double y, double x) {
    auto s = state(); return store(fpatan(load(x, s), load(y, s), s), s);
}
extern "C" double software_sqrt(double x, int precision) {
    if (precision != 24 && precision != 53) return invalid_precision();
    auto s = state(precision); return store(extF80_sqrt(load(x, s), &s), s);
}
#define BINARY(name, op) \
extern "C" double software_##name(double a, double b, int precision) { \
    if (precision != 24 && precision != 53) return invalid_precision(); \
    auto s = state(precision); return store(extF80_##op(load(a,s), load(b,s), &s), s); \
}
BINARY(add, add)
BINARY(sub, sub)
BINARY(mul, mul)
BINARY(div, div)

static floatx80 load_float(float value, softfloat_status_t &s) {
    uint32_t bits; __builtin_memcpy(&bits, &value, sizeof bits);
    return f32_to_extF80(bits, &s);
}
static float store_float(floatx80 value, softfloat_status_t &s) {
    uint32_t bits = extF80_to_f32(value, &s);
    float result; __builtin_memcpy(&result, &bits, sizeof result); return result;
}
static bool supported_precision(int p) { return p == 24 || p == 53; }
static int bad_argument() { return -1; }
static floatx80 unpack(const software_ext80 &v) {
    floatx80 result{}; result.signif = v.significand; result.signExp = v.signExp;
    return result;
}
static int output_raw(floatx80 value, software_ext80 *out) {
    software_ext80 result{};
    result.significand = value.signif; result.signExp = value.signExp;
    *out = result; return 0;
}
static_assert(sizeof(software_ext80) == 16, "ext80 ABI must be 16 bytes");
extern "C" int software_ext_load_double(double value, software_ext80 *out) {
    if (!out) return bad_argument();
    auto s = state(); return output_raw(load(value, s), out);
}
extern "C" int software_ext_load_float(float value, software_ext80 *out) {
    if (!out) return bad_argument();
    auto s = state(); return output_raw(load_float(value, s), out);
}
extern "C" int software_ext_store_double(const software_ext80 *value, double *out) {
    if (!value || !out) return bad_argument();
    auto s = state(); *out = store(unpack(*value), s); return 0;
}
extern "C" int software_ext_store_float(const software_ext80 *value, float *out) {
    if (!value || !out) return bad_argument();
    auto s = state(); *out = store_float(unpack(*value), s); return 0;
}
#define RAW_TRIG(name, op) \
extern "C" int software_ext_##name(const software_ext80 *value, software_ext80 *out) { \
    if (!value || !out) return bad_argument(); \
    auto s = state(); auto v = unpack(*value); op(v,s); return output_raw(v,out); \
}
RAW_TRIG(sin,fsin)
RAW_TRIG(cos,fcos)
extern "C" int software_ext_atan2(const software_ext80 *y, const software_ext80 *x, software_ext80 *out) {
    if (!y || !x || !out) return bad_argument();
    auto s = state(); return output_raw(fpatan(unpack(*x),unpack(*y),s),out);
}
extern "C" int software_ext_sqrt(const software_ext80 *value, int precision, software_ext80 *out) {
    if (!value || !out || !supported_precision(precision)) return bad_argument();
    auto s = state(precision); return output_raw(extF80_sqrt(unpack(*value),&s),out);
}
#define RAW_BINARY(name) \
extern "C" int software_ext_##name(const software_ext80 *a,const software_ext80 *b,int precision,software_ext80 *out) { \
    if (!a || !b || !out || !supported_precision(precision)) return bad_argument(); \
    auto s = state(precision); return output_raw(extF80_##name(unpack(*a),unpack(*b),&s),out); \
}
RAW_BINARY(add)
RAW_BINARY(sub)
RAW_BINARY(mul)
RAW_BINARY(div)
extern "C" float software_move(float p,float v,double angle,float scale,int precision) {
    if (!supported_precision(precision)) { invalid_precision(); uint32_t bits=0x7fc00000; float result; __builtin_memcpy(&result,&bits,4); return result; }
    auto s = state(precision); auto result = load(angle,s);
    fcos(result,s);
    result = extF80_mul(result,load_float(v,s),&s);
    result = extF80_mul(result,load_float(scale,s),&s);
    result = extF80_add(result,load_float(p,s),&s);
    return store_float(result,s);
}
extern "C" double software_direction(float px,float py,float tx,float ty,int precision) {
    if (!supported_precision(precision)) return invalid_precision();
    auto s = state(precision);
    auto dx = extF80_sub(load_float(px,s),load_float(tx,s),&s);
    auto dy = extF80_sub(load_float(py,s),load_float(ty,s),&s);
    // Match explicit FSTPL boundaries in legacy_direction before FPATAN.
    dx = load(store(dx,s),s); dy = load(store(dy,s),s);
    auto result = fpatan(dx,dy,s);
    uint64_t pi_bits = UINT64_C(0x400921fb54442d18);
    result = extF80_add(result,f64_to_extF80(pi_bits,&s),&s);
    return store(result,s);
}
