# Sourced by repository entry points after ROOT has been resolved.
# Pick a runnable host framework; an explicit selection still needs its runtime.
THSSS_SDK_VERSION=$(cd "$ROOT" && dotnet --version)
THSSS_SDK_MAJOR=${THSSS_SDK_VERSION%%.*}
if [ "$THSSS_SDK_MAJOR" -ge 10 ]; then
    : "${THSSS_FRAMEWORK:=net10.0}"
else
    : "${THSSS_FRAMEWORK:=net8.0}"
fi
case "$THSSS_FRAMEWORK" in
    net8.0) ;;
    net10.0)
        [ "$THSSS_SDK_MAJOR" -ge 10 ] || {
            printf 'net10.0 requires the .NET 10 SDK or newer.\n' >&2
            return 1
        } ;;
    *) printf 'Unsupported THSSS_FRAMEWORK: %s (use net8.0 or net10.0)\n' "$THSSS_FRAMEWORK" >&2; return 1 ;;
esac
export THSSS_FRAMEWORK
