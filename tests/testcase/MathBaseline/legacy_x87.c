#include <stdint.h>

#if !defined(__linux__) || !defined(__x86_64__)
#error "MathBaseline investigation shim requires Linux x86_64"
#endif

// Investigation-only x86 shim. It does not provide an ARM implementation.
// The measured CLR2 default uses 53 significand bits; original D3D9 switches to 24.
static _Thread_local unsigned short precision=0x0200u;
__attribute__((visibility("default"))) int legacy_precision(int bits) {
    if (bits!=24 && bits!=53) return -1;
    precision=bits==24 ? 0x0000u : 0x0200u;
    return 0;
}
static unsigned short enter(void) {
    unsigned short old, current;
    __asm__ volatile ("fnstcw %0" : "=m"(old));
    current = (unsigned short)((old & ~0x0f00u) | precision);
    __asm__ volatile ("fldcw %0" : : "m"(current));
    return old;
}
static void leave(unsigned short old) { __asm__ volatile ("fldcw %0" : : "m"(old)); }
__attribute__((visibility("default"))) double legacy_sin(double x) {
    unsigned short old=enter(); double y;
    __asm__ volatile ("fldl %1; fsin; fstpl %0" : "=m"(y) : "m"(x) : "st");
    leave(old); return y;
}
__attribute__((visibility("default"))) double legacy_cos(double x) {
    unsigned short old=enter(); double y;
    __asm__ volatile ("fldl %1; fcos; fstpl %0" : "=m"(y) : "m"(x) : "st");
    leave(old); return y;
}
__attribute__((visibility("default"))) double legacy_atan2(double y, double x) {
    unsigned short old=enter(); double result;
    __asm__ volatile ("fldl %1; fldl %2; fpatan; fstpl %0" : "=m"(result) : "m"(y),"m"(x) : "st");
    leave(old); return result;
}
__attribute__((visibility("default"))) double legacy_direction(float px, float py, float tx, float ty) {
    unsigned short old=enter(); double result, dx, dy;
    const double pi=3.1415926535897932384626433832795;
    __asm__ volatile ("flds %1; fsubs %2; fstpl %0" : "=m"(dx) : "m"(px),"m"(tx) : "st");
    __asm__ volatile ("flds %1; fsubs %2; fstpl %0" : "=m"(dy) : "m"(py),"m"(ty) : "st");
    __asm__ volatile ("fldl %1; fldl %2; fpatan; faddl %3; fstpl %0" : "=m"(result) : "m"(dy),"m"(dx),"m"(pi) : "st");
    leave(old); return result;
}
__attribute__((visibility("default"))) double legacy_distance(float px, float py, float tx, float ty) {
    unsigned short old=enter(); double result, dx, dy;
    __asm__ volatile ("flds %1; fsubs %2; fstpl %0" : "=m"(dx) : "m"(px),"m"(tx) : "st");
    __asm__ volatile ("flds %1; fsubs %2; fstpl %0" : "=m"(dy) : "m"(py),"m"(ty) : "st");
    __asm__ volatile ("fldl %1; fmul %%st, %%st; fldl %2; fmul %%st, %%st; faddp; fstpl %0; fldl %0; fsqrt; fstpl %0"
        : "=m"(result) : "m"(dx),"m"(dy) : "st","st(1)");
    leave(old); return result;
}
__attribute__((visibility("default"))) double legacy_sqrt(double x) {
    unsigned short old=enter(); double result;
    __asm__ volatile ("fldl %1; fsqrt; fstpl %0" : "=m"(result) : "m"(x) : "st");
    leave(old); return result;
}
__attribute__((visibility("default"))) float legacy_move(float p,float v,double d,float s) {
    unsigned short old=enter(); float result;
    __asm__ volatile ("fldl %1; fcos; fmuls %2; fmuls %3; fadds %4; fstps %0"
        : "=m"(result) : "m"(d),"m"(v),"m"(s),"m"(p) : "st");
    leave(old); return result;
}
