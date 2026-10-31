// 2026 deterministic scalar captures, not a concurrent-getter scheduling test.
#pragma once
#include <iterator>
#include <vector>
#include "native_ellipse_oracle.h"

namespace ellipse {
constexpr size_t OriginalCases = 192305;
class Generator {
    uint64_t state = UINT64_C(0xc5a12488d9a753ef);
public:
    uint64_t next() { state ^= state << 13; state ^= state >> 7; return state ^= state << 17; }
    float normal() { return (int(next() % 200001) - 100000) * 0.00390625f; }
};

inline std::vector<Case> original_cases() {
    Generator g;
    std::vector<Case> cases;
    const uint32_t edges[] = {
        0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000, 0x3f800000, 0xbf800000,
        0x3f7fffff, 0x3f800001, 0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000,
        0x7fc00000, 0xffc12345, 0x7f800001, 0xff800001
    };
    const uint64_t angles[] = {
        0, UINT64_C(0x8000000000000000), 1, UINT64_C(0x8000000000000001),
        UINT64_C(0x400921fb54442d18), UINT64_C(0xc00921fb54442d18), UINT64_C(0x43dfffffffffffff),
        UINT64_C(0x43e0000000000000), UINT64_C(0x7fefffffffffffff), UINT64_C(0x7ff0000000000000),
        UINT64_C(0xfff0000000000000), UINT64_C(0x7ff0000000000001), UINT64_C(0xfff8000012345678)
    };
    for (int i = 0; i < 100000; ++i) {
        Case c;
        c.flag = int(g.next() % 5) - 2; c.direction = g.normal(); c.angle = g.normal();
        for (auto &x : c.coordinates) x = g.normal();
        c.region = static_cast<int32_t>(g.next()); c.scale = g.normal();
        c.radius = g.normal(); c.width = g.normal(); c.length = g.normal();
        if (i < 40000) {
            auto *f = c.coordinates;
            f[1] = f[0]; f[3] = f[2]; f[6] = f[2]; f[7] = f[0]; f[8] = f[4]; f[9] = f[5];
            c.region = int(g.next() % 10); c.scale = float(g.next() % 100) * 0.01f;
            c.radius = float(g.next() % 200); c.width = float(g.next() % 200) * 0.01f;
            c.length = float(g.next() % 200) * 0.01f;
        } else if (i < 55000) {
            c.region = int(g.next() % 31) - 15;
        } else if (i < 75000) {
            for (auto &x : c.coordinates) x = float_from_bits(g.next());
            c.direction = double_from_bits(g.next()); c.angle = double_from_bits(g.next());
            c.scale = float_from_bits(g.next()); c.radius = float_from_bits(g.next());
            c.width = float_from_bits(g.next()); c.length = float_from_bits(g.next());
        } else {
            for (auto &x : c.coordinates) x = float_from_bits(edges[g.next() % std::size(edges)]);
            c.direction = double_from_bits(angles[g.next() % std::size(angles)]);
            c.angle = double_from_bits(angles[g.next() % std::size(angles)]);
            c.scale = float_from_bits(edges[g.next() % std::size(edges)]);
            c.radius = float_from_bits(edges[g.next() % std::size(edges)]);
            c.width = float_from_bits(edges[g.next() % std::size(edges)]);
            c.length = float_from_bits(edges[g.next() % std::size(edges)]);
        }
        cases.push_back(c);
    }
    // Bit-adjacent finite values are nextafterf neighbors, without a host libm dependency.
    for (int sign : {-1, 1}) for (int radius = 1; radius <= 1000; ++radius) for (int offset : {-1, 0, 1}) {
        Case c;
        c.direction = double_from_bits(UINT64_C(0x7ff0000000000001));
        c.radius = radius; c.width = c.length = 1;
        float at = float_from_bits(bits_of_float(float(sign * radius)) + offset);
        c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = at;
        cases.push_back(c);
    }
    const int regions[] = {0, -1, 1, 16777215, 16777216, 16777217, 16777219, -16777217, INT32_MAX, INT32_MIN};
    for (int region : regions) for (int i = 0; i < 200; ++i) {
        Case c = cases[i]; c.region = region; cases.push_back(c);
    }
    // Keep distance finite so a bad Atan2 infinity wrapper can turn a false result into a hit.
    for (uint32_t oy : edges) for (uint32_t ox : edges) for (uint32_t gx : edges) for (uint32_t gy : edges) {
        if ((oy & 0x7fffffff) < 0x7f800000 && (ox & 0x7fffffff) < 0x7f800000 &&
            (gx & 0x7fffffff) < 0x7f800000 && (gy & 0x7fffffff) < 0x7f800000) continue;
        Case c;
        c.direction = double_from_bits(UINT64_C(0x7ff0000000000001));
        c.radius = c.width = c.length = 1;
        c.coordinates[6] = float_from_bits(oy); c.coordinates[7] = float_from_bits(ox);
        c.coordinates[8] = float_from_bits(gx); c.coordinates[9] = float_from_bits(gy);
        cases.push_back(c);
    }
    for (uint64_t angle : angles) for (int flag : {0, 1, -1, INT32_MAX, INT32_MIN}) {
        Case c;
        c.direction = double_from_bits(angle); c.flag = flag; c.radius = c.width = c.length = 1;
        cases.push_back(c);
    }
    require(cases.size() == OriginalCases, "original 192305-case corpus count");
    return cases;
}

inline void append_graph_edges(std::vector<Case> &cases) {
    // Cancellation of independently captured doubles: do not round operands before Add_PC.
    const int exponents[] = {0, 1, 31, 500, 1022, 1023, 1024, 1070, 1083, 2045, 2046};
    for (int exponent : exponents) for (uint64_t sign : {UINT64_C(0), UINT64_C(0x8000000000000000)})
        for (int offset : {-1, 0, 1}) for (int flag : {0, 1, -1}) {
            uint64_t bits = (uint64_t(exponent) << 52) | sign | 1;
            Case c;
            c.direction = double_from_bits(bits);
            c.angle = double_from_bits((bits ^ UINT64_C(0x8000000000000000)) + offset);
            c.flag = flag; c.radius = 1; c.width = -0.5f; c.length = 2;
            c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = 0.75f;
            cases.push_back(c);
        }
    for (uint64_t angle : {UINT64_C(0x7fefffffffffffff), UINT64_C(0xffefffffffffffff)}) {
        Case c;
        c.direction = c.angle = double_from_bits(angle); c.flag = 1;
        c.radius = c.width = c.length = 1;
        cases.push_back(c); // theta remains finite ext80; an N64 store would overflow.
    }
    // Independent mutations of every coordinate capture, with negative/zero anisotropic axes.
    for (int index = 0; index < 10; ++index) for (uint32_t bits : {0u, 0x80000000u, 0x3f7fffffu, 0x3f800001u, 0xbf800001u})
        for (float width : {-1.0f, -0.0f, 0.5f, 2.0f}) {
            Case c;
            c.radius = c.length = 1; c.width = width;
            c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = 0.75f;
            c.coordinates[index] = float_from_bits(bits); cases.push_back(c);
        }
}
} // namespace ellipse
