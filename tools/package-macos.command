#!/bin/sh
set -eu
ROOT=$(CDPATH='' cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT"
for tool in dotnet perl glslangValidator spirv-cross; do
    command -v "$tool" >/dev/null || {
        printf 'Missing build tool: %s\nInstall the .NET 8 or 10 SDK and, with Homebrew, brew install glslang spirv-cross.\n' "$tool" >&2
        exit 1
    }
done
# The runtime is included; the destination M-series Mac needs no .NET installation.
exec perl tools/make-dist.pl --rid osx-arm64 --self-contained "$@"
