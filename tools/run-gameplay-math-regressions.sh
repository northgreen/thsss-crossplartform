#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
cd "$ROOT"
if command -v python3 >/dev/null 2>&1; then
    exec python3 tests/testcase/GameplayMath/run_regressions.py --framework "$THSSS_FRAMEWORK" "$@"
fi
exec python tests/testcase/GameplayMath/run_regressions.py --framework "$THSSS_FRAMEWORK" "$@"
