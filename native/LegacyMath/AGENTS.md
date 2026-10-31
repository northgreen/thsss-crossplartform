# Gameplay numeric backends (2026)

This directory contains hardware x87 and portable integer-only software implementations of the operations required by game simulation. It is not a CPU emulator or rendering backend. Supported x64 targets prefer real x87; ARM64 uses software. Never replace the explicit precision/storage contract with host double or long double arithmetic.

## Sources and ABI

- `origin.json`, `closure.json` and `license-inventory.json` bind the exact upstream revision, extracted file closure and SHA-256 values. Preserve upstream notices and do not edit vendor sources to make a test pass.
- `software_x87.h` defines the C ABI. Extended values are 16 bytes: UInt64 significand, UInt16 sign/exponent, three zeroed UInt16 padding fields. This is a portable bit representation, not the host `long double` ABI.
- Arithmetic precision is explicitly 24 or 53 significand bits, with extended exponent range. Transcendental internal precision and final float/double stores are separate operations. Preserve extended intermediates between operations; returning double too early can change game behavior.
- `software_ext_*` always denotes the independent software backend. `hardware_ext_*` provides hardware arithmetic on x64, and `legacy_ext_*` dispatches to the selected backend. `legacy_math_set_backend(0/1/2)` means auto/software/x87; get returns the resolved 1/2. Invalid IDs return -1, unavailable hardware returns -2, and errors do not change the selection
- Each hardware arithmetic operation saves/restores the caller's full x87 environment and stack with FNSAVE/FRSTOR, masks exceptions and selects nearest rounding plus PC24/53 (PC64 for trig). CW, SW, tags, registers and MXCSR must survive dirty/full-stack tests unchanged. Do not optimize this down to changing only the control word
- IEEE float/double/ext80 format transport shares the verified integer codecs across both backends: saving the full FPU state merely to move formats is much slower. Arithmetic still uses x87 when selected; format transport has no host FPU side effects
- Software operations own their exception/rounding state. Runtime argument errors are explicit return statuses; there is no errno contract. Managed `THSSS_MATH_BACKEND=auto|software|x87` is an explicit regression/diagnostic override; absent/auto prefers hardware where available and rejects a forced unsupported backend
- The kernel does not choose the game/thread precision policy. The managed compatibility layer supplies it. Do not globally reinterpret all threads as 24-bit.

## Build

```sh
python3 native/LegacyMath/build.py --rid linux-x64 --output /tmp/thsss-legacy-math
```

Supported RIDs: `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`. The build uses Clang/LLVM and freestanding sources, without target OS SDKs or host libm. Missing tools, unsupported targets and altered source hashes must fail explicitly. Generated native binaries and object caches do not belong in version control.

Cross-linking a Windows/macOS binary is not runtime validation. Preserve the distinction between Linux x64 execution, ARM64 execution under QEMU, metadata checks, and tests on real target operating systems/hardware.

## Distribution

Keep `NOTICE.md`, all license texts, complete extracted source, local adapter/configuration/memory shim, manifests and build script together with published libraries as corresponding-source material. The component is mixed-license, including LGPL; never describe it as BSD-only. Libraries remain dynamically replaceable. Packaging must not prohibit replacing a compatible library or reverse engineering needed to debug modifications to it. See `NOTICE.md` for the notices and practical distribution checklist.

The scalar/mixed backend tests and the actual game-state/Replay tests are different gates. Passing kernel tests alone does not establish production gameplay parity.

On Linux x64, `python3 tests/testcase/GameplayMath/check_backends.py <library.so>` checks both exact ABIs, immutable baseline bits, invalid argument behavior, dirty full-stack preservation and native hot-loop timings. Native call timings are not game FPS; compare real recording/playback under the same settings separately.
