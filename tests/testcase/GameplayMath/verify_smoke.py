#!/usr/bin/env python3
"""Check capture integrity, not historical numerical equivalence."""

import csv
from pathlib import Path
import struct
import sys

from generate_inputs import FRAMES, SCENARIOS


def verify(path):
    with path.open(newline="") as stream:
        reader = csv.DictReader(stream)
        groups = {name: [] for name in SCENARIOS}
        for row in reader:
            assert row["scenario"] in groups, "Unexpected scenario"
            assert None not in row, "Unexpected CSV fields"
            for key, bits in row.items():
                if not key.endswith(("_f32", "_f64")):
                    continue
                width = 8 if key.endswith("_f32") else 16
                assert len(bits) == width, "Incorrect raw bit width"
                raw = bytes.fromhex(bits)
                number = struct.unpack(">f" if width == 8 else ">d", raw)[0]
                assert float("-inf") < number < float("inf"), "Nonfinite game result"
            assert row["hit"] in ("0", "1"), "Invalid collision result"
            groups[row["scenario"]].append(row)
    for name, rows in groups.items():
        assert len(rows) == FRAMES + 1, "Missing frames: " + name
        for index, row in enumerate(rows):
            assert int(row["frame"]) == index - 1, "Frame order: " + name
            assert int(row["time"]) == index + (1 if name == "binding" else 0), (
                "Ctrl time: " + name
            )
            if int(row["time"]) < 9:
                assert row["hit"] == "0", "Collision activation gate"
        positions = {(row["x_f32"], row["y_f32"]) for row in rows}
        assert (len(positions) == 1) == (name == "collision"), (
            "Movement not exercised: " + name
        )
    collision = groups["collision"][1:]
    for ellipse in (False, True):
        hits = {
            row["hit"]
            for frame, row in enumerate(collision)
            if frame >= 9 and ((frame // 128) % 2 != 0) == ellipse
        }
        assert hits == {"0", "1"}, "Collision boundary did not straddle both outcomes"
    print(
        "SMOKE_PASS: 8 scenarios, 8192 Ctrl frames + 8 initial states; finite raw bits, times, movement and both collision branches"
    )


if __name__ == "__main__":
    verify(Path(sys.argv[1]))
