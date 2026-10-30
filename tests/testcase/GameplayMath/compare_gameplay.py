#!/usr/bin/env python3
"""Report the first different state/decision per scenario; reject incomplete traces."""

import argparse
import csv
import gzip
import json
from pathlib import Path

from generate_inputs import FRAMES, SCENARIOS


def load(path):
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = reader.fieldnames
        rows = list(reader)
    if not fields or len(rows) != len(SCENARIOS) * (FRAMES + 1):
        raise ValueError("Missing header or incomplete gameplay trace: " + str(path))
    expected = [
        (scenario, str(frame)) for scenario in SCENARIOS for frame in range(-1, FRAMES)
    ]
    keys = [(row["scenario"], row["frame"]) for row in rows]
    if keys != expected or any(None in row or None in row.values() for row in rows):
        raise ValueError("Wrong scenario/frame order or malformed row: " + str(path))
    return fields, rows


def compare(expected, actual):
    fields, left = load(expected)
    other_fields, right = load(actual)
    if fields != other_fields:
        raise ValueError("Gameplay trace headers differ")
    groups = {}
    for old, new in zip(left, right):
        group = groups.setdefault(
            old["scenario"],
            {"rows": 0, "different_rows": 0, "different_decisions": 0, "first": None},
        )
        group["rows"] += 1
        differences = {
            field: {"expected": old[field], "actual": new[field]}
            for field in fields
            if old[field] != new[field]
        }
        if differences:
            group["different_rows"] += 1
            group["different_decisions"] += old["hit"] != new["hit"]
            if group["first"] is None:
                group["first"] = {"frame": int(old["frame"]), "fields": differences}
    return {
        "rows": len(left),
        "different_rows": sum(x["different_rows"] for x in groups.values()),
        "different_decisions": sum(x["different_decisions"] for x in groups.values()),
        "scenarios": groups,
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("expected", type=Path)
    parser.add_argument("actual", type=Path)
    parser.add_argument("--require-equal", action="store_true")
    args = parser.parse_args()
    result = compare(args.expected, args.actual)
    print(json.dumps(result, indent=2))
    if args.require_equal and result["different_rows"]:
        raise SystemExit(1)
