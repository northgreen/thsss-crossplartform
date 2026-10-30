#!/bin/sh
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
: "${THSSS_DATA_ROOT:=$ROOT/game}"
export THSSS_DATA_ROOT
cd "$ROOT"

exec dotnet run --project "$ROOT/src/THSSS.csproj" --framework "$THSSS_FRAMEWORK" -- "$@"
