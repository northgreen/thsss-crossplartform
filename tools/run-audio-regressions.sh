#!/bin/sh
# Audio suites driven by DejaGnu (Linux OpenAL, plus Wine's native XAudio2 when available).
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
cd "$ROOT"
export THSSS_TEST_ROOT="$ROOT"
dotnet build "$ROOT/tests/testcase/AudioBackendCompatibility/AudioBackendCompatibility.csproj" --framework "$THSSS_FRAMEWORK" -p:WarningLevel=0
export THSSS_AUDIO_TEST_DLL="$ROOT/tests/testcase/AudioBackendCompatibility/bin/Debug/$THSSS_FRAMEWORK/AudioBackendCompatibility.dll"
REPORT=${THSSS_TEST_REPORT_DIR:-$(mktemp -d)}
mkdir -p "$REPORT"
printf 'DejaGnu reports: %s\n' "$REPORT"
cd "$ROOT"
runtest --tool audio --srcdir "$ROOT/tests/dejagnu/audio" --outdir "$REPORT"
