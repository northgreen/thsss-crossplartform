#!/bin/sh
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
cd "$ROOT"
DATA=$(mktemp -d)
trap 'rm -rf "$DATA"' EXIT HUP INT TERM
for name in Image Model FX BGM Sound Story CS BPic; do
    [ ! -d "$ROOT/game/$name" ] || ln -s "$ROOT/game/$name" "$DATA/$name"
done
for renderer in gl vk; do
    cp "$ROOT/game/Setting.INI" "$DATA/Setting.INI"
    [ ! -f "$ROOT/game/Score.dat" ] || cp "$ROOT/game/Score.dat" "$DATA/Score.dat"
    THSSS_RENDERER=$renderer dotnet run --project tests/testcase/WindowResizeCompatibility/WindowResizeCompatibility.csproj \
        --framework "$THSSS_FRAMEWORK" -p:WarningLevel=0 -p:GenerateResourceWarnOnBinaryFormatterUse=false -- "$DATA"
done
