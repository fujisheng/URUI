"""同源对照门禁：失败、边界和缺证据必须有不同结果。"""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image
from compare import compare_snapshots


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.browser = self.snapshot("browser")
        self.unity = self.snapshot("unity")

    def snapshot(self, runtime):
        image = self.root / (runtime + ".png")
        Image.new("RGBA", (16, 16), (51, 102, 153, 255)).save(image)
        digest = "a" * 64
        return {"schemaVersion": 1, "runtime": runtime, "viewport": {"width": 16, "height": 16, "dpr": 1},
                "time": 0, "inputs": {"page.html": digest, "font.ttf": digest}, "fonts": {"font.ttf": digest},
                "fontsLoaded": True, "screenshot": image.name,
                "screenshotSha256": hashlib.sha256(image.read_bytes()).hexdigest(),
                "nodes": {"box": {"tag": "div", "parent": None, "rect": [1, 2, 10, 8], "text": "Text",
                                   "styles": {"font-size": 16, "visibility": "visible"},
                                   "lines": [{"rect": [1, 2, 10, 8], "baseline": 9}]}},
                "flatRegions": {"fill": [3, 3, 2, 2]}}

    def compare(self):
        for name, snapshot in (("browser", self.browser), ("unity", self.unity)):
            (self.root / (name + ".json")).write_text(json.dumps(snapshot))
        return compare_snapshots(self.root / "browser.json", self.root / "unity.json")

    def pixel(self, delta):
        image = self.root / "unity.png"
        Image.new("RGBA", (16, 16), (51 + delta, 102, 153, 255)).save(image)
        self.unity["screenshotSha256"] = hashlib.sha256(image.read_bytes()).hexdigest()

    def test_equal_evidence_passes(self):
        self.assertEqual(self.compare()["status"], "passed")

    def test_one_css_pixel_shift_fails(self):
        self.unity["nodes"]["box"]["rect"][0] += 1
        self.assertEqual(self.compare()["status"], "failed")

    def test_half_pixel_boundary_passes(self):
        self.unity["nodes"]["box"]["rect"][0] += .5
        self.assertEqual(self.compare()["status"], "passed")

    def test_missing_text_line_fails(self):
        self.unity["nodes"]["box"]["lines"] = []
        self.assertEqual(self.compare()["status"], "failed")

    def test_baseline_boundary_passes_and_exceeding_fails(self):
        self.unity["nodes"]["box"]["lines"][0]["baseline"] += 1
        self.assertEqual(self.compare()["status"], "passed")
        self.unity["nodes"]["box"]["lines"][0]["baseline"] += .001
        self.assertEqual(self.compare()["status"], "failed")

    def test_three_channel_steps_fail(self):
        self.pixel(3)
        self.assertEqual(self.compare()["status"], "failed")

    def test_two_channel_steps_boundary_passes(self):
        self.pixel(2)
        self.assertEqual(self.compare()["status"], "passed")

    def test_no_screenshot_is_unverified(self):
        (self.root / "unity.png").unlink()
        self.assertEqual(self.compare()["status"], "unverified")

    def test_changed_screenshot_bytes_are_unverified(self):
        Image.new("RGBA", (16, 16), (0, 0, 0, 255)).save(self.root / "unity.png")
        self.assertEqual(self.compare()["status"], "unverified")

    def test_missing_or_mismatched_fonts_are_unverified(self):
        for fonts in ({}, {"font.ttf": "b" * 64}):
            with self.subTest(fonts=fonts):
                self.unity["fonts"] = fonts
                self.assertEqual(self.compare()["status"], "unverified")

    def test_pending_fonts_are_unverified(self):
        self.unity["fontsLoaded"] = False
        self.assertEqual(self.compare()["status"], "unverified")

    def test_mismatched_inputs_are_unverified(self):
        self.unity["inputs"]["page.html"] = "b" * 64
        self.assertEqual(self.compare()["status"], "unverified")

    def test_different_viewport_or_clock_are_unverified(self):
        original = copy.deepcopy(self.unity)
        for key, value in (("viewport", {"width": 16, "height": 16, "dpr": 2}), ("time", 1)):
            with self.subTest(key=key):
                self.unity = copy.deepcopy(original); self.unity[key] = value
                self.assertEqual(self.compare()["status"], "unverified")

    def test_missing_critical_node_is_unverified(self):
        self.unity["nodes"].clear()
        self.assertEqual(self.compare()["status"], "unverified")

    def test_missing_style_and_line_evidence_are_unverified(self):
        original = copy.deepcopy(self.unity)
        for field in ("styles", "lines"):
            with self.subTest(field=field):
                self.unity = copy.deepcopy(original); del self.unity["nodes"]["box"][field]
                self.assertEqual(self.compare()["status"], "unverified")

    def test_text_and_style_differences_fail(self):
        original = copy.deepcopy(self.unity)
        for field, value in (("text", "Lost"), ("styles", {"font-size": 18, "visibility": "visible"})):
            with self.subTest(field=field):
                self.unity = copy.deepcopy(original); self.unity["nodes"]["box"][field] = value
                self.assertEqual(self.compare()["status"], "failed")

    def test_no_flat_pixel_probes_are_unverified(self):
        self.unity["flatRegions"].clear(); self.browser["flatRegions"].clear()
        self.assertEqual(self.compare()["status"], "unverified")

    def test_both_captures_missing_visible_text_lines_are_unverified(self):
        for snapshot in (self.browser, self.unity): snapshot["nodes"]["box"]["lines"] = []
        self.assertEqual(self.compare()["status"], "unverified")

    def test_invalid_numeric_or_outside_probe_is_unverified(self):
        original = copy.deepcopy(self.unity)
        for field, value in (("time", float("nan")), ("flatRegions", {"fill": [16, 16, 2, 2]})):
            with self.subTest(field=field):
                self.unity = copy.deepcopy(original); self.unity[field] = value
                self.assertEqual(self.compare()["status"], "unverified")


if __name__ == "__main__":
    unittest.main()
