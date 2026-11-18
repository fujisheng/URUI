"""能力清单必须覆盖默认、运行时及未被游戏使用的能力。"""
import json
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from inventory import collect


class InventoryTests(unittest.TestCase):
    def test_build_and_license_text_are_not_author_markup_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            native = root / "URUI/Packages/com.fujisheng.urui/Native~"
            native.mkdir(parents=True)
            (native / "CMakeLists.txt").write_text('message("use <target> here")')
            (native / "LICENSE.txt").write_text('Contact <author@example.invalid>')
            resources = root / "URUI/Packages/com.fujisheng.urui/Runtime/Resources"
            resources.mkdir(parents=True)
            legacy = resources / "Legacy.txt"
            legacy.write_text('<rml><body></body></rml>')
            _, report = collect(root, None)
            self.assertEqual(report["assets"]["legacy_markup"], [legacy.relative_to(root).as_posix()])

    def test_hashes_packaged_font_bytes_without_treating_them_as_markup(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fonts = root / "URUI/Packages/com.fujisheng.urui/Runtime/Resources/URUI/Fonts"
            fonts.mkdir(parents=True)
            data = b"\x00\xfffont<body>binary\x80"
            for name in ("Default.ttf.bytes", "Author.woff2"):
                (fonts / name).write_bytes(data)
            _, report = collect(root, None)
            expected = sorted(str((fonts / name).relative_to(root))
                              for name in ("Default.ttf.bytes", "Author.woff2"))
            self.assertEqual(sorted(report["assets"].get("fonts", [])), expected)
            self.assertEqual(report["assets"]["legacy_markup"], [])
            for record in report["files"]:
                self.assertEqual(record["sha256"], hashlib.sha256(data).hexdigest())
                self.assertEqual(record["bytes"], len(data))

    def test_keeps_runtime_and_unused_properties(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            native = root / "URUI/Packages/com.fujisheng.urui/Native~"
            core = native / "Engine/Source/Core"
            core.mkdir(parents=True)
            (core / "StyleSheetSpecification.cpp").write_text(
                'RegisterProperty(PropertyId::Width, "width", "auto", false, true)'
                '.AddParser("length_percent");\n'
                'RegisterProperty(PropertyId::Float, "float", "none", false, true)'
                '.AddParser("keyword", "none, left, right");\n'
                'RegisterShorthand(ShorthandId::Margin, "margin", '
                '"margin-top, margin-right, margin-bottom, margin-left", ShorthandType::Box);\n'
            )
            (native / "image_geometry.h").write_text(
                'auto& fit = URUI::StyleSheetSpecification::RegisterProperty("object-fit", "fill", false);\n'
                'fit.AddParser("keyword", "fill, contain");\n'
                'for (const char* name : {"--urui-image-slice-top", "--urui-image-slice-right"})\n'
                'RegisterProperty(name, "0px", false).AddParser("length");\n'
            )
            controls = root / "URUI/Packages/com.fujisheng.urui/Runtime/Controls"
            controls.mkdir(parents=True)
            (controls / "ScrollMotion.cs").write_text('var sensitivity = Number("--urui-scroll-sensitivity");')
            (core / "CssParser.cpp").write_text('if (rule.atRule == "media") {}\n')
            (core / "StyleSheetParser.cpp").write_text(
                '\n'.join('if (rule.atRule == "' + name + '") {}'
                          for name in ("keyframes", "font-face", "property", "decorator", "spritesheet"))
            )
            snapshot = root / "native.json"
            snapshot.write_text(json.dumps({"properties": [
                {"name": "width", "initial": "auto", "inherited": False},
                {"name": "float", "initial": "none", "inherited": False},
                {"name": "object-fit", "initial": "fill", "inherited": False},
                {"name": "--urui-image-fill-amount", "initial": "1", "inherited": False},
                {"name": "--urui-image-slice-right", "initial": "0px", "inherited": False},
                {"name": "--urui-language", "initial": "", "inherited": True},
            ], "shorthands": ["margin"]}))
            output = root / "evidence"
            run = subprocess.run(
                [sys.executable, str(Path(__file__).with_name("inventory.py")),
                 "--root", str(root), "--output", str(output),
                 "--native-snapshot", str(snapshot)], capture_output=True, text=True
            )
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            entries = json.loads((output / "support-manifest.json").read_text())["entries"]
            names = {(entry["category"], entry["name"]) for entry in entries}
            self.assertIn(("property", "float"), names)
            self.assertIn(("property", "object-fit"), names)
            self.assertIn(("property", "--urui-image-fill-amount"), names)
            self.assertIn(("shorthand", "margin"), names)
            self.assertIn(("property", "--urui-scroll-sensitivity"), names)
            for name in ("media", "keyframes", "font-face", "property", "decorator", "spritesheet"):
                self.assertIn(("at-rule", "@" + name), names)
            spritesheet = next(e for e in entries if e["name"] == "@spritesheet")
            self.assertEqual(spritesheet["status"], "remove")
            self.assertEqual(spritesheet["test"]["owner"], "T11")
            fill = next(e for e in entries if e["name"] == "--urui-image-fill-amount")
            self.assertEqual(fill["status"], "extension")
            self.assertEqual(fill["initial"], "1")
            language = next(e for e in entries if e["name"] == "--urui-language")
            self.assertEqual(language["status"], "extension")
            fit = next(e for e in entries if e["name"] == "object-fit")
            self.assertEqual(fit["values"], [["keyword", "fill, contain"]])
            sliced = next(e for e in entries if e["name"] == "--urui-image-slice-right")
            self.assertEqual(sliced["values"], [["length", ""]])

    def test_missing_runtime_snapshot_is_reported_as_incomplete(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "evidence"
            run = subprocess.run(
                [sys.executable, str(Path(__file__).with_name("inventory.py")),
                 "--root", str(root), "--output", str(output)],
                capture_output=True, text=True,
            )
            self.assertEqual(run.returncode, 3, run.stdout + run.stderr)
            report = json.loads((output / "inventory.json").read_text())
            self.assertEqual(report["runtimeCoverage"], "unverified")


if __name__ == "__main__":
    unittest.main()
