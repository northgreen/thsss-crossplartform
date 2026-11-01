"""Run the BCL launcher against a real, isolated fake child; never publish the game."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[3]


class LauncherTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="thsss-config-launcher-")
        cls.addClassCleanup(cls.temp.cleanup)
        cls.root = Path(cls.temp.name)
        cls.launcher = cls.root / "package with spaces"
        project = REPO / "src/ConfigLauncher/THSSS.ConfigLauncher.csproj"
        cls.build(project, cls.launcher, cls.root / "launcher-artifacts")
        child = cls.root / "child"
        child.mkdir()
        (child / "Child.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable></PropertyGroup><ItemGroup>'
            f'<Compile Include="{escape(str(REPO / "src/Config/ConfigWorkspace.cs"))}" Link="ConfigWorkspace.cs" />'
            '</ItemGroup></Project>'
        )
        (child / "Program.cs").write_text("""using System;
using System.IO;
using System.Text.Json;
using THSSS.Config;
string? marker = Environment.GetEnvironmentVariable("THSSS_TEST_GAME_MARKER");
if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "THSSS" && marker != null)
{
    File.WriteAllText(marker, JsonSerializer.Serialize(new { root = Environment.GetEnvironmentVariable("THSSS_DATA_ROOT"), cwd = Directory.GetCurrentDirectory() }));
    return 37;
}
if (args.Length == 1 && args[0] == "--apply-start-game")
{
    var workspace = new ConfigWorkspace(Environment.GetEnvironmentVariable("THSSS_DATA_ROOT")!);
    if (!workspace.LaunchGame(out string message)) throw new Exception(message);
}
Console.WriteLine(JsonSerializer.Serialize(new { args, cwd = Directory.GetCurrentDirectory(), root = Environment.GetEnvironmentVariable("THSSS_DATA_ROOT") }));
return 37;
namespace THSSS.Config { internal static class ConfigSettings { public const string FileName = "Setting.INI"; } }
""")
        cls.build(
            child / "Child.csproj",
            cls.launcher / "ConfigApp",
            cls.root / "child-artifacts",
        )
        cls.extension = ".exe" if os.name == "nt" else ""
        (cls.launcher / ("ConfigApp/Child" + cls.extension)).rename(
            cls.launcher / ("ConfigApp/Config" + cls.extension)
        )
        import shutil
        shutil.copy2(cls.launcher / ("ConfigApp/Config" + cls.extension), cls.launcher / ("THSSS" + cls.extension))
        for name in ('Child.dll', 'Child.deps.json', 'Child.runtimeconfig.json'):
            shutil.copy2(cls.launcher / 'ConfigApp' / name, cls.launcher / name)

    @staticmethod
    def build(project, output, artifacts):
        result = subprocess.run(
            [
                "dotnet",
                "build",
                str(project),
                "-f",
                "net8.0",
                "-c",
                "Release",
                "-warnaserror",
                "-o",
                str(output),
                "--artifacts-path",
                str(artifacts),
            ],
            cwd=REPO,
            capture_output=True,
            text=True,
        )
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)

    def launch(self, args=(), data_root=None, extra_env=None):
        env = dict(os.environ)
        env.pop("THSSS_DATA_ROOT", None)
        if data_root is not None:
            env["THSSS_DATA_ROOT"] = data_root
        env.update(extra_env or {})
        return subprocess.run(
            [str(self.launcher / ("Config" + self.extension)), *args],
            cwd=self.root,
            env=env,
            capture_output=True,
            text=True,
        )

    def test_args_cwd_default_root_exit_code(self):
        arguments = [
            "space value",
            "",
            '"quoted"',
            "; touch INJECTION",
            "$(echo injected)",
            "日本語",
            "ends\\",
        ]
        result = self.launch(arguments)
        self.assertEqual(result.returncode, 37, result.stderr)
        observation = json.loads(result.stdout)
        self.assertEqual(observation["args"], arguments)
        self.assertEqual(Path(observation["cwd"]), self.launcher)
        self.assertEqual(Path(observation["root"]), self.launcher)
        self.assertFalse((self.launcher / "INJECTION").exists())

    def test_inherited_data_root_is_unchanged(self):
        data_root = str(self.root / "external player data")
        Path(data_root).mkdir(exist_ok=True)
        marker = Path(data_root) / 'Score.dat'
        marker.write_bytes(b'untouched fake player file')
        result = self.launch(data_root=data_root)
        self.assertEqual(result.returncode, 37, result.stderr)
        observation = json.loads(result.stdout)
        self.assertEqual(observation["root"], data_root)
        self.assertEqual(Path(observation["cwd"]), Path(data_root))
        self.assertEqual(marker.read_bytes(), b'untouched fake player file')

    def test_relative_data_root_is_resolved_before_changing_directory(self):
        relative = 'relative player data'
        expected = self.root / relative
        expected.mkdir(exist_ok=True)
        result = self.launch(data_root=relative)
        self.assertEqual(result.returncode, 37, result.stderr)
        observation = json.loads(result.stdout)
        self.assertEqual(Path(observation['root']), expected)
        self.assertEqual(Path(observation['cwd']), expected)

    def test_external_data_apply_and_launch_finds_package_game(self):
        data_root = self.root / 'external apply-and-launch data'
        data_root.mkdir(exist_ok=True)
        marker = data_root / 'game-start.json'
        result = self.launch(['--apply-start-game'], str(data_root), {'THSSS_TEST_GAME_MARKER': str(marker)})
        self.assertEqual(result.returncode, 37, result.stderr)
        deadline = time.monotonic() + 10
        while not marker.is_file() and time.monotonic() < deadline:
            time.sleep(.05)
        self.assertTrue(marker.is_file(), 'ConfigWorkspace failed to launch the actual package-root game')
        observation = json.loads(marker.read_text())
        self.assertEqual(Path(observation['root']), data_root)
        self.assertEqual(Path(observation['cwd']), self.launcher)

    def test_missing_data_root_fails_without_creating_it(self):
        data_root = self.root / 'missing player data'
        result = self.launch(data_root=str(data_root))
        self.assertEqual(result.returncode, 1)
        self.assertIn('cannot run', result.stderr)
        self.assertFalse(data_root.exists())

    def test_empty_data_root_uses_package_root(self):
        result = self.launch(data_root="")
        self.assertEqual(result.returncode, 37, result.stderr)
        self.assertEqual(Path(json.loads(result.stdout)["root"]), self.launcher)

    def test_missing_child_fails_clearly_without_data_changes(self):
        app = self.launcher / "ConfigApp"
        hidden = self.launcher / "hidden-child"
        app.rename(hidden)
        try:
            result = self.launch()
            self.assertEqual(result.returncode, 1)
            self.assertIn("missing configuration application", result.stderr)
            self.assertIn(str(app / ("Config" + self.extension)), result.stderr)
            self.assertFalse(app.exists())
        finally:
            hidden.rename(app)


if __name__ == "__main__":
    unittest.main()
