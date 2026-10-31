#!/usr/bin/env python3
"""Actual production game loop, isolated assets, fresh record/playback processes."""

import argparse
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def execute(command, *, env=None, timeout=300, log=None):
    started = time.monotonic()
    with log.open("w") if log else open(os.devnull, "w") as stream:
        process = subprocess.Popen(
            command,
            cwd=ROOT,
            env=env,
            stdout=stream,
            stderr=subprocess.STDOUT,
            start_new_session=True,
        )
        try:
            status = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise RuntimeError(
                f"Timeout after {timeout}s: {command}; log={log}"
            ) from None
    if status:
        detail = log.read_text(errors="replace") if log else ""
        raise RuntimeError(f"Exit {status}: {command}; log={log}\n{detail[-16000:]}")
    return time.monotonic() - started


def stage_assets(destination):
    destination.mkdir()
    for name in ("Image", "Model", "FX", "Sound", "Story", "BGM", "CS", "BPic"):
        source = ROOT / "game" / name
        if not source.is_dir():
            raise FileNotFoundError(source)
        shutil.copytree(source, destination / name, copy_function=shutil.copy2)
    shutil.copy2(ROOT / "game" / "Setting.INI", destination / "Setting.INI")
    for name in ("Replay", "ScreenShot"):
        (destination / name).mkdir()


def compare(output, frames):
    count = 0
    with (
        (output / "record.jsonl").open() as recorded,
        (output / "playback.jsonl").open() as played,
    ):
        while True:
            left, right = recorded.readline(), played.readline()
            if not left and not right:
                break
            if not left or not right:
                raise AssertionError(f"Trace length mismatch at step {count}")
            a, b = json.loads(left), json.loads(right)
            if a != b:
                differences = [
                    (key, a["state"].get(key), b["state"].get(key))
                    for key in sorted(a["state"].keys() | b["state"].keys())
                    if a["state"].get(key) != b["state"].get(key)
                ]
                report = {
                    "step": count,
                    "recordDigest": a["digest"],
                    "playbackDigest": b["digest"],
                    "differentFields": len(differences),
                    "firstDifferences": differences[:30],
                }
                (output / "first-difference.json").write_text(
                    json.dumps(report, indent=2)
                )
                raise AssertionError(json.dumps(report, indent=2))
            count += 1
    if count != frames:
        raise AssertionError(f"Expected {frames} captures, got {count}")
    return count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--frames", type=int, default=900)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    parser.add_argument("--backend", choices=("auto", "software", "x87"), default="auto")
    args = parser.parse_args()
    if not 200 <= args.frames <= 18000 or not 30 <= args.timeout <= 1800:
        parser.error("frames must be 200..18000, timeout 30..1800 seconds")
    output = (
        args.output.resolve()
        if args.output
        else Path(tempfile.mkdtemp(prefix="thsss-gameplay-runtime-"))
    )
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("output must be empty (isolated evidence/data root)")
    print(f"Evidence/data: {output}", flush=True)
    execute(
        [
            "dotnet",
            "build",
            str(HERE / "GameplayRuntime.csproj"),
            "-c",
            args.configuration,
            "--nologo",
        ],
        timeout=args.timeout,
        log=output / "build.log",
    )
    data = output / "data"
    stage_assets(data)
    env = os.environ.copy()
    for key in (
        "THSSS_START_STATE",
        "THSSS_INPUT",
        "THSSS_WINDOW_HIDDEN",
        "THSSS_SCREENSHOT",
        "WAYLAND_DISPLAY",
        "WAYLAND_SOCKET",
    ):
        env.pop(key, None)
    env.update(
        THSSS_DATA_ROOT=str(data),
        THSSS_RENDERER="gl",
        ALSOFT_DRIVERS="null",
        LIBGL_ALWAYS_SOFTWARE="1",
        THSSS_DIAG_FPS="1",
        THSSS_MATH_BACKEND=args.backend,
        XDG_SESSION_TYPE="x11",
    )
    dll = HERE / "bin" / args.configuration / "net8.0" / "GameplayRuntime.dll"
    for mode in ("record", "playback"):
        for name in ("Score.dat", "Score.dat.bak"):
            (data / name).unlink(missing_ok=True)
        elapsed = execute(
            ["xvfb-run", "-a", sys.executable, str(HERE / "xvfb_session.py"),
             "dotnet", str(dll), mode, str(output), str(args.frames)],
            env=env,
            timeout=args.timeout,
            log=output / f"{mode}.log",
        )
        summary = json.loads((output / f"{mode}-summary.json").read_text())
        expected_backend = args.backend
        if expected_backend == "auto":
            import platform
            expected_backend = "x87" if platform.machine().lower() in ("x86_64", "amd64") else "software"
        if summary["backend"] != expected_backend:
            raise AssertionError(f"Live game selected {summary['backend']}, expected {expected_backend}")
        print(
            f"{mode}: process {elapsed:.2f}s; "
            + (output / f"{mode}-summary.json").read_text(),
            flush=True,
        )
    count = compare(output, args.frames)
    key_bytes = (output / "keys.rpy").stat().st_size
    if key_bytes != count * 2:
        raise AssertionError(
            f"Replay key bytes {key_bytes} != captured updates {count} * 2"
        )
    print(
        f"PASS: all {count} per-frame states/digests identical, {key_bytes} real replay bytes; evidence {output}"
    )


if __name__ == "__main__":
    main()
