#!/bin/sh
# Rendering suites driven by DejaGnu. Set THSSS_TEST_REPORT_DIR to keep shader.sum/shader.log.
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
export THSSS_TEST_ROOT="$ROOT"
REPORT=${THSSS_TEST_REPORT_DIR:-$(mktemp -d)}
mkdir -p "$REPORT"
printf 'DejaGnu reports: %s\n' "$REPORT"
cd "$ROOT"
runtest --tool shader --srcdir "$ROOT/tests/dejagnu/shader" --outdir "$REPORT"
