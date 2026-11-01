import math
import random
import struct

COUNT = 6000


def double_bits(value):
    return struct.unpack("<Q", struct.pack("<d", value))[0]


def float_bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def corpus():
    # Store actual IEEE input bits; seeds alone are not the frozen input contract.
    angles = [
        0,
        1 << 63,
        1,
        (1 << 63) | 1,
        0x000FFFFFFFFFFFFF,
        0x0010000000000000,
        0x7FF8000000001234,
        0xFFF8000000005678,
        0x7FF0000000001234,
    ]
    for value in [math.pi / 180, math.pi / 2, math.pi, 2 * math.pi]:
        bits = double_bits(value)
        angles.extend([bits - 1, bits, bits + 1])
    rates = [
        0,
        1 << 31,
        1,
        (1 << 31) | 1,
        0x007FFFFF,
        0x00800000,
        0x7FC01234,
        0xFFC05678,
        0x7F801234,
    ]
    for value in [1, -1, 180, -180, 360, -360, 7200, -7200, 2**-24]:
        bits = float_bits(value)
        rates.extend(candidate for candidate in [bits - 1, bits, bits + 1]
                     if abs(struct.unpack('<f', struct.pack('<I', candidate))[0]) <= 7200)
    rng = random.Random(20261101)
    records = []
    for i in range(COUNT):
        if i < len(angles) * len(rates):
            angle = angles[i % len(angles)]
            direction = angles[(i * 7 + 3) % len(angles)]
            rate = rates[(i // len(angles)) % len(rates)]
            direction_rate = rates[(i // len(angles) + 11) % len(rates)]
        else:
            angle = double_bits(rng.uniform(0, math.tau))
            direction = double_bits(rng.uniform(0, math.tau))
            rate = float_bits(rng.uniform(-7199, 7199)) if i % 3 else 0
            direction_rate = float_bits(rng.uniform(-7199, 7199)) if i % 5 else 0
        # Zero delta must not bypass the nonidentity degree roundtrip.
        if i < len(angles):
            rate = direction_rate = 0
        flags = (1 if i % 13 == 12 else 0) | (2 if i % 4 == 3 else 0)
        records.append(
            struct.pack("<QQIIB", angle, direction, rate, direction_rate, flags)
        )
    return struct.pack("<II", 0x31474544, COUNT) + b"".join(records)
