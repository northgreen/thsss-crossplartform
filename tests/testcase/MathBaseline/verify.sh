#!/bin/sh
set -eu

if [ "$(uname -s)" != Linux ] || [ "$(uname -m)" != x86_64 ]; then
    echo 'MathBaseline solution prototype requires Linux x86_64 (x87); unsupported platform.' >&2
    exit 1
fi
suite=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
work=$(mktemp -d "${TMPDIR:-/tmp}/thsss-math-baseline.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT
trap 'exit 1' HUP INT TERM

python3 - "$suite" "$work" <<'PY'
import gzip
import hashlib
import json
import shutil
import sys
from pathlib import Path

suite, work = map(Path, sys.argv[1:])
manifest = json.loads((suite / "baseline.json").read_text())
inputs = (suite / "inputs.bin").read_bytes()
if len(inputs) != 98304 or hashlib.sha256(inputs).hexdigest() != "6bd574af15179cca05f8f249ae0bd8218b22a82ddd01c0b3cd506c00b5a2ba6d":
    raise ValueError("Historical input length/SHA256 mismatch")
shutil.copyfile(suite / "inputs.bin", work / "inputs.bin")
for name in ("windows-clr2-x86", "windows-clr2-d3d-x86",
              "actual-windows-clr2-x86", "actual-windows-clr2-d3d-x86",
              "random-windows-clr2-x86", "random-windows-clr2-d3d-x86"):
    with gzip.open(suite / (name + ".csv.gz"), "rb") as source:
        data = source.read()
    identity = manifest["files"][name + ".csv"]
    if (hashlib.sha256(data).hexdigest() != identity["sha256"] or
            len(data) != identity["bytes"] or len(data.splitlines()) != identity["rows"]):
        raise ValueError("Historical fixture SHA256/size/row count mismatch: " + name)
    (work / (name + ".csv")).write_bytes(data)
PY

gcc -shared -fPIC -O2 -Wall -Wextra -Werror "$suite/legacy_x87.c" -o "$work/liblegacy_x87.so"
dotnet build "$suite/SolutionsProbe.csproj" --configuration Release \
    -p:OutputPath="$work/bin/" -p:BaseIntermediateOutputPath="$work/obj/"
for precision in 24 53; do
    echo "Checking historical x87 precision=$precision"
    LD_LIBRARY_PATH="$work${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
        dotnet "$work/bin/SolutionsProbe.dll" "$work" "$precision" > "$work/result-$precision.txt" 2>&1 || {
            cat "$work/result-$precision.txt"
            exit 1
        }
    cat "$work/result-$precision.txt"
    python3 - "$work/result-$precision.txt" <<'PY'
import sys
from pathlib import Path

lines = Path(sys.argv[1]).read_text().splitlines()
if lines.count("TOTAL=473728; DIFFERENT=0") != 1:
    raise ValueError("Solution prototype did not check exactly 473728 samples with zero differences")
PY
done
echo MATH_BASELINE_SOLUTIONS_PASS
