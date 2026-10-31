import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from check_archive import check_archive

REPO = Path(__file__).resolve().parents[3]
LIBRARIES = (
    "thsss_legacy_math.dll",
    "libthsss_legacy_math.so",
    "libthsss_legacy_math.dylib",
)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="thsss-math-package-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.put("tools/make-dist.pl", (REPO / "tools/make-dist.pl").read_text())
        for path in (
            "src/THSSS.csproj",
            "src/Config/THSSS.Config.csproj",
            "game/Setting.INI",
        ):
            self.put(path, "fixture")
        for name in ("Image", "Model", "FX", "Sound", "Story", "BGM", "CS", "BPic"):
            self.put(f"game/{name}/fixture", "tiny asset")
        for name in ("NOTICE.md", "build.py", "software_x87.cpp"):
            self.put(f"native/LegacyMath/{name}", "fixture source")
        self.put(
            "mockbin/dotnet",
            '#!/bin/sh\nif [ "$1" = --version ]; then echo 8.0.100; exit 0; fi\necho unexpected SDK invocation >&2; exit 99\n',
        )
        (self.root / "mockbin/dotnet").chmod(0o755)
        self.env = dict(
            os.environ,
            PATH=str(self.root / "mockbin") + os.pathsep + os.environ["PATH"],
        )

    def put(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)

    def stage(self, mode="auto", digest=None):
        self.put("output/THSSS.dll", f"compiled fixture mode={mode}")
        digest = (
            digest
            or hashlib.sha256((self.root / "output/THSSS.dll").read_bytes()).hexdigest()
        )
        self.put(
            "output/THSSS.math-mode.json", json.dumps({"mode": mode, "sha256": digest})
        )
        for name in ("THSSS", "Config"):
            self.put(
                f"output/{name}.runtimeconfig.json",
                json.dumps({"runtimeOptions": {"tfm": "net8.0"}}),
            )
        if mode != "off":
            self.put("output/libthsss_legacy_math.so", "fixture library")
        return self.root / "output"

    def run_pack(self, mode="auto", *extra, success=True, skip=True):
        args = [
            "perl",
            "tools/make-dist.pl",
            "--rid",
            "linux-x64",
            "--out",
            "output",
            "--math-mode",
            mode,
        ]
        if skip:
            args.append("--skip-build")
        result = subprocess.run(
            args + list(extra),
            cwd=self.root,
            env=self.env,
            capture_output=True,
            text=True,
        )
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout)
        return result

    def test_strict_modes_before_sdk_lookup(self):
        self.env["PATH"] = str(self.root / "empty-path")
        perl = shutil.which("perl")
        assert perl is not None
        for mode in ("", "AUTO", "Software", "off ", " auto", "native", "auto\n"):
            with self.subTest(mode=mode):
                result = subprocess.run(
                    [perl, "tools/make-dist.pl", "--math-mode=" + mode],
                    cwd=self.root,
                    env=self.env,
                    capture_output=True,
                    text=True,
                )
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn("dotnet not found", result.stderr)
                self.assertNotIn("cannot query .NET SDK", result.stderr)

    def test_all_modes_and_uppercase_hash(self):
        for mode in ("auto", "software", "off"):
            with self.subTest(mode=mode):
                shutil.rmtree(self.root / "output", ignore_errors=True)
                stage = self.stage(mode)
                record_path = stage / "THSSS.math-mode.json"
                record = json.loads(record_path.read_text())
                record["sha256"] = record["sha256"].upper()
                record_path.write_text(json.dumps(record))
                self.run_pack(mode)
                check_archive(stage, mode, "linux-x64")
                readme = (stage / "README.txt").read_text()
                self.assertIn(f"Math mode: {mode}.", readme)
                self.assertEqual("NOTICE.md" in readme, mode != "off")
                self.assertIn(
                    "THSSS.math-mode.json", (stage / "SHA256SUMS.txt").read_text()
                )

    def test_identity_errors_do_not_copy_assets_or_clean(self):
        stage = self.stage("off")
        self.put(
            "output/libthsss_legacy_math.so", "do not remove before identity check"
        )
        record_path = stage / "THSSS.math-mode.json"
        valid = record_path.read_text()
        cases = (
            (None, "cannot read math mode record"),
            ("{broken", "invalid math mode record"),
            ("[]", "invalid math mode record"),
            ('{"mode": "off"}', "invalid math mode record"),
            (
                json.dumps({"mode": "off", "sha256": "g" * 64}),
                "invalid math mode record",
            ),
            (
                json.dumps({"mode": "software", "sha256": "0" * 64}),
                "math mode mismatch",
            ),
            (json.dumps({"mode": "off", "sha256": "0" * 64}), "SHA-256 mismatch"),
        )
        for content, message in cases:
            with self.subTest(message=message):
                if content is None:
                    record_path.unlink()
                else:
                    record_path.write_text(content)
                result = self.run_pack("off", success=False)
                self.assertIn(message, result.stderr)
                self.assertFalse((stage / "Image").exists())
                self.assertTrue((stage / "libthsss_legacy_math.so").exists())
        record_path.write_text(valid)
        with (stage / "THSSS.dll").open("a") as dll:
            dll.write("tampered")
        self.assertIn("SHA-256 mismatch", self.run_pack("off", success=False).stderr)

    def test_off_removes_only_generated_math_artifacts(self):
        stage = self.stage("off")
        for name in (
            *LIBRARIES,
            "GameplayMath.manifest.json",
            "licenses/LegacyMath/source/NOTICE.md",
        ):
            self.put("output/" + name, "stale")
        self.put("output/licenses/unrelated.txt", "keep")
        self.put("output/licenses/LegacyMath/custom.txt", "keep")
        self.put("output/other_math.dll", "keep")
        with self.assertRaisesRegex(ValueError, "disabled math artifact"):
            check_archive(stage, "off", "linux-x64")
        self.run_pack("off")
        check_archive(stage, "off", "linux-x64")
        for name in (
            "licenses/unrelated.txt",
            "licenses/LegacyMath/custom.txt",
            "other_math.dll",
        ):
            self.assertEqual((stage / name).read_text(), "keep")

    def test_enabled_requires_native_and_source(self):
        stage = self.stage("software")
        (stage / "libthsss_legacy_math.so").unlink()
        self.assertIn(
            "missing packaged gameplay numeric library",
            self.run_pack("software", success=False).stderr,
        )
        self.put("output/libthsss_legacy_math.so", "library")
        (self.root / "native/LegacyMath/software_x87.cpp").unlink()
        self.assertIn(
            "missing gameplay numeric notices/source",
            self.run_pack("software", success=False).stderr,
        )

    def test_publish_forwards_math_and_wine_flags(self):
        # A fake SDK produces the final DLL identity; no production build or assets are touched.
        self.put(
            "mockbin/dotnet",
            """#!/usr/bin/env python3
import hashlib, json, pathlib, sys
args = sys.argv[1:]
if args == ["--version"]:
    print("8.0.100")
    sys.exit(0)
with open("publish-args.jsonl", "a") as log:
    log.write(json.dumps(args) + "\\n")
stage = pathlib.Path(args[args.index("-o") + 1])
stage.mkdir(exist_ok=True, parents=True)
name = "Config" if "Config" in args[1] else "THSSS"
(stage / (name + ".runtimeconfig.json")).write_text(json.dumps({"runtimeOptions": {"tfm": "net8.0"}}))
if name == "THSSS":
    mode = next(arg.split("=", 1)[1] for arg in args if arg.startswith("-p:THSSSMathMode="))
    dll = b"finalized DLL"
    (stage / "THSSS.dll").write_bytes(dll)
    (stage / "THSSS.math-mode.json").write_text(json.dumps({"mode": mode, "sha256": hashlib.sha256(dll).hexdigest()}))
    if mode != "off":
        (stage / "libthsss_legacy_math.so").write_text("fixture library")
""",
        )
        for mode in ("auto", "software", "off"):
            args = ["perl", "tools/make-dist.pl", "--rid", "linux-x64", "--wine-compat"]
            if mode != "auto":
                args += ["--math-mode", mode]
            result = subprocess.run(args, cwd=self.root, env=self.env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            calls = [
                json.loads(line)
                for line in (self.root / "publish-args.jsonl").read_text().splitlines()
            ]
            self.assertEqual(len(calls), 2)
            self.assertIn(f"-p:THSSSMathMode={mode}", calls[1])
            self.assertIn("-p:THSSSManagedDesFallback=true", calls[1])
            stage = Path(calls[1][calls[1].index("-o") + 1])
            self.assertEqual(stage.parent, self.root / "dist")
            if mode != "auto":
                self.assertTrue(stage.name.endswith("-" + mode))
            else:
                self.assertRegex(stage.name, r"^thsss-\d{8}-\d{6}$")
            check_archive(stage, mode, "linux-x64")
            (self.root / "publish-args.jsonl").unlink()


if __name__ == "__main__":
    unittest.main()
