import struct


def bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def corpus():
    cases = []
    zero, one, nan, inf = 0, bits(1), 0x7FC12345, 0x7F800000

    def add(
        points,
        target=1,
        region=0,
        scale=one,
        width=one,
        radius=one,
        x=zero,
        y=zero,
        expected=-1,
    ):
        cases.append((points, target, region, scale, width, radius, x, y, expected))

    for length in range(8):
        add([(zero, zero)] * length, expected=int(length == 7))
        add([(zero, zero)] * length, target=0, expected=2 if length == 7 else 0)
    add(None, expected=2)
    add(None, target=0, expected=2)
    for distance in [one - 1, one, one + 1]:
        add([(distance, zero)] * 7, expected=int(distance < one))
    add([(zero, zero)] * 7, target=2, expected=1)
    add(
        [(bits(10), zero)] * 3 + [(zero, zero), (nan, nan)] + [(bits(10), zero)] * 3,
        expected=1,
    )
    add([(bits(10), zero)] * 4 + [(zero, zero)] + [(bits(10), zero)] * 3, expected=1)
    for point in [0, 1, 2, 4, 5, 6]:
        points = [(bits(10), zero)] * 7
        points[point] = (zero, zero)
        add(points, expected=0)
    special = [
        zero,
        0x80000000,
        1,
        0x80000001,
        one,
        bits(-1),
        bits(2),
        0x7F7FFFFF,
        0xFF7FFFFF,
        inf,
        0xFF800000,
        nan,
        0xFFC54321,
    ]
    for radius in special:
        for width in special:
            for scale in [one, bits(-1), nan]:
                add(
                    [(zero, zero)] * 7,
                    radius=radius,
                    width=width,
                    scale=scale,
                    region=1,
                )
    for region in [-(2**31), -16777217, -1, 0, 1, 16777217, 2**31 - 1]:
        for scale in special:
            add([(one, zero)] * 7, region=region, scale=scale)
    for x, y in [(0, 0), (3, 4), (-3, 4), (5, 12), (8, -15)]:
        distance = (x * x + y * y) ** 0.5
        for region in [-7, 0, 3, 17]:
            for scale in [0.5, 1, 2]:
                for width in [0.5, 1, 2]:
                    boundary = (distance - region * scale) / width
                    raw = bits(boundary)
                    for adjacent in [max(0, raw - 1), raw, min(0xffffffff, raw + 1)]:
                        add([(bits(x), bits(y))] * 9, region=region,
                            scale=bits(scale), width=bits(width), radius=adjacent)
    for radius in [nan, 0x7f800000, 0xff800000]:
        add([(one, zero)] * 7, radius=radius,
            expected=1 if radius == 0x7f800000 else 0)
    state = 0x6C617365

    def next32():
        nonlocal state
        state ^= (state << 13) & 0xFFFFFFFF
        state ^= state >> 17
        state ^= (state << 5) & 0xFFFFFFFF
        return state & 0xFFFFFFFF

    def value():
        n = next32()
        return special[n % len(special)] if n & 3 == 0 else n

    while len(cases) < 5000:
        length = next32() % 33
        points = [(value(), value()) for _ in range(length)]
        if len(cases) % 3 == 0:
            # Bounded ordinary gameplay coordinates as well as arbitrary IEEE payloads.
            points = [
                (
                    bits((next32() % 20001 - 10000) / 16),
                    bits((next32() % 20001 - 10000) / 16),
                )
                for _ in points
            ]
        region = next32()
        if region >= 2**31:
            region -= 2**32
        add(
            points,
            target=2 if len(cases) % 2 else 1,
            region=region,
            scale=value(),
            width=value(),
            radius=value(),
            x=value(),
            y=value(),
        )
    data = bytearray(struct.pack("<ii", 0x31525A4C, len(cases)))
    for points, target, region, scale, width, radius, x, y, expected in cases:
        data += struct.pack(
            "<iBiIIIIIb",
            -1 if points is None else len(points),
            target,
            region,
            scale,
            width,
            radius,
            x,
            y,
            expected,
        )
        for point in points or []:
            data += struct.pack("<II", *point)
    return bytes(data)
