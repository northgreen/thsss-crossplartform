# Portable gameplay numeric kernel (2026)

This directory is an integer-only software implementation of the x87 operations required by game simulation. It is not a CPU emulator, rendering backend or host-FPU mode switch. Native x87 code belongs only in independent tests, never in this runtime library.

## Sources and ABI

- `origin.json`, `closure.json` and `license-inventory.json` bind the exact upstream revision, extracted file closure and SHA-256 values. Preserve upstream notices and do not edit vendor sources to make a test pass.
- `software_x87.h` defines the C ABI. Extended values are 16 bytes: UInt64 significand, UInt16 sign/exponent, three zeroed UInt16 padding fields. This is a portable bit representation, not the host `long double` ABI.
- Arithmetic precision is explicitly 24 or 53 significand bits, with extended exponent range. Transcendental internal precision and final float/double stores are separate operations. Preserve extended intermediates between operations; returning double too early can change game behavior.
- Each operation owns its software exception/rounding state. It must not change the calling thread's hardware control word, rounding mode or exception flags. Runtime argument errors are explicit return statuses; there is no errno contract.
- The kernel does not choose the game/thread precision policy. The managed compatibility layer supplies it. Do not globally reinterpret all threads as 24-bit.

## Build

```sh
python3 native/LegacyMath/build.py --rid linux-x64 --output /tmp/thsss-legacy-math
```

Supported RIDs: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`. The build uses Clang/LLVM and freestanding integer sources, without target OS SDKs or host libm. Missing tools, unsupported targets and altered source hashes must fail explicitly. Generated native binaries and object caches do not belong in version control.

Cross-linking a Windows/macOS binary is not runtime validation. Preserve the distinction between Linux x64 execution, ARM64 execution under QEMU, metadata checks, and tests on real target operating systems/hardware.

## Distribution

Keep `NOTICE.md`, all license texts, complete extracted source, local adapter/configuration/memory shim, manifests and build script together with published libraries as corresponding-source material. The component is mixed-license, including LGPL; never describe it as BSD-only. Libraries remain dynamically replaceable. Packaging must not prohibit replacing a compatible library or reverse engineering needed to debug modifications to it. See `NOTICE.md` for the notices and practical distribution checklist.

The scalar/mixed backend tests and the actual game-state/Replay tests are different gates. Passing kernel tests alone does not establish production gameplay parity.
