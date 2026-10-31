#!/usr/bin/env python3
"""Run the game with real keyboard focus in its private Xvfb server."""

import subprocess
import sys
import time


def main():
    # Xvfb needs no window-manager policy. run_live forces X11 so the client
    # cannot accidentally open on the inherited user's Wayland desktop instead.
    child = subprocess.Popen(sys.argv[1:])
    try:
        deadline = time.monotonic() + 30
        while child.poll() is None:
            found = subprocess.run(
                ["xdotool", "search", "--onlyvisible", "--pid", str(child.pid)],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                timeout=3,
            )
            windows = found.stdout.splitlines()
            if windows:
                subprocess.run(
                    ["xdotool", "windowfocus", windows[-1]], check=True, timeout=5
                )
                print(
                    "[DESKTOP] private X11 client focused: " + windows[-1], flush=True
                )
                break
            if time.monotonic() >= deadline:
                raise TimeoutError(
                    "Production process did not map a visible X11 window within 30 seconds"
                )
            time.sleep(0.05)
        return child.wait()
    finally:
        if child.poll() is None:
            child.terminate()
            try:
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()


if __name__ == "__main__":
    sys.exit(main())
