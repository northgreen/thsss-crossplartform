#!/usr/bin/env python3
"""Inspect an existing completed build; never builds or modifies the game."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["verify"])
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--mode", choices=["auto", "software", "off"], required=True)
    parser.add_argument("--backend", choices=["software", "x87"])
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    if args.mode == "auto" and not args.backend:
        parser.error(
            "auto requires --backend: specify the actual platform backend expected"
        )
    root = Path(__file__).resolve().parent
    tool = root / "bin/Release/net8.0/MathModeCompatibility.dll"
    if not tool.is_file():
        parser.error("Build only MathModeCompatibility.csproj -c Release first")
    game = args.game.resolve(strict=True)
    evidence = Path(tempfile.mkdtemp(prefix="thsss-math-mode-"))
    print(f"Evidence: {evidence}", flush=True)
    records = []

    def invoke(command, environment=None):
        env = os.environ.copy()
        env.pop("THSSS_MATH_BACKEND", None)
        if environment is not None:
            env["THSSS_MATH_BACKEND"] = environment
        result = subprocess.run(
            ["dotnet", str(tool), *command],
            env=env,
            text=True,
            capture_output=True,
            check=False,
        )
        entry = {
            "command": command,
            "environment": environment,
            "returncode": result.returncode,
            "stdout": result.stdout,
            "stderr": result.stderr,
        }
        records.append(entry)
        (evidence / "results.json").write_text(json.dumps(records, indent=2))
        print(result.stdout, end="")
        if result.returncode:
            raise RuntimeError(result.stderr + f"\nEvidence: {evidence}")

    inspect = ["inspect", str(game), args.mode]
    if args.reference:
        inspect.append(str(args.reference.resolve(strict=True)))
    invoke(inspect)
    if args.mode == "off":
        isolated = evidence / "without-native"
        shutil.copytree(game.parent, isolated)
        for file in isolated.rglob("*"):
            if file.is_file() and "legacy_math" in file.name.lower():
                raise RuntimeError(
                    f"Off build unexpectedly contains native artifact: {file}"
                )
        for environment in [None, "software", "auto", "x87", "invalid"]:
            invoke(["probe", str(isolated / game.name), "off"], environment)
    else:
        for environment in [None, "software", "auto"]:
            expected = (
                "software"
                if args.mode == "software" or environment == "software"
                else args.backend
            )
            invoke(["probe", str(game), expected], environment)
        invoke(["probe", str(game), "ArgumentException"], "invalid")
        if args.mode == "software":
            invoke(["probe", str(game), "ArgumentException"], "x87")
    print(f"PASS mode={args.mode}; evidence={evidence}")


if __name__ == "__main__":
    main()
