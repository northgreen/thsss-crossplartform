// 2026: additions only; the existing corpus/oracle remain byte-for-byte unchanged.
#pragma once
#include "native_ellipse_cases.h"

namespace shortcut {
using namespace ellipse;
inline Case axis(uint32_t distance) {
    Case c;
    c.angle = double_from_bits(UINT64_C(0x400921fb54442d18));
    c.radius = c.width = c.length = 1;
    c.coordinates[0] = c.coordinates[1] = c.coordinates[7] = float_from_bits(distance);
    return c;
}
inline std::vector<Case> cases() {
    auto result = original_cases();
    append_graph_edges(result);
    require(result.size() == 192705, "unchanged old corpus count");
    Generator g;
    for (int i = 0; i < 100000; ++i) {
        Case c;
        for (auto &f : c.coordinates) f = float_from_bits(g.next());
        c.direction = double_from_bits(g.next()); c.angle = double_from_bits(g.next());
        c.flag = int(g.next() % 3) - 1; c.region = int32_t(g.next());
        c.scale = float_from_bits(g.next()); c.radius = float_from_bits(g.next());
        c.width = float_from_bits(g.next()); c.length = float_from_bits(g.next());
        result.push_back(c);
    }
    const uint32_t denominators[] = {0, 0x80000000u, 0x3f800000u, 0xbf800000u,
        0x7f800000u, 0xff800000u, 0x7f800001u, 0xff800001u};
    for (uint32_t width : denominators) for (uint32_t length : denominators)
        for (uint32_t radius : denominators)
            for (uint32_t distance : {0x3f7fffffu, 0x3f800000u, 0x3f800001u}) {
                auto c = axis(distance);
                c.width = float_from_bits(width); c.length = float_from_bits(length);
                c.radius = float_from_bits(radius); result.push_back(c);
                c.region = -10; c.scale = 1; result.push_back(c);
                c.coordinates[1] = -c.coordinates[0]; result.push_back(c);
                c.direction = double_from_bits(UINT64_C(0x43e0000000000000));
                c.angle = 0; c.flag = 1; result.push_back(c);
            }
    require(result.size() == 298849, "old + 100000 IEEE + 6144 directed cases");
    return result;
}
struct Literal { Case input; int expected; };
inline std::vector<Literal> literals() {
    std::vector<Literal> result;
    for (uint32_t distance : {0u, 0x80000000u, 0x3f7fffffu, 0x3f800000u, 0x3f800001u})
        result.push_back({axis(distance), distance == 0x3f800000u || distance == 0x3f800001u ? 0 : 1});
    auto c = axis(0x3f000000u); c.width = c.length = -1; result.push_back({c, 1});
    c.region = -1; c.scale = 1; result.push_back({c, 1}); // Negative extent, same axes.
    for (uint32_t bits : {0u, 0x80000000u, 0x7f800000u, 0x7fc12345u, 0x7f800001u}) {
        c = axis(0x3f800000u); c.length = float_from_bits(bits);
        result.push_back({c, bits == 0x7f800000u ? 1 : 0});
    }
    c = axis(0x3f000000u); c.coordinates[1] = -c.coordinates[0]; result.push_back({c, 0});
    c = axis(0x3f000000u); c.direction = double_from_bits(UINT64_C(0x7ff0000000000001));
    result.push_back({c, 1}); c.flag = -1; result.push_back({c, 0});
    c = axis(0x3f000000u); c.angle = double_from_bits(UINT64_C(0x43e0000000000000));
    result.push_back({c, 0}); // x87 C2: unreduced argument returned, not host libm.
    return result;
}
} // namespace shortcut
