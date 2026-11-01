"""Exercise real MSBuild/Exec argument transport with space-containing paths, without native tools."""

from pathlib import Path
from html import escape
import json
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
TARGETS = ROOT / "native/LegacyMath/LegacyMath.targets"
PROBE = r"""
from pathlib import Path
import argparse
import json
parser = argparse.ArgumentParser()
parser.add_argument("--rid", required=True)
parser.add_argument("--output", required=True)
a = parser.parse_args()
assert not a.output.endswith(("/", "\\")), repr(a.output)
assert '"' not in a.output, repr(a.output)
out = Path(a.output)
assert out.name == "legacy-math", repr(a.output)
out.mkdir(parents=True, exist_ok=True)
(out / "argv.json").write_text(json.dumps(vars(a)))
(out / "thsss_legacy_math.dll").write_bytes(b"argument-test-only")
"""


def exercise(base, rid, text):
    work = base / rid
    native = work / "native sources"
    native.mkdir(parents=True)
    (native / "LegacyMath.targets").write_text(text)
    (native / "build.py").write_text(PROBE)
    project = work / "ArgumentCheck.proj"
    project.write_text(f"""<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>{rid}</RuntimeIdentifier>
    <BaseIntermediateOutputPath>obj with spaces/</BaseIntermediateOutputPath>
    <LegacyMathPython>{escape(sys.executable)}</LegacyMathPython>
  </PropertyGroup>
  <Import Project="native sources/LegacyMath.targets" />
  <Target Name="Build" DependsOnTargets="BuildLegacyGameplayMath" />
</Project>""")
    result = subprocess.run(
        ["dotnet", "msbuild", str(project), "-t:Build", "-nologo", "-v:minimal"],
        cwd=work,
        capture_output=True,
        text=True,
    )
    output = work / "obj with spaces/Release/net8.0" / rid / "legacy-math"
    return result, output


with tempfile.TemporaryDirectory(prefix="thsss native command ") as directory:
    base = Path(directory)
    text = TARGETS.read_text()
    for rid in ("win-x64", "win-arm64"):
        result, output = exercise(base, rid, text)
        if result.returncode:
            raise RuntimeError(result.stdout + result.stderr)
        record = json.loads((output / "argv.json").read_text())
        assert record["rid"] == rid
        assert Path(record["output"]).resolve() == output.resolve()
        print("NATIVE_COMMAND_ARGUMENTS_PASS", rid)
    # Reintroducing the exact old boundary must be detected, even on a POSIX host.
    broken = text.replace("/legacy-math'))", "/legacy-math/'))")
    assert broken != text, (
        "Output-directory expression changed; update the negative control"
    )
    result, _ = exercise(base, "negative-control", broken)
    assert result.returncode != 0, "Trailing output separator was not rejected"
    print("TRAILING_SEPARATOR_REGRESSION_REJECTION_PASS")
