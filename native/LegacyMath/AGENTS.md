# Gameplay numeric backends (2026)

This directory contains hardware x87 and portable integer-only software implementations of the operations required by game simulation. It is not a CPU emulator or rendering backend. Supported x64 targets prefer real x87; ARM64 uses software. Never replace the explicit precision/storage contract with host double or long double arithmetic.

## Sources and ABI

- `origin.json`, `closure.json` and `license-inventory.json` bind the exact upstream revision, extracted file closure and SHA-256 values. Preserve upstream notices and do not edit vendor sources to make a test pass.
- Root `.gitattributes` marks `native/LegacyMath/vendor/**` as `-text`: sources and license texts must retain exact upstream bytes even with Windows `core.autocrlf=true`. Do not normalize bytes before hashing or update frozen hashes to accept rewritten line endings. Regression: `python3 tests/testcase/GameplayMath/check_vendor_checkout.py` checks a Windows-style checkout plus deliberate-tamper rejection.
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

The supported Clang targets enable the vendor's existing CLZ/unsigned-int128 integer implementations. ThinLTO removes repeated internal format/status wrappers; ELF builds bind internal calls locally, while public ABI exports and whole-library replacement remain available. COFF explicitly retains `_fltused` through LTO. These settings do not enable fast-math/FMA, merge Sin/Cos or use host floating arithmetic; CLZ/UMULH are scalar instructions, not proof of NEON acceleration.

`LegacyMathOutputDirectory` must not end with a separator: a final Windows backslash escapes Exec's closing quote before Python receives `--output`. Combine child names with `Path.Combine` rather than adding a trailing slash. `python3 tests/testcase/GameplayMath/check_native_command.py` exercises real MSBuild/Exec with spaced paths and both Windows RIDs, including a negative control for the old boundary.

Cross-linking a Windows/macOS binary is not runtime validation. Preserve the distinction between Linux x64 execution, ARM64 execution under QEMU, metadata checks, and tests on real target operating systems/hardware.

## Distribution

Keep `NOTICE.md`, all license texts, complete extracted source, local adapter/configuration/memory shim, manifests and build script together with published libraries as corresponding-source material. The component is mixed-license, including LGPL; never describe it as BSD-only. Libraries remain dynamically replaceable. Packaging must not prohibit replacing a compatible library or reverse engineering needed to debug modifications to it. See `NOTICE.md` for the notices and practical distribution checklist.

The scalar/mixed backend tests and the actual game-state/Replay tests are different gates. Passing kernel tests alone does not establish production gameplay parity.

On Linux x64, `python3 tests/testcase/GameplayMath/check_backends.py <library.so>` checks both exact ABIs, immutable baseline bits, invalid argument behavior, dirty full-stack preservation and native hot-loop timings. Native call timings are not game FPS; compare real recording/playback under the same settings separately.

## Fused gameplay geometry

`native_control.cpp` also provides exact simulation-control blocks. Degree increments retain the getter's binary64 boundary, rate conversion, addition boundary and degree setter conversion; normalization remains in the original managed setter. Script acceleration keeps the extended degree-to-radian intermediate, binary64 trig arguments, binary32 trig results and final component stores. It uses separate Cos and Sin, including their existing out-of-range behavior. Do not cancel conversions, skip zero rates/acceleration or replace normalization loops with modulo. Each hardware block preserves full host state in one transaction. `DegreeCompatibility` and `AccelerationCompatibility` compare actual frozen pre-fusion methods plus independent raw/state/machine checks; active-stage ownership checks cover angular rates, script acceleration inputs and Ax/Ay writes.

`native_geometry.cpp` exposes distance and circle-test blocks, dispatched through the same backend selector. Six coordinate inputs preserve four separate own-position captures and two target coordinates. The distance graph keeps its binary64 store before sqrt and at public return; circle products keep their explicit binary64 boundaries, Region's binary32 conversion and ordered strict comparison. Each hardware block saves/restores host x87 once. Do not replace these with squared-distance algebra or host double arithmetic.

`native_ellipse.cpp` keeps the entire original ellipse expression in one transaction, including the extended angle sum and distance-plus-extent, separate distance/direction captures, Atan2 infinity wrapper, binary64 π constant (not FLDPI), repeated binary32 trig arguments, explicit half-axis binary64 boundaries and original ordered division chain. Scalar inputs preserve getter order for the actual single-thread simulation; arbitrary concurrent position mutation is outside this block's equivalence guarantee. Never infer a binary64 boundary merely from a C# double local.

The software ellipse path computes the original Cos/right squared term before the independent Sin/left term. If right is >=1 or NaN it returns miss: each squared term divided twice by its same denominator is nonnegative or NaN, so the original ordered `(left + right) < 1` cannot succeed. Keep Cos FIRST, both N64 denominator boundaries, each multiplication/division rounding and the final left+right operand order. This is a bool-only, status-isolated short-circuit; do not apply it to APIs exposing intermediate results or exception flags. The hardware expression remains unchanged.

`native_motion.cpp` retains two complete translated Position getters, each PointF binary32 boundary, ordered velocity/trig/scale multiplication, the final PointF argument and the translated setter. It returns final OriginalPosition; the caller must not apply Position again. Four separate origin captures and six offset reads remain distinct. Integer offset conversion is RNE binary32 independent of host control mode; do not cancel translations algebraically. Each hardware block saves/restores full x87 state once; null outputs/invalid precision return before changing either output, and aliased outputs receive Y last.

`native_laser.cpp` fuses one StraightLaser candidate point only; the managed point loop, endpoint exclusions and first-hit return stay authoritative. Capture the point once and four target coordinates in X1/X2/Y1/Y2 order. Preserve three binary64 boundaries (sqrt argument and each extent product), Region's binary32 conversion, and ordered strict `<`. Unlike the public distance/circle API, laser sqrt returns an extended intermediate with no binary64 store; reusing those blocks adds an incorrect boundary. Each hardware point transaction saves/restores the entire x87 state once. `tests/testcase/LaserCompatibility/` freezes the pre-fusion actual method, while `NativeChecks/` independently checks raw operations and machine/state invariants; neither alone proves full Replay compatibility.

The circle block has a pure-integer, reject-only coarse filter. It applies only to stable coordinate captures and its documented ±2^20/nonnegative bounded domain, using outward integer ceiling-product extents and a conservative16-pixel margin. The proof bounds the total adverse truncation/rounding error by27/8 pixels. NaNs, infinities, negative/extreme values, changing captures or overly large bounds use the original graph. Validate API errors first. The reject path must not execute any FP arithmetic, conversions or FPU control instructions; preserve its disassembly and differential/state gates in `tests/testcase/CollisionCompatibility/`.
