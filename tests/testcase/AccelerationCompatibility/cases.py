import math
import random
import struct

COUNT = 6000
MAGIC = 0x31434341


def double_bits(value):
    return struct.unpack("<Q", struct.pack("<d", value))[0]


def float_bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def corpus():
    # Actual binary inputs, not decimal parsing or a newly compiled arithmetic oracle.
    directions = [
        0,
        1 << 63,
        1,
        (1 << 63) | 1,
        0x000FFFFFFFFFFFFF,
        0x0010000000000000,
        0x7FF8000000001234,
        0xFFF8000000005678,
        0x7FF0000000001234,
        0xFFF0000000004321,
        0x7FF0000000000000,
        0xFFF0000000000000,
        0x7FEFFFFFFFFFFFFF,
        0xFFEFFFFFFFFFFFFF,
    ]
    # Neighbors of quadrants and large argument-reduction boundaries in degrees.
    for value in [
        90,
        -90,
        180,
        -180,
        360,
        -360,
        2**20 * 90,
        2**48 * 90,
        2**53,
        1e100,
        -1e100,
        1e300,
    ]:
        bits = double_bits(value)
        directions.extend([bits - 1, bits, bits + 1])
    accelerations = [
        0,
        1 << 31,
        1,
        (1 << 31) | 1,
        0x007FFFFF,
        0x807FFFFF,
        0x00800000,
        0x80800000,
        0x7F7FFFFF,
        0xFF7FFFFF,
        0x7F800000,
        0xFF800000,
        0x7FC01234,
        0xFFC05678,
        0x7F801234,
        0xFF805678,
    ]
    for value in [1, -1, 2**-24, -(2**-24), 17, -17]:
        bits = float_bits(value)
        accelerations.extend([bits - 1, bits, bits + 1])
    rng = random.Random(20261101)
    records = []
    for index in range(COUNT):
        if index < 2 * len(directions) * len(accelerations):
            pair = index // 2
            direction = directions[pair % len(directions)]
            acceleration = accelerations[(pair // len(directions)) % len(accelerations)]
        else:
            direction = rng.getrandbits(64)
            acceleration = rng.getrandbits(32)
        angle = double_bits((index % 7) * math.pi / 3)
        heading = double_bits((index % 5) * math.pi / 2)
        angular_rate = float_bits([-90, 0, 90][index % 3])
        heading_rate = float_bits([0, -180, 180][index % 3])
        # Every boundary pair runs enabled and disabled. Bit 1 means observe prefix only.
        flags = index % 2
        if index >= COUNT - 32:
            flags |= 2
            angle = [0x7FF0000000000000, 0x7FEFFFFFFFFFFFFF][index % 2]
            heading = [0xFFF0000000000000, 0xFFEFFFFFFFFFFFFF][index % 2]
            angular_rate = heading_rate = 0x7F800000
        elif index % 37 == 0:
            angle = 0x7FF8000000009876
            heading = 0xFFF8000000003456
        records.append(
            struct.pack(
                "<QIQQIIB",
                direction,
                acceleration,
                angle,
                heading,
                angular_rate,
                heading_rate,
                flags,
            )
        )
    return struct.pack("<II", MAGIC, COUNT) + b"".join(records)
