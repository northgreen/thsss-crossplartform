#!/usr/bin/env python3
"""Write the shared GPM1 controls once; neither runtime synthesizes float inputs."""

import argparse
from pathlib import Path
import struct

SCENARIOS = (
    "constant",
    "acceleration",
    "angular",
    "negative",
    "destination",
    "binding",
    "collision",
    "components",
)
FRAMES = 1024
FLOATS = {
    "Velocity",
    "Accelerate",
    "Ax",
    "Ay",
    "AngularVelocityDegree",
    "DirectionVelocityDegree",
    "ScaleLength",
    "ScaleWidth",
    "ScaleVL",
    "ScaleVW",
    "ScaleX",
    "ScaleY",
    "Vx",
    "Vy",
}


def integer(stream, value):
    stream.write(struct.pack("<i", value))


def text(stream, value):
    data = value.encode("ascii")
    assert len(data) < 128
    stream.write(bytes([len(data)]))  # BinaryWriter's 7-bit byte-length prefix.
    stream.write(data)


def value(stream, name, item):
    if name in FLOATS:
        stream.write(struct.pack("<f", item))
    elif name in ("OriginalPosition", "DestPoint"):
        stream.write(struct.pack("<ff", *item))
    elif name in ("Region", "AngleWithDirection"):
        integer(stream, int(item))
    elif name in ("Direction", "Angle"):
        stream.write(struct.pack("<d", item))
    else:
        raise ValueError(name)


def assignments(stream, items):
    integer(stream, len(items))
    for name, item in items:
        text(stream, name)
        value(stream, name, item)


def initial(name):
    subject = [
        ("OriginalPosition", (123.456, 234.567)),
        ("Velocity", 0.125),
        ("Direction", 0.731),
    ]
    if name == "acceleration":
        subject += [("Ax", 0.00013), ("Ay", -0.00017), ("Accelerate", 0.0002)]
    elif name == "angular":
        subject += [
            ("DirectionVelocityDegree", 0.7),
            ("AngularVelocityDegree", 1.1),
            ("ScaleLength", 1.2),
            ("ScaleVL", 0.00005),
            ("ScaleVW", -0.00003),
        ]
    elif name == "negative":
        subject += [("Velocity", -0.125), ("Direction", 5.721)]
    elif name == "destination":
        subject += [("Velocity", 3.7), ("DestPoint", (320.25, 240.125))]
    elif name == "binding":
        subject += [("OriginalPosition", (210.25, 200.125)), ("Velocity", 0.01)]
    elif name == "collision":
        subject += [
            ("OriginalPosition", (0.0, 0.0)),
            ("Velocity", 0.0),
            ("Direction", 0.0),
            ("AngleWithDirection", False),
        ]
    target = [("OriginalPosition", (68.0, 64.0)), ("Region", 1)]
    anchor = [
        ("OriginalPosition", (180.25, 200.125)),
        ("Velocity", 0.05),
        ("Direction", 0.37),
        ("AngularVelocityDegree", 0.1),
        ("DirectionVelocityDegree", 0.2),
    ]
    return subject, target, anchor


def events(name):
    if name == "acceleration":
        return [
            ("AccelerateDictionary", 256, -0.0001),
            ("VelocityDictionary", 512, 0.3),
        ]
    if name == "angular":
        return [
            ("DirectionVelocityDegreeDictionary", 512, -0.9),
            ("DirectionDegreeDictionary", 768, 179.9),
            ("ScaleWidthVelocityDictionary", 512, 0.00002),
            ("ScaleLengthVelocityDictionary", 768, -0.00001),
        ]
    if name == "negative":
        return [("VelocityDictionary", 512, -0.25)]
    if name == "destination":
        return [
            ("DestPointDictionary", 256, (80.25, 100.125)),
            ("VelocityDictionary", 256, 3.7),
            ("DestPointDictionary", 768, (400.125, 300.25)),
            ("VelocityDictionary", 768, 2.3),
        ]
    return []


def frame_controls(name, frame):
    subject = []
    # Integer-indexed literal tables: no trig, random or accumulated float math.
    target = [
        (
            "OriginalPosition",
            ((120.125, 240.25), (123.5, 234.5), (320.25, 240.125), (80.25, 100.125))[
                frame % 4
            ],
        )
    ]
    if name == "components":
        subject = [
            ("Vx", (0.1, -0.2, 0.0, 0.3)[frame % 4]),
            ("Vy", (-0.1, 0.0, 0.2, -0.3)[frame % 4]),
        ]
    if name == "collision":
        ellipse = (frame // 128) % 2 != 0
        subject = [("ScaleLength", 2.0 if ellipse else 1.0), ("ScaleWidth", 1.0)]
        boundaries = (
            (4.99999952, 5.0, 5.00000048) if ellipse else (3.99999976, 4.0, 4.00000048)
        )
        target = [
            (
                "OriginalPosition",
                (boundaries[frame % 3], (0.0, 0.0000001, -0.0000001)[frame % 3]),
            )
        ]
    return subject, target, []


def generate(path):
    with path.open("wb") as stream:
        for number in (0x314D5047, 1, FRAMES, len(SCENARIOS)):
            integer(stream, number)
        for name in SCENARIOS:
            text(stream, name)
            for items in initial(name):
                assignments(stream, items)
            schedule = events(name)
            integer(stream, len(schedule))
            for dictionary, time, item in schedule:
                text(stream, dictionary)
                integer(stream, time)
                value(
                    stream,
                    "DestPoint" if dictionary == "DestPointDictionary" else "Velocity",
                    item,
                )
            for frame in range(FRAMES):
                for items in frame_controls(name, frame):
                    assignments(stream, items)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "output", nargs="?", type=Path, default=Path(__file__).with_name("inputs.bin")
    )
    generate(parser.parse_args().output)
