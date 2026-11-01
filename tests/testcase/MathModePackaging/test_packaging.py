import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

from check_archive import check_archive, check_config_isolation

REPO = Path(__file__).resolve().parents[3]
LIBRARIES = (
    "thsss_legacy_math.dll",
    "libthsss_legacy_math.so",
    "libthsss_legacy_math.dylib",
)


def isolation_files(rid='linux-x64', framework='net8.0'):
    platform = rid.split('-')[0]
    extension = '.exe' if platform == 'win' else ''
    files = {}
    for name in ('THSSS', 'Config', 'ConfigApp/Config'):
        files[name + extension] = 'fixture apphost'
        files[name + '.runtimeconfig.json'] = json.dumps({'runtimeOptions': {'tfm': framework}})
    files['Config.deps.json'] = json.dumps({'libraries': {'Config/1.0.0': {}}, 'targets': {}})
    for directory, name, version in (('', 'THSSS', '4.154.0'), ('ConfigApp/', 'Config', '2.88.9')):
        native_names = [ {'win': f'lib{n}.dll', 'linux': f'lib{n}.so', 'osx': f'lib{n}.dylib'}[platform]
                        for n in ('SkiaSharp', 'HarfBuzzSharp') ]
        libraries = {f'SkiaSharp/{version}': {}, 'HarfBuzzSharp/14.2.1.400' if name == 'THSSS' else 'HarfBuzzSharp/8.3.1.1': {}}
        if name == 'Config':
            libraries['Avalonia/11.3.22'] = {}
            libraries['Tmds.DBus.Protocol/0.21.3'] = {}
        files[directory + name + '.deps.json'] = json.dumps({'libraries': libraries, 'targets': {rid: {'native/1.0': {'native': {n: {} for n in native_names}}}}})
        for native in native_names:
            files[directory + native] = f'{version} native fixture'
    return files


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="thsss-math-package-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.put("tools/make-dist.pl", (REPO / "tools/make-dist.pl").read_text())
        shutil.copytree(REPO / 'third_party/SkiaSharp', self.root / 'third_party/SkiaSharp')
        for path in (
            "src/THSSS.csproj",
            "src/Config/THSSS.Config.csproj",
            "src/ConfigLauncher/THSSS.ConfigLauncher.csproj",
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

    def test_vulnerable_config_dbus_dependency_is_rejected(self):
        stage = self.stage('off')
        deps = stage / 'ConfigApp/Config.deps.json'
        deps.write_text(deps.read_text().replace('Tmds.DBus.Protocol/0.21.3', 'Tmds.DBus.Protocol/0.21.2'))
        with self.assertRaisesRegex(ValueError, 'patched D-Bus'):
            check_config_isolation(stage, 'linux-x64')

    def stage(self, mode="auto", digest=None):
        self.put("output/THSSS.dll", f"compiled fixture mode={mode}")
        digest = (
            digest
            or hashlib.sha256((self.root / "output/THSSS.dll").read_bytes()).hexdigest()
        )
        self.put(
            "output/THSSS.math-mode.json", json.dumps({"mode": mode, "sha256": digest})
        )
        for name, content in isolation_files().items():
            self.put('output/' + name, content)
        shutil.copytree(self.root / 'third_party/SkiaSharp', self.root / 'output/licenses/SkiaSharp')
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
                self.assertIn('libfontconfig.so.1', readme)
                self.assertIn('ConfigApp/', readme)
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

    def test_framework_checks_all_three_executables_before_copy(self):
        stage = self.stage('off')
        for name in ('THSSS', 'Config', 'ConfigApp/Config'):
            with self.subTest(name=name):
                path = stage / (name + '.runtimeconfig.json')
                valid = path.read_text()
                path.write_text(json.dumps({'runtimeOptions': {'tfm': 'net10.0'}}))
                self.assertIn("expected 'net8.0'", self.run_pack('off', success=False).stderr)
                self.assertFalse((stage / 'Image').exists())
                path.unlink()
                self.assertIn('cannot read published runtime configuration', self.run_pack('off', success=False).stderr)
                path.write_text(valid)

    def test_license_tampering_and_unsafe_inventory_fail_before_publish(self):
        source = self.root / 'third_party/SkiaSharp'
        inventory = source / 'license-inventory.json'
        valid = inventory.read_text()
        entries = json.loads(valid)
        path = source / entries[0]['file']
        original = path.read_bytes()
        path.write_bytes(original + b'tampered')
        self.assertIn('Skia license SHA-256 mismatch', self.run_pack('off', skip=False, success=False).stderr)
        self.assertFalse((self.root / 'output').exists())
        path.write_bytes(original)
        for replacement in ('../../outside', '/absolute', entries[1]['file']):
            entries[0]['file'] = replacement
            inventory.write_text(json.dumps(entries))
            self.assertIn('unsafe or duplicate Skia license path', self.run_pack('off', skip=False, success=False).stderr)
        inventory.write_text(valid)

    def test_archive_rejects_tampered_license_even_when_math_off(self):
        stage = self.stage('off')
        check_archive(stage, 'off', 'linux-x64')
        (stage / 'licenses/SkiaSharp/skiasharp-4.154.0/LICENSE.txt').write_text('tampered')
        with self.assertRaisesRegex(ValueError, 'Skia license SHA-256 mismatch'):
            check_archive(stage, 'off', 'linux-x64')

    def test_archive_requires_real_gui_dependencies_and_native_records(self):
        stage = self.stage('off')
        path = stage / 'ConfigApp/Config.deps.json'
        valid = path.read_text()
        for library in ('SkiaSharp/2.88.9', 'HarfBuzzSharp/8.3.1.1', 'Avalonia/11.3.22'):
            deps = json.loads(valid)
            del deps['libraries'][library]
            path.write_text(json.dumps(deps))
            with self.assertRaisesRegex(ValueError, 'dependency|version'):
                check_config_isolation(stage, 'linux-x64')
        deps = json.loads(valid)
        deps['targets'] = {}
        path.write_text(json.dumps(deps))
        with self.assertRaisesRegex(ValueError, 'missing native dependency record'):
            check_config_isolation(stage, 'linux-x64')
        deps = json.loads(valid)
        deps['libraries']['SkiaSharp.NativeAssets.Linux.NoDependencies/2.88.9'] = {}
        path.write_text(json.dumps(deps))
        with self.assertRaisesRegex(ValueError, 'NoDependencies'):
            check_config_isolation(stage, 'linux-x64')

    def test_config_isolation_all_six_rids(self):
        for rid in ('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'):
            with self.subTest(rid=rid):
                for name, content in isolation_files(rid).items():
                    self.put(rid + '/' + name, content)
                stage = self.root / rid
                check_config_isolation(stage, rid)
                suffix = '.exe' if rid.startswith('win-') else ''
                missing = stage / ('ConfigApp/Config' + suffix)
                missing.unlink()
                with self.assertRaisesRegex(ValueError, 'missing executable'):
                    check_config_isolation(stage, rid)
                missing.write_text('apphost')
                native = {'win': 'libSkiaSharp.dll', 'osx': 'libSkiaSharp.dylib', 'linux': 'libSkiaSharp.so'}[rid.split('-')[0]]
                for directory in (stage, stage / 'ConfigApp'):
                    asset = directory / native
                    valid = asset.read_text()
                    asset.unlink()
                    with self.assertRaisesRegex(ValueError, 'missing native asset'):
                        check_config_isolation(stage, rid)
                    asset.write_text(valid)
                deps = stage / 'Config.deps.json'
                deps.write_text(json.dumps({'libraries': {'SkiaSharp/4.154.0': {}}, 'targets': {}}))
                with self.assertRaisesRegex(ValueError, 'BCL-only'):
                    check_config_isolation(stage, rid)

    def test_publish_forwards_math_and_wine_flags(self):
        # A fake SDK produces the final DLL identity; no production build or assets are touched.
        self.put('publish-files.json', json.dumps(isolation_files()))
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
files = json.loads(pathlib.Path('publish-files.json').read_text())
for path, content in files.items():
    if 'ConfigLauncher' in args[1]:
        if path not in ('Config', 'Config.deps.json', 'Config.runtimeconfig.json'):
            continue
    elif name == 'Config':
        if not path.startswith('ConfigApp/'):
            continue
        path = path.removeprefix('ConfigApp/')
    elif path.startswith('Config'):
        continue
    target = stage / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content)
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
            args = ["perl", "tools/make-dist.pl", "--rid", "linux-x64", "--wine-compat", '--self-contained', '--framework', 'net8.0', '--configuration', 'Debug']
            if mode != "auto":
                args += ["--math-mode", mode]
            result = subprocess.run(args, cwd=self.root, env=self.env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            calls = [
                json.loads(line)
                for line in (self.root / "publish-args.jsonl").read_text().splitlines()
            ]
            self.assertEqual(len(calls), 3)
            for call in calls:
                self.assertEqual(call[call.index('--self-contained') + 1], 'true')
                self.assertEqual(call[call.index('--framework') + 1], 'net8.0')
                self.assertEqual(call[call.index('-r') + 1], 'linux-x64')
                self.assertEqual(call[call.index('-c') + 1], 'Debug')
            self.assertIn('Config/THSSS.Config.csproj', calls[0][1])
            self.assertIn('ConfigLauncher/THSSS.ConfigLauncher.csproj', calls[1][1])
            self.assertIn(f"-p:THSSSMathMode={mode}", calls[2])
            self.assertIn("-p:THSSSManagedDesFallback=true", calls[2])
            stage = Path(calls[2][calls[2].index("-o") + 1])
            self.assertEqual(Path(calls[0][calls[0].index('-o') + 1]), stage / 'ConfigApp')
            self.assertEqual(Path(calls[1][calls[1].index('-o') + 1]), stage)
            self.assertNotEqual((stage / 'libSkiaSharp.so').read_text(), (stage / 'ConfigApp/libSkiaSharp.so').read_text())
            self.assertEqual(stage.parent, self.root / "dist")
            if mode != "auto":
                self.assertTrue(stage.name.endswith("-" + mode))
            else:
                self.assertRegex(stage.name, r"^thsss-\d{8}-\d{6}$")
            check_archive(stage, mode, "linux-x64")
            (self.root / "publish-args.jsonl").unlink()


if __name__ == "__main__":
    unittest.main()
