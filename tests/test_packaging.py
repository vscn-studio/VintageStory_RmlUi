"""Release policy checks. Synthetic binaries exist only in TemporaryDirectory fixtures."""
# Copyright (c) 2026 VSCN-Studio
# SPDX-License-Identifier: MIT

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location("release_build", Path(__file__).resolve().parents[1] / "build.py")
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.patcher = patch.object(build, "ROOT", self.root)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)
        for name in ("native/CMakeLists.txt", "native/bridge.h", "native/bridge.cpp", "src/VSRmlUi/assets/test.txt", "src/VSRmlUi/modicon.png", "licenses/MIT", "LICENSE", "COPYRIGHT.txt", "README.md", "VALIDATION.md", "PLATFORMS.zh-CN.md", "DIRECTOR_MIGRATION_GAPS.zh-CN.md", "artifacts/managed/VSRmlUi.dll", "artifacts/managed/VSRmlUi.xml"):
            p = self.root / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(b"test fixture only")
        (self.root / "build").mkdir()
        self.write_json("src/VSRmlUi/modinfo.json", {"version": "1.0.1"})
        self.write_json("artifacts/managed/manifest.json", {"version": "1.0.1", "sha256": hashlib.sha256(b"test fixture only").hexdigest()})
        for rid in build.REQUIRED:
            self.add_native(rid)

    def write_json(self, name, data):
        (self.root / name).write_text(json.dumps(data), encoding="utf-8")

    def add_native(self, rid):
        folder = self.root / "artifacts/native" / rid
        folder.mkdir(parents=True)
        binary = folder / build.FILES[rid.split('-')[0]]
        binary.write_bytes(b"synthetic package fixture")
        self.write_json(folder / "manifest.json", {"rid": rid, "rmlui": build.REVISION, "bridgeSource": build.bridge_hash(), "sha256": hashlib.sha256(binary.read_bytes()).hexdigest(), "nativeSmoke": True})

    def package(self):
        with contextlib.redirect_stdout(io.StringIO()):
            build.package_merged(self.root / "artifacts/native")

    def add_example(self):
        staged = self.root / "artifacts/example"
        staged.mkdir(parents=True)
        (staged / "VSRmlUi.Example.dll").write_bytes(b"synthetic test mod")
        self.write_json(staged / "modinfo.json", {"modid": "vsrmluiexample", "version": "1.0.1"})
        (staged / "assets/vsrmluiexample/dialog/input-test.rml").parent.mkdir(parents=True)
        (staged / "assets/vsrmluiexample/dialog/input-test.rml").write_text("<rml />", encoding="utf-8")

    def test_single_complete_archive_only(self):
        self.add_native("osx-x64")
        self.add_native("osx-arm64")
        self.package()
        archives = list((self.root / "artifacts").glob("*.zip"))
        self.assertEqual([p.name for p in archives], ["vsrmlui_1.0.1.zip"])
        with zipfile.ZipFile(archives[0]) as z:
            self.assertIsNone(z.testzip())
            self.assertEqual(json.loads(z.read("modinfo.json"))["version"], "1.0.1")
            self.assertEqual(set(z.namelist()), {"VSRmlUi.dll", "modinfo.json", "modicon.png", "LICENSE", "COPYRIGHT.txt", "licenses/MIT", "assets/test.txt", "native/win-x64/vsrmlui_native.dll", "native/linux-x64/libvsrmlui_native.so", "native/osx-x64/libvsrmlui_native.dylib", "native/osx-arm64/libvsrmlui_native.dylib"})

    def test_missing_linux_refuses_partial_archive(self):
        (self.root / "artifacts/native/linux-x64/libvsrmlui_native.so").unlink()
        with self.assertRaisesRegex(RuntimeError, "Missing native builds: linux-x64"):
            self.package()
        self.assertEqual(list((self.root / "artifacts").glob("*.zip")), [])

    def test_modified_native_is_rejected(self):
        (self.root / "artifacts/native/win-x64/vsrmlui_native.dll").write_bytes(b"modified")
        with self.assertRaisesRegex(RuntimeError, "manifest mismatch"):
            self.package()

    def test_wrong_managed_version_is_rejected(self):
        self.write_json("artifacts/managed/manifest.json", {"version": "0.1.1"})
        with self.assertRaisesRegex(RuntimeError, "does not match 1.0.1"):
            self.package()

    def test_failed_update_preserves_existing_release(self):
        self.package()
        release = self.root / "artifacts/vsrmlui_1.0.1.zip"
        original = release.read_bytes()
        (self.root / "artifacts/native/linux-x64/libvsrmlui_native.so").unlink()
        with self.assertRaises(RuntimeError):
            self.package()
        self.assertEqual(release.read_bytes(), original)

    def test_windows_linux_release_without_mac(self):
        self.package()
        with zipfile.ZipFile(self.root / "artifacts/vsrmlui_1.0.1.zip") as z:
            self.assertIn("native/win-x64/vsrmlui_native.dll", z.namelist())
            self.assertIn("native/linux-x64/libvsrmlui_native.so", z.namelist())

    def test_input_diagnostics_archive_is_separate(self):
        self.add_example()
        self.package()
        self.assertEqual(sorted(p.name for p in (self.root / "artifacts").glob("*.zip")), ["vsrmlui-test_1.0.1.zip", "vsrmlui_1.0.1.zip"])
        with zipfile.ZipFile(self.root / "artifacts/vsrmlui-test_1.0.1.zip") as z:
            self.assertEqual(set(z.namelist()), {"VSRmlUi.Example.dll", "modinfo.json", "LICENSE", "COPYRIGHT.txt", "licenses/MIT", "assets/vsrmluiexample/dialog/input-test.rml"})


if __name__ == "__main__":
    unittest.main()
