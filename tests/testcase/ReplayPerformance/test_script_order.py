#!/usr/bin/env python3
"""Asset byte equality must not hide a changed controller creation order."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location(
    "replay_profile", Path(__file__).with_name("run_profile.py")
)
assert SPEC is not None and SPEC.loader is not None
profile = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(profile)


class ScriptOrderTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / "game"
        for name in (
            "Image",
            "Model",
            "FX",
            "Sound",
            "Story",
            "BGM",
            "CS",
            "BPic",
            "Replay",
        ):
            (self.game / name).mkdir(parents=True)
        scripts = self.game / "CS/St06/A/H"
        scripts.mkdir(parents=True)
        for name in ("first.mbg", "second.mbg", "third.mbg"):
            (scripts / name).write_bytes(b"same script bytes")
        (self.game / "Setting.INI").write_bytes(b"UseDirectInput=1\r\n")
        (self.game / "Replay/thSSS_25.rpy").write_bytes(b"private replay fixture")
        self.output = self.root / "output"
        self.output.mkdir()

    def test_original_order_is_preserved_and_recorded(self):
        with patch.object(profile, "ROOT", self.root):
            data = profile.stage_assets(self.output)
        evidence = json.loads((self.output / "script-enumeration.json").read_text())
        self.assertEqual(evidence["source"], evidence["copied"])
        self.assertEqual((data / "Setting.INI").read_bytes(), b"UseDirectInput=0\r\n")
        self.assertEqual(
            (self.game / "Setting.INI").read_bytes(), b"UseDirectInput=1\r\n"
        )
        self.assertEqual(
            (data / "Replay/thSSS_25.rpy").read_bytes(), b"private replay fixture"
        )

    def test_identical_bytes_with_reverse_order_are_rejected(self):
        original_copytree = profile.shutil.copytree

        def copytree(source, target, *args, **kwargs):
            result = original_copytree(source, target, *args, **kwargs)
            if Path(source).name == "CS":
                directory = Path(target) / "St06/A/H"
                entries = [(p.name, p.read_bytes()) for p in directory.iterdir()]
                for p in directory.iterdir():
                    p.unlink()
                for name, contents in reversed(entries):
                    (directory / name).write_bytes(contents)
            return result

        with (
            patch.object(profile, "ROOT", self.root),
            patch.object(profile.shutil, "copytree", copytree),
            patch.object(profile, "restore_copied_script_order", return_value=None),
        ):
            with self.assertRaisesRegex(RuntimeError, "enumeration differs"):
                profile.stage_assets(self.output)
        evidence = json.loads((self.output / "script-enumeration.json").read_text())
        self.assertEqual(
            evidence["source"]["St06/A/H"],
            list(reversed(evidence["copied"]["St06/A/H"])),
        )
        self.assertFalse((self.output / "data/Replay").exists())


if __name__ == "__main__":
    unittest.main()
