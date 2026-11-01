# Degree-increment compatibility (2026)

The independent probe calls a frozen actual `BaseObject.Ctrl` through reflection and compares 6,000 binary cases and all selected object-field bits. This is a pre-optimization modern implementation, not a new CLR2 oracle. Preserve frozen input/output bytes and their manifest; capture is exclusive and must not overwrite existing expectations.

Run `python3 -B tests/testcase/DegreeCompatibility/verify.py verify --dll /candidate/THSSS.dll --native-library /candidate/libthsss_legacy_math.so`. Debug/Release probes cover software/x87 and PC24/53, zero-rate nonidentity round trips, clamp/dictionary behavior, Move/CtrlCall ordering and ghost history. Single-bit output mutation must be rejected.

`NativeChecks/run.py --library /six-rid/linux-x64/libthsss_legacy_math.so --oracle /frozen/pre-degree.so --artifacts /six-rid --output /temporary/report` compares the current block with independent frozen raw primitives, checks complete host state/error/alias behavior and the exact transaction graph, and executes ARM64 QEMU software/dispatch. Exports must exactly match the current ABI header, including unrelated newer operations. A missing sampled sum-N64 omission witness does not permit removing that boundary; the static store-deletion negative control remains mandatory.

The active-stage ownership and complete Replay/entity/RNG checks are separate gates. Neither raw-operation timing nor QEMU proves real game FPS. Keep temporary dependencies linked and remove disposable copies after tests.
