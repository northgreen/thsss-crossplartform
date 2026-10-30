#!/bin/sh
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
. "$ROOT/tools/dotnet-framework.sh"
if [ "$#" -ne 1 ]; then printf 'Usage: sh tools/export-metal-shaders.sh PUBLISH_DIRECTORY\n' >&2; exit 2; fi
mkdir -p "$1"
OUT=$(CDPATH='' cd -- "$1" && pwd)
for tool in glslangValidator spirv-cross; do
    command -v "$tool" >/dev/null || { printf 'Missing build tool: %s\n' "$tool" >&2; exit 1; }
done
cd "$ROOT"
dotnet run --project tools/MetalShaderExporter/MetalShaderExporter.csproj --framework "$THSSS_FRAMEWORK" -c Release -- "$OUT/MetalShaders"
