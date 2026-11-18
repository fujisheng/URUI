import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image
from prepare_player import prepare_manifest


class PlayerInputsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.html = b"<!doctype html><html><head></head><body></body></html>"
        (self.root / "page.html").write_bytes(self.html)
        self.digest = hashlib.sha256(self.html).hexdigest()
        self.inputs = {"root": str(self.root), "cases": [{"name": "page", "uri": "/page.html", "path": "page.html", "resources": [{"uri": "/page.html", "path": "page.html", "sha256": self.digest}], "regions": [{"name": "fill", "rect": [0, 0, 1, 1]}]}], "viewports": [{"width": 2, "height": 2}]}
        self.directory = self.root / "browser/page-2x2"
        self.directory.mkdir(parents=True)
        Image.new("RGBA", (2, 2), (30, 60, 90, 255)).save(self.directory / "browser.png")
        self.snapshot = {"schemaVersion": 1, "runtime": "browser", "time": 0, "viewport": {"width": 2, "height": 2, "dpr": 1}, "fontsLoaded": True, "fonts": {}, "inputs": {"/page.html": self.digest}, "screenshot": "browser.png", "screenshotSha256": hashlib.sha256((self.directory / "browser.png").read_bytes()).hexdigest(), "flatRegions": {"fill": [0, 0, 1, 1]}, "nodes": {"box": {"tag": "div", "parent": None, "rect": [0, 0, 1, 1], "styles": {"font-size": 16, "opacity": 1}}}}
        self.write_snapshot()

    def write_snapshot(self):
        (self.directory / "browser.json").write_text(json.dumps(self.snapshot))

    def prepare(self):
        return prepare_manifest(self.inputs, self.root / "browser", "native-sha")

    def test_preserves_source_bytes_geometry_and_every_probe_channel(self):
        manifest, blobs = self.prepare()
        self.assertEqual([self.html], list(blobs.values()))
        case = manifest["cases"][0]
        self.assertEqual([0, 0, 1, 1], case["nodes"][0]["rect"])
        self.assertEqual([30, 60, 90, 255], case["regions"][0]["rgba"])
        self.assertEqual(self.digest, case["resources"][0]["sha256"])

    def test_rejects_changed_author_bytes_before_bundle_creation(self):
        (self.root / "page.html").write_bytes(self.html + b" ")
        with self.assertRaisesRegex(ValueError, "source changed"):
            self.prepare()

    def test_rejects_browser_from_different_inputs(self):
        self.snapshot["inputs"]["/page.html"] = "stale"
        self.write_snapshot()
        with self.assertRaisesRegex(ValueError, "browser inputs"):
            self.prepare()

    def test_rejects_screenshot_without_matching_hash(self):
        self.snapshot["screenshotSha256"] = "stale"
        self.write_snapshot()
        with self.assertRaisesRegex(ValueError, "screenshot"):
            self.prepare()

    def test_rejects_missing_font_readiness(self):
        self.snapshot["fontsLoaded"] = False
        self.write_snapshot()
        with self.assertRaisesRegex(ValueError, "font"):
            self.prepare()

    def test_rejects_wrong_viewport(self):
        self.snapshot["viewport"]["dpr"] = 2
        self.write_snapshot()
        with self.assertRaisesRegex(ValueError, "viewport"):
            self.prepare()


if __name__ == "__main__":
    unittest.main()
