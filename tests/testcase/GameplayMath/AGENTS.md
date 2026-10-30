# GameplayMath production parity and capture (2026-10-31)

Scope: actual production game methods, RNG state/errors, script interpolation, scalar/vector/conversion operations and script text. Never replace a historical capture with modern output. Do not change sibling MathBaseline fixtures to pass modern code. Complete historical Replay parity is not implied.

## Production gate

Run `sh tools/run-gameplay-math-regressions.sh --configuration Debug` and `--configuration Release` from the repository root. The runner verifies historical SHA256/source/input identities, builds the real woven game, requires a non-placeholder manifest with no unsupported/missing roots or RNG reordering, and compares 434962 production output rows across both precision states. It also executes 74172 runtime conversion/scalar/vector values, 161232 exact finite Single formatting rows and 10056 existing-asset parser rows. Failures preserve temporary artifacts. No player files are read or written.

`FormattingProbe.cs` and `ParseProbe.cs` are independently compiled by actual CLR2 x86. `TextCapture.cs` runs their public `Run` method on the same thread before/after real D3D creation; optional fifth argument selects `FormattingProbe` instead of `ParseProbe`. Original exe/SlimDX staging follows the capture boundary below. `baseline.json` binds exact source and input identities. `generate_parse_inputs.py`/`parse-corpus.json` record existing asset provenance; 1257 unique strings cover a superset of float.Parse operands from 937 MBG files. Dynamic `Execution.region` text is separately covered by formatting tests rather than assumed from asset parsing. Modern `G7` is not the old formatter's decimal midpoint rule.

`RuntimeChecks` can consume host-inflated byte-identical captures for targets without a compression runtime (e.g. a minimal ARM64 QEMU sysroot). Verify gzip SHA256 identities before inflating. Its native library must match the executable architecture. `PrecisionProbeHost` selects explicit software thread precision before executing an unchanged probe; it does not modify the historical captures or use hardware control-state shortcuts.

## Reproduce a modern smoke capture

From the repository root, using an already-built actual game assembly:

```sh
python3 tests/testcase/GameplayMath/generate_inputs.py
 dotnet build tests/testcase/GameplayMath/GameplayProbe.csproj -c Release
 dotnet tests/testcase/GameplayMath/bin/GameplayProbe/Release/net8.0/GameplayProbe.dll src/bin/Release/net8.0/THSSS.dll tests/testcase/GameplayMath/smoke.csv tests/testcase/GameplayMath/inputs.bin
python3 tests/testcase/GameplayMath/verify_smoke.py tests/testcase/GameplayMath/smoke.csv
```

`GameplayProbe.cs` is standalone C# 2 source: legacy compilation uses CLR2 `csc /optimize+ /platform:x86 /target:exe /out:GameplayProbe.exe GameplayProbe.cs`, with only framework references. The SDK project enforces language version 2 and has no game/package references. Actual CLR2.0.50727.9179 x86 compilation and both pre/post-D3D executions passed; `baseline.json` binds the probe, inputs and compressed raw capture hashes. Repeated gameplay captures were byte-identical.

`ScriptProbe.cs` invokes the actual private `Execution.ChangeValue` int/float/double overloads for all nine change/mode pairs and 4096 binary input records from sibling `MathBaseline/inputs.bin`. It captures 110592 values per precision mode without copying the game formulas. Its input, source and historical-output identities are also in `baseline.json`. Int32 outputs are encoded as exactly representable binary64 values for the existing raw comparator.

`compare_gameplay.py expected.csv.gz actual.csv [--require-equal]` checks complete scenario/frame identities and reports the first different state/decision. Without `--require-equal` it is an audit; with that switch any different frame exits nonzero. Shape/smoke success alone never proves parity.

## Historical capture boundary

`Capture.cs` references the original SlimDX and Windows Forms. It invokes both probes before and after creating the original D3D9 device without FpuPreserve, verifying 53/24-bit round-to-nearest mode and retaining the device. Probe execution stays on that **same thread**; a new process/thread would lose the D3D floating environment. Its arguments are GameplayProbe.exe, gameplay inputs.bin, pre-D3D CSV, post-D3D CSV, MathBaseline inputs.bin and script output prefix. Stage `THSSS.exe`, `SlimDX.dll`, both probe executables and optional app-local x86 D3DX9 together in an isolated temporary directory. Do not install dependencies or overwrite player files. Capture.cs does not draw game frames; D3D is only the original precision-state transition. Preserve CLR version, pointer size, assembly MVID, input SHA-256 and FPU/device evidence. Compare raw hexadecimal bit columns, not formatted decimals.

## Fixture contract

- Eight fixed scenarios, 1024 frames each: constant movement; Ax/Ay plus scalar acceleration; angular/direction velocities and scale changes; negative velocity; destination retargeting; binding; alternating circle/ellipse boundaries; sequential Vx then Vy setters.
- Default BaseObject constructors initialize real dictionaries and defaults. StageDataPackage is uninitialized except BoundRect `(16,24,640,480)`; its real getter only reads that field. No stage Init, globals, assets, drawing, audio, saves, or Main are touched. Binding uses real SetBinding and advances Ctrl once before initial frame `-1`; subsequent frames run anchor Ctrl before subject Ctrl.
- `GPM1` binary: little-endian Int32 magic `0x314D5047`, version `1`, frame count, scenario count. Strings use BinaryWriter-compatible 7-bit byte lengths and ASCII. Each scenario contains its name, initial assignment lists for subject/target/anchor, scheduled subject dictionary entries, then per-frame assignment lists for those three objects. Lists start with Int32 counts. Assignment names determine types: float properties use IEEE binary32; Direction/Angle binary64; points two binary32 values; Region/AngleWithDirection Int32. Dictionary entries contain name, Int32 time and binary32 value (DestPointDictionary uses a point). Generator tables and integer indexes define controls; no local Math/trig/random is used. Keep one identical input artifact across runtimes.
- Frames apply binary controls, execute real Ctrl, then record raw OriginalPosition, Velocity, Direction, Angle, ScaleLength/Width, Vx/Vy, GetDistance/GetDirection and HitCheck plus integer Time. Targets move through a literal position table, not through harness arithmetic. Collision fixture straddles strict circle/ellipse boundaries after the real Time gate; no expected historical bits are embedded.
- Smoke verification checks capture shape, finite results, real time advancement/movement and both collision outcomes. It is **not** CLR2-D3D parity verification. Rendering, RNG, derived bullets/stages, collisions with full gameplay lists, replay playback, and complete-game equivalence remain outside this isolated scaffold. No requested BaseObject scenario was omitted as unsafe.
