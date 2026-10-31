#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def source_snapshot():
    paths = [ROOT / "game/Setting.INI", ROOT / "game/Replay/thSSS_25.rpy"]
    paths += sorted((ROOT / "game").glob("Score.dat*"))
    for directory in ("src", "native/LegacyMath", "tools/GameplayMath.Fody"):
        paths += sorted(
            p
            for p in (ROOT / directory).rglob("*")
            if p.is_file()
            and not {"bin", "obj", "build"}.intersection(p.parts)
            and p.suffix in {".cs", ".csproj", ".cpp", ".h", ".py", ".xml"}
        )
    return {
        str(p.relative_to(ROOT)): {
            "bytes": p.stat().st_size,
            "mtime_ns": p.stat().st_mtime_ns,
            "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
        }
        for p in paths
    }


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def stage_assets(output):
    data = output / "data"
    data.mkdir()
    for name in ("Image", "Model", "FX", "Sound", "Story", "BGM", "CS", "BPic"):
        shutil.copytree(ROOT / "game" / name, data / name)
    shutil.copy2(ROOT / "game/Setting.INI", data / "Setting.INI")
    # Replay speed controls are live keyboard inputs, not recorded key words.
    # A diagnostic run must not consume the user's typing while it runs in background.
    setting = data / "Setting.INI"
    contents = setting.read_bytes()
    if b"UseDirectInput=1" in contents:
        setting.write_bytes(contents.replace(b"UseDirectInput=1", b"UseDirectInput=0"))
    elif b"UseDirectInput=0" not in contents:
        raise ValueError("Temporary setting has no recognized UseDirectInput value")
    (data / "Replay").mkdir()
    (data / "ScreenShot").mkdir()
    shutil.copy2(ROOT / "game/Replay/thSSS_25.rpy", data / "Replay/thSSS_25.rpy")
    return data


def focus_once(process, deadline):
    while process.poll() is None and time.monotonic() < deadline:
        found = subprocess.run(
            ["xdotool", "search", "--onlyvisible", "--pid", str(process.pid)],
            capture_output=True,
            text=True,
            timeout=3,
        )
        if found.stdout.splitlines():
            window = found.stdout.splitlines()[-1]
            subprocess.run(["xdotool", "windowfocus", window], check=True, timeout=5)
            return window
        time.sleep(0.05)
    raise TimeoutError("Production did not map a visible X11 window within 45s")


def run_game(output, data, args):
    env = os.environ.copy()
    for key in (
        "WAYLAND_DISPLAY",
        "WAYLAND_SOCKET",
        "THSSS_INPUT",
        "THSSS_START_STATE",
        "THSSS_SCREENSHOT",
        "THSSS_WINDOW_HIDDEN",
        "LIBGL_ALWAYS_SOFTWARE",
    ):
        env.pop(key, None)
    env.update(
        THSSS_DATA_ROOT=str(data),
        THSSS_RENDERER="gl",
        THSSS_MATH_BACKEND="x87",
        THSSS_DIAG_FPS="1",
        XDG_SESSION_TYPE="x11",
        COMPlus_PerfMapEnabled="1",
    )
    if args.display:
        env["DISPLAY"] = args.display
    if not env.get("DISPLAY"):
        raise RuntimeError(
            "A real X11 DISPLAY is required; no implicit software/Xvfb fallback"
        )
    gpu = subprocess.run(
        ["glxinfo", "-B"], env=env, capture_output=True, text=True, check=True
    )
    (output / "glxinfo.txt").write_text(gpu.stdout + gpu.stderr)
    previous = subprocess.run(
        ["xdotool", "getwindowfocus"], env=env, capture_output=True, text=True
    )
    write_json(output / "focus-before.json", {"status": previous.returncode,
               "window": previous.stdout.strip(), "error": previous.stderr})
    # Focus commands use the same explicitly selected X11 display as the game.
    os.environ["DISPLAY"] = env["DISPLAY"]
    dll = HERE / "bin" / args.configuration / "net8.0/ReplayPerformance.dll"
    with (output / "runtime.log").open("w") as log:
        process = subprocess.Popen(
            ["dotnet", str(dll), str(output), str(args.frames)],
            env=env,
            cwd=ROOT,
            stdout=log,
            stderr=subprocess.STDOUT,
        )
        write_json(
            output / "process.json",
            {
                "pid": process.pid,
                "display": env["DISPLAY"],
                "configuration": args.configuration,
                "frames": args.frames,
                "dataRoot": str(data),
            },
        )
        print(f"PROFILE_PROCESS pid={process.pid} output={output}", flush=True)
        try:
            focus_once(process, time.monotonic() + 45)
            deadline = time.monotonic() + args.timeout
            announced = False
            while process.poll() is None:
                if not announced and (output / "ready.json").exists():
                    print(
                        "PROFILE_READY " + (output / "ready.json").read_text(),
                        flush=True,
                    )
                    announced = True
                if time.monotonic() > deadline:
                    raise TimeoutError(
                        f"Replay profiling timeout after {args.timeout}s"
                    )
                time.sleep(0.2)
            if process.returncode:
                raise RuntimeError(
                    f"Game exited {process.returncode}; see {output / 'runtime.log'}"
                )
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            if previous.returncode == 0 and previous.stdout.strip():
                restored = subprocess.run(
                    ["xdotool", "windowfocus", previous.stdout.strip()],
                    env=env,
                    capture_output=True,
                    text=True,
                )
                (output / "focus-restore.txt").write_text(
                    str(restored.returncode) + "\n" + restored.stderr
                )
            else:
                (output / "focus-restore.txt").write_text(
                    "No previous X11 focus available; Wayland compositor owns desktop focus restoration.\n"
                )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--frames", type=int, default=0, help="0: through actual replay end marker"
    )
    parser.add_argument(
        "--configuration", choices=("Debug", "Release"), default="Release"
    )
    parser.add_argument(
        "--display", help="Existing hardware X11 display; defaults to DISPLAY"
    )
    parser.add_argument("--output", type=Path)
    parser.add_argument("--timeout", type=int, default=1800)
    args = parser.parse_args()
    if args.frames < 0 or args.timeout < 60:
        parser.error("frames must be >=0 and timeout >=60")
    output = (
        args.output.resolve()
        if args.output
        else Path(tempfile.mkdtemp(prefix="thsss-replay-profile-"))
    )
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("output must be empty")
    before = source_snapshot()
    write_json(output / "source-before.json", before)
    print(f"Evidence: {output}", flush=True)
    try:
        with (output / "build.log").open("w") as log:
            subprocess.run(
                [
                    "dotnet",
                    "build",
                    str(HERE / "ReplayPerformance.csproj"),
                    "-c",
                    args.configuration,
                    "--nologo",
                ],
                cwd=ROOT,
                stdout=log,
                stderr=subprocess.STDOUT,
                check=True,
            )
        data = stage_assets(output)
        run_game(output, data, args)
        summary = json.loads((output / "summary.json").read_text())
        print(json.dumps(summary, indent=2), flush=True)
    finally:
        after = source_snapshot()
        write_json(output / "source-after.json", after)
        unchanged = before == after
        write_json(
            output / "source-integrity.json",
            {
                "unchanged": unchanged,
                "changed": [
                    p
                    for p in before.keys() | after.keys()
                    if before.get(p) != after.get(p)
                ],
            },
        )
        if not unchanged:
            raise AssertionError(
                "Source/game input changed during run; inspect source-integrity.json"
            )


if __name__ == "__main__":
    main()
