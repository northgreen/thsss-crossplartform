# GameplayRuntime scope (2026)

- This fixture calls the production entry point and subscribes after the original Render callback. Only subscription happens on its setup thread; all gameplay actions happen on the window thread.
- Change only this directory. Do not patch production, native code, root tools, assets, documentation, or other tests to make this gate pass. Never disable the gameplay weave.
- Run `rtk python3 tests/testcase/GameplayRuntime/run_live.py`. Requires dotnet, xvfb-run and xfwm4. Each private Xvfb display runs its own window manager so the real window receives native focus events. Assets are real copies in a fresh temporary root. Record and playback use fresh processes and fresh player data. Evidence is retained at the printed path.
- The shipped menu selects Reimu. The unused BaseStage Aya fallback fails on missing Aya00; this harness initializes the actual Reimu class exactly as the menu does, at Normal difficulty.
- Capture steps count actual St1 updates, not dialogue-sensitive TimeMain. Trace raw scalar fields, float/double bits, entity lists, and complete gameplay RNG integer state. Never invoke computed entity getters for tracing. Wall time, HiScore, presentation fields and visual-only entity lists are excluded.
- Pause assertions compare the retained stage state on every actual pause-menu frame. Playback consumes the real Replay key stream and does not inject gameplay keys or pause.
- This gate proves current production record/playback determinism, input operability, pause/resume and bounded runtime coverage only. It does not prove Windows CLR2 replay compatibility, all stages, renderer correctness, Apple/ARM execution, or uninstrumented 60 FPS. Frame timing includes tracing, software rendering and the game pacing loop.
- Preserve failure logs and first-difference reports. Report production exceptions with their original method/stack; do not suppress failures, skip bad frames, modify expected state, or silently retry runs.
