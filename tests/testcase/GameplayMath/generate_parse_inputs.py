#!/usr/bin/env python3
import argparse
import base64
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

# Indices transcribed from src/Shooting/CS_Data.cs (2026-10-31).
BULLET = [
    6,
    7,
    10,
    11,
    13,
    17,
    20,
    21,
    23,
    24,
    28,
    29,
    34,
    37,
    38,
    40,
    41,
    43,
    44,
    53,
    54,
    56,
    59,
    61,
    62,
    63,
    64,
    65,
    66,
    67,
    68,
    69,
]
LASER = [
    6,
    7,
    11,
    15,
    18,
    19,
    21,
    22,
    26,
    30,
    31,
    33,
    34,
    45,
    48,
    50,
    51,
    52,
    53,
    54,
    55,
    56,
    57,
]
COVER = [2, 3, 11, 12, 14, 15, 19, 20, 21, 22]
NUMBER = re.compile(r"[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?")


class Corpus:
    def __init__(self):
        self.tokens = {}
        self.counts = Counter()

    def add(self, token, category, location):
        entry = self.tokens.setdefault(token, {"counts": Counter(), "first": location})
        entry["counts"][category] += 1
        self.counts[category] += 1

    def events(self, field, location):
        # Match '&' and ';' Length - 1 loops, not all trailing fragments.
        for group in field.split("&")[:-1]:
            for event in group.split("|")[3].split(";")[:-1]:
                condition, action = event.split("：")[:2]
                conditions = [condition]
                for connector in ("且", "或"):
                    if connector in conditions[0]:
                        parts = conditions[0].split(connector)
                        conditions = [parts[0], parts[1]]
                for clause in conditions:
                    for operator in (">", "=", "<"):
                        if operator in clause:
                            clause = clause.split(operator)[1]
                    if any(op in condition for op in (">", "=", "<")):
                        self.add(clause, "event-condition", location)
                if "变化到" in event:
                    value = action.split("变")[1].split("，")[0].replace("化到", "")
                elif "增加" in event:
                    value = action.split("增")[1].split("，")[0].replace("加", "")
                elif "减少" in event:
                    value = re.split("[减少]", action)[2].split("，")[0]
                else:
                    continue
                parts = value.split("+")
                if parts[0] not in ("自身", "自机"):
                    self.add(parts[0], "event-value", location)
                if len(parts) > 1:
                    self.add(parts[1], "event-random", location)

    def record(self, line, kind, location):
        fields = line.split(",")
        indices = {"bullet": BULLET, "laser": LASER, "cover": COVER}[kind]
        for index in indices:
            self.add(fields[index], "direct-" + kind, location + ":" + str(index))
        if kind == "bullet":
            if len(fields) > 95:
                self.add(fields[95], "direct-bullet", location + ":95")
            # CS_Data reuses point22 for SubBullet.DestPoint, not point39.
            for index in (14, 18, 22):
                point = fields[index].replace("{", "").replace("}", "").split(" ")
                for part in point[:2]:
                    self.add(part.split(":")[1], "point", location + ":" + str(index))
        event_indices = {"bullet": (51, 52), "laser": (42, 43), "cover": (17, 18)}[kind]
        for index in event_indices:
            self.events(fields[index], location + ":" + str(index))

    def asset(self, text, name):
        lines = text.splitlines()
        if lines[0] != "Crazy Storm Data 1.01":
            raise ValueError("Unsupported asset version: " + name)
        cursor = 1
        for section in ("Types", "GlobalEvents", "Sounds"):
            if section in lines[cursor]:
                cursor += 1 + int(lines[cursor].split(" ")[0])
        cursor += 2  # Center and Totalframe
        for _ in range(4):
            header = lines[cursor].split(":")[1].split(",")
            cursor += 1
            if header[0] == "empty":
                continue
            for kind, count in [("bullet", int(header[3]))] + (
                [
                    ("laser", int(header[4])),
                    ("cover", int(header[5])),
                    ("ignored", int(header[6])),
                    ("ignored", int(header[7])),
                ]
                if len(header) >= 7
                else []
            ):
                for _ in range(count):
                    if kind != "ignored":
                        self.record(lines[cursor], kind, name + ":" + str(cursor + 1))
                    cursor += 1
        # Intentional superset: also includes integers, headers, ignored sections,
        # tags, coordinates not parsed by CS_Data, expression subliterals.
        for line_number, line in enumerate(lines, 1):
            for match in NUMBER.finditer(line):
                self.add(
                    match.group(), "lexical-superset", name + ":" + str(line_number)
                )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("asset_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.asset_root.resolve()
    output = args.output.resolve()
    if output == root or root in output.parents:
        raise ValueError("Output must not be within the asset root")
    corpus = Corpus()
    assets = []
    for path in sorted(root.rglob("*"), key=lambda p: p.relative_to(root).as_posix()):
        if not path.is_file() or path.suffix.lower() != ".mbg":
            continue
        raw = path.read_bytes()
        name = path.relative_to(root).as_posix()
        assets.append(
            {"path": name, "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}
        )
        # StreamReader Encoding.UTF8 consumes an optional UTF8 BOM.
        corpus.asset(raw.decode("utf-8-sig"), name)
    if not assets:
        raise ValueError("No .mbg assets found")
    tokens = sorted(corpus.tokens, key=lambda s: s.encode("utf-8"))
    data = b"".join(base64.b64encode(s.encode("utf-8")) + b"\n" for s in tokens)
    output.mkdir(parents=True, exist_ok=True)
    (output / "inputs.b64").write_bytes(data)
    manifest = {
        "schema": 1,
        "encoding": "one base64 UTF8 exact token per LF line; ordinal 0-based ID",
        "input_sha256": hashlib.sha256(data).hexdigest(),
        "files": len(assets),
        "unique_tokens": len(tokens),
        "occurrences": dict(sorted(corpus.counts.items())),
        "assets": assets,
        "tokens": [dict(id=i, **corpus.tokens[s]) for i, s in enumerate(tokens)],
    }
    (output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        json.dumps({k: v for k, v in manifest.items() if k not in ("assets", "tokens")})
    )


if __name__ == "__main__":
    main()
