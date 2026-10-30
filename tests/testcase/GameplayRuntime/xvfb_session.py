#!/usr/bin/env python3
"""Give the real game normal focus events inside its private Xvfb display."""

import subprocess
import sys


def main():
    manager = subprocess.Popen(
        ["xfwm4", "--compositor=off"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    try:
        return subprocess.call(sys.argv[1:])
    finally:
        manager.terminate()
        try:
            manager.wait(timeout=5)
        except subprocess.TimeoutExpired:
            manager.kill()
            manager.wait()


if __name__ == "__main__":
    sys.exit(main())
