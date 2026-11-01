# Script acceleration compatibility (2026)

`verify.py` compares the actual frozen pre-fusion `BaseObject_CS.Ctrl` with a supplied game/native pair, using 6,000 binary cases and exact field/output bits across Debug/Release probes, software/x87 and PC24/53. The frozen reference is a modern woven implementation, not a new CLR2 oracle. Keep the captured inputs, outputs and source identity immutable; do not regenerate them to accept a candidate.

Run `python3 -B tests/testcase/AccelerationCompatibility/verify.py verify --dll /candidate/THSSS.dll --native-library /candidate/libthsss_legacy_math.so --dependencies /matching/dependency/directory`. EventCtrl and Move observations cover write/event order; explicitly marked exceptional cases stop after EventCtrl to avoid unbounded original angle normalization. Ordinary cases execute the complete control method, including disabled-object behavior. A one-bit output mutation must be rejected.

`NativeChecks/run.py` accepts `--library`, `--oracle`, `--artifacts` and `--output`. It uses independent frozen raw primitives, exact output bits, full host-state/error/alias checks and fail-closed transaction mutation checks. Six-RID export checks and ARM64 QEMU are correctness evidence, not real ARM FPS. NativeChecks has a separate identity domain from the actual-method fixture.

Keep temporary dependencies linked rather than copying game assets. Complete Replay/entity/RNG, active-stage ownership and real frame measurements are separate gates; these tests do not prove canonical Replay metadata or stable 60 FPS.
