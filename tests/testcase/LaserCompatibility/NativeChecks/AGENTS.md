# Native laser-point regression gates (2026)

`python3 tests/testcase/LaserCompatibility/NativeChecks/run.py --library /absolute/path/libthsss_legacy_math.so` compiles only the supplied-artifact Linux x64 harness, compares independent raw-operation graphs and preserves full caller x87/MXCSR state. The machine gate requires one transaction, three binary64 boundaries and no post-sqrt spill; diagnostic mutations must be rejected. It never rebuilds the supplied library.

`check.cpp` also supports Linux ARM64: cross-compile with `aarch64-linux-gnu-g++ -std=c++17 -O2 -Wall -Wextra -Werror check.cpp -ldl -o /tmp/laser-arm64`, then execute against an ARM64 library using `qemu-aarch64 -L /usr/aarch64-linux-gnu /tmp/laser-arm64 /absolute/path/libthsss_legacy_math.so`. This checks software/dispatch PC24/53, FPCR/FPSR preservation and rejection of hardware x87; it does not exercise x87 hardware or establish ARM FPS.

Keep these additional native tests inside `NativeChecks/`; the parent managed fixture binds its capture sources and must not silently regenerate old expected outputs. Neither raw-operation checks nor modern frozen-method parity replace authentic CLR2 results or actual Replay comparisons.
