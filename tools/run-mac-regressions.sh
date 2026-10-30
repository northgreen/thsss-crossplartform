#!/bin/sh
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
cd "$ROOT"
for tool in dotnet cc glslangValidator spirv-cross runtest; do
    command -v "$tool" >/dev/null || { printf 'Missing required tool: %s\n' "$tool" >&2; exit 1; }
done
OUT=${THSSS_TEST_REPORT_DIR:-$(mktemp -d)}
mkdir -p "$OUT"
OUT=$(CDPATH='' cd -- "$OUT" && pwd)
export THSSS_TEST_ROOT="$ROOT" THSSS_MAC_CONTRACT_DIR="$OUT"
cc -std=c11 -Wall -Wextra -Werror -Wno-unused-parameter -shared -fPIC tests/testcase/CoreAudioCompatibility/audioqueue_shim.c -o "$OUT/libaudioqueue.so"
for suite in CoreAudioCompatibility MetalApiCompatibility MetalCompatibility; do
    dotnet build "tests/testcase/$suite/$suite.csproj" --framework "$THSSS_FRAMEWORK" -p:WarningLevel=0
done
printf 'Software API contract reports (not Apple runtime): %s\n' "$OUT"
runtest --tool mac --srcdir "$ROOT/tests/dejagnu/mac" --outdir "$OUT"
