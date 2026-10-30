import collections
import json
import struct
import sys
from pathlib import Path


def ordered(bits, kind):
    value = int(bits, 16)
    width = 32 if kind == "f" else 64
    sign = 1 << (width - 1)
    return (~value & ((1 << width) - 1)) if value & sign else value | sign


def compare(left, right):
    counts = collections.Counter()
    mismatches = collections.Counter()
    maxima = collections.Counter()
    first = {}
    a = Path(left).read_text().splitlines()
    b = Path(right).read_text().splitlines()
    if len(a) != len(b):
        raise ValueError(f"Row counts differ: {len(a)} != {len(b)}")
    for x, y in zip(a, b):
        name, index, kind, xb = x.split(",")
        rn, ri, rk, yb = y.split(",")
        if (name, index, kind) != (rn, ri, rk):
            raise ValueError(f"Key mismatch: {x} / {y}")
        counts[name] += 1
        if xb == yb:
            continue
        mismatches[name] += 1
        distance = abs(ordered(xb, kind) - ordered(yb, kind))
        maxima[name] = max(maxima[name], distance)
        fmt = ">f" if kind == "f" else ">d"
        first.setdefault(
            name,
            {
                "index": int(index),
                "left_bits": xb,
                "right_bits": yb,
                "left": struct.unpack(fmt, bytes.fromhex(xb))[0],
                "right": struct.unpack(fmt, bytes.fromhex(yb))[0],
            },
        )
    return {
        "left": str(left),
        "right": str(right),
        "rows": len(a),
        "mismatched_rows": sum(mismatches.values()),
        "groups": {
            name: {
                "rows": count,
                "different": mismatches[name],
                "max_ulp": maxima[name],
                "first": first.get(name),
            }
            for name, count in counts.items()
        },
    }


if __name__ == "__main__":
    result = compare(sys.argv[1], sys.argv[2])
    print(json.dumps(result, ensure_ascii=False, indent=2))
