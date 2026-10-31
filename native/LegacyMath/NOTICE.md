# THSSS portable numeric library: notices and source (2026)

This library incorporates a selected Bochs/SoftFloat implementation and a local hardware x87 backend. Supported x64 targets use real x87 arithmetic by default; ARM64 uses the portable software implementation. Both share the verified integer IEEE-format codecs. The library does not execute the original Windows CLR. All corresponding source, notices and replacement rights below apply to the complete distributed library, including its hardware backend.

## Licensing

The extracted component is **not BSD-only**. Preserve the actual notices in each source file:

- SoftFloat Release 3e files retain their BSD three-clause copyright, conditions and disclaimer; see `vendor/SoftFloat-BSD.txt`
- The older SoftFloat-derived transcendental files retain their complete derivative notices, including the applicable modification, warranty and liability text
- `vendor/fpu/fpu_trans.h` contains an explicit LGPL-2.0-or-later notice; files without an individual notice are identified separately in `license-inventory.json`, with the upstream Bochs license material retained in `vendor/Bochs-LICENSE.txt`
- Local adapter, configuration, memory shim and build code carry LGPL-2.1-or-later identifiers. The LGPL 2.1 text is included as `vendor/LGPL-2.1.txt`

Exact upstream revision, file identities and local extraction boundary are recorded in `origin.json`, `closure.json` and `license-inventory.json`. Local changes provide a narrow C ABI, explicit per-operation precision, platform-independent bit transport, hidden memory primitives and freestanding cross-target builds. Upstream vendor sources are retained unchanged.

## Corresponding source and replacement

A distribution containing the library must also retain this complete directory, including source/header files, configuration, manifests, license texts and `build.py`, as corresponding-source/build material. Distribution tooling must include this material alongside the binary notices; a bare attribution or upstream link is not a substitute for the modified component's source.

The game dynamically loads the compatible native library. Users may replace it with a compatible modified version. Do not impose a restriction on reverse engineering needed to debug modifications to this LGPL library. Preserve these permissions if adding a launcher, signature policy or packaging layer.

Rebuild from the repository root, with Python 3 and Clang/LLVM available:

```sh
python3 native/LegacyMath/build.py --rid linux-x64 --output /tmp/thsss-legacy-math
```

Other supported target values are `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64` and `osx-arm64`. See `AGENTS.md` and `build.py --help` for build/runtime boundaries. No original Microsoft CLR or DirectX binary is part of this component.

These are concrete source/notice and replacement requirements for this distribution, not a representation that all possible downstream licensing arrangements have been legally reviewed.
