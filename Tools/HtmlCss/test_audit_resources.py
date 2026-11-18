"""资源闭包门禁必须拒绝遗漏，且使用当前原生解析器。"""
import json
import hashlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class ResourceAuditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.assets = self.root / "assets"
        self.assets.mkdir()
        self.library = os.environ["URUI_NATIVE_LIBRARY"]

    def write(self, name, text):
        target = self.assets / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)
        return target

    def page(self, body="", head=""):
        return self.write("page.html", "<!doctype html><html><head>" + head + "</head><body>" + body + "</body></html>")

    def run_audit(self, inventory=None, mounts=None):
        report = self.root / "report.json"
        command = [sys.executable, str(Path(__file__).with_name("audit_resources.py")),
                   "--root", str(self.assets), "--output", str(report), "--native-library", self.library]
        if inventory is not None:
            path = self.root / "inventory.json"
            path.write_text(json.dumps(inventory))
            command.extend(["--inventory", str(path)])
        for prefix, directory in (mounts or {}).items():
            command.extend(["--mount", prefix + "=" + directory])
        run = subprocess.run(command, capture_output=True, text=True)
        self.assertTrue(report.exists(), run.stdout + run.stderr)
        return run.returncode, json.loads(report.read_text())

    def test_shared_native_parser_and_complete_image_font_css_closure(self):
        self.page("<img src='images/a%20b.png'>", "<link rel='stylesheet' href='css/page.css'>")
        self.write("css/page.css", "@font-face{font-family:Fixture;src:url('../fonts/font.ttf.bytes');}body{background-image:url(../images/a\\ b.png);}")
        self.write("images/a b.png", "image bytes")
        self.write("fonts/font.ttf.bytes", "font bytes")
        code, report = self.run_audit()
        self.assertEqual(code, 0, report)
        self.assertEqual(report["status"], "passed")
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(set(page["closure"]), {"css/page.css", "images/a b.png", "fonts/font.ttf.bytes"})
        self.assertTrue(all(len(row["sha256"]) == 64 for row in report["files"]))

    def test_missing_dependency_fails_even_with_valid_markup(self):
        self.page("<img src='missing.png'>")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertIn("RESOURCE_MISSING", {row["code"] for row in report["diagnostics"]})

    def test_browser_mount_and_css_relative_font_share_the_actual_file_identity(self):
        self.page(head="<link rel='stylesheet' href='/urui/base.css'>")
        self.write("package/base.css", "@font-face{font-family:Fixture;src:url('Fonts/font.ttf.bytes');}")
        self.write("package/Fonts/font.ttf.bytes", "font bytes")
        code, report = self.run_audit(mounts={"/urui": "package"})
        self.assertEqual(code, 0, report)
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(set(page["closure"]), {"package/base.css", "package/Fonts/font.ttf.bytes"})

    def test_absolute_url_dot_segments_normalize_before_selecting_a_mount(self):
        self.page(head="<link rel='stylesheet' href='/urui/../shared.css'>")
        self.write("vendor/package/base.css", "body{color:red;}")
        self.write("shared.css", "body{color:blue;}")
        code, report = self.run_audit(mounts={"/urui": "vendor/package"})
        self.assertEqual(code, 0, report)
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(page["closure"], ["shared.css"])

    def test_css_parent_url_uses_its_public_url_before_filesystem_mounting(self):
        self.page(head="<link rel='stylesheet' href='/urui/base.css'>")
        self.write("vendor/package/base.css", "body{background-image:url('../image.png');}")
        self.write("image.png", "image bytes")
        code, report = self.run_audit(mounts={"/urui": "vendor/package"})
        self.assertEqual(code, 0, report)
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(set(page["closure"]), {"vendor/package/base.css", "image.png"})

    def test_unmounted_source_keeps_its_physical_relative_resource_identity(self):
        self.write("URUI/samples/page.html", "<!doctype html><html><head></head><body><img src='icon.png'></body></html>")
        self.write("URUI/samples/icon.png", "sample image")
        self.write("game-ui/unused.css", "body{color:red;}")
        code, report = self.run_audit(mounts={"/URUI": "game-ui"})
        self.assertEqual(code, 0, report)

    def test_stylesheet_links_require_real_css_files(self):
        self.page(head="<link rel='stylesheet' href=''><link rel='stylesheet' href='style.txt'>")
        self.write("style.txt", "body{color:red;}")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertEqual(sum(row["code"] == "STYLESHEET_URL" for row in report["diagnostics"]), 2)

    def test_path_escape_and_external_authority_are_not_local_dependencies(self):
        self.page("<img src='../outside.png'><img src='//other.test/a.png'>")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertTrue({"RESOURCE_ESCAPE", "RESOURCE_EXTERNAL"}.issubset({row["code"] for row in report["diagnostics"]}))

    def test_symlink_escape_is_rejected(self):
        outside = self.root / "outside.png"
        outside.write_bytes(b"external")
        (self.assets / "linked.png").symlink_to(outside)
        self.page("<img src='linked.png'>")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertIn("RESOURCE_ESCAPE", {row["code"] for row in report["diagnostics"]})

    def test_fragment_requires_explicit_inventory_role(self):
        self.write("item.html", "<div>Item</div>")
        code, _ = self.run_audit()
        self.assertNotEqual(code, 0)
        code, report = self.run_audit({"files": [{"path": "item.html", "kind": "fragment"}]})
        self.assertEqual(code, 0, report)

    def test_declared_owned_fragment_is_in_dependency_closure(self):
        self.page(head="<link rel='urui-fragment' href='item.html'>")
        self.write("item.html", "<div><img src='item.png'></div>")
        self.write("item.png", "image")
        code, report = self.run_audit({"files": [{"path": "item.html", "kind": "fragment"}]})
        self.assertEqual(code, 0, report)
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(set(page["closure"]), {"item.html", "item.png"})

    def test_declared_owned_fragment_must_exist(self):
        self.page(head="<link rel='urui-fragment' href='missing.html'>")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0, report)
        self.assertIn("RESOURCE_MISSING", {row["code"] for row in report["diagnostics"]})

    def test_fragment_relative_images_use_owner_document_url(self):
        self.page(head="<link rel='urui-fragment' href='items/item.html'>")
        self.write("items/item.html", "<img src='image.png'>")
        self.write("image.png", "image")
        code, report = self.run_audit()
        self.assertEqual(code, 0, report)

    def runtime_inventory(self, page, source):
        provider = self.write("Provider.cs", 'new ImageAsset("' + source + '", texture);')
        return {"runtimeResources": [{"path": "page.html", "source": source, "provider": "Provider.cs",
                                     "providerSha256": hashlib.sha256(provider.read_bytes()).hexdigest(),
                                     "ownerSha256": hashlib.sha256(page.read_bytes()).hexdigest()}]}

    def test_runtime_image_requires_an_exact_current_owner_and_provider_declaration(self):
        page = self.page("<img src='memory.png'>")
        inventory = self.runtime_inventory(page, "memory.png")
        code, report = self.run_audit(inventory)
        self.assertEqual(code, 0, report)
        owner = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(owner["runtimeDependencies"][0]["source"], "memory.png")

    def test_repository_inventory_is_discovered_without_an_extra_cli_argument(self):
        page = self.page("<img src='memory.png'>")
        inventory = self.runtime_inventory(page, "memory.png")
        self.write("URUI/Tools/HtmlCss/resource_inventory.json", json.dumps(inventory))
        code, report = self.run_audit()
        self.assertEqual(code, 0, report)

    def test_stale_runtime_provider_does_not_hide_a_missing_image(self):
        page = self.page("<img src='memory.png'>")
        inventory = self.runtime_inventory(page, "memory.png")
        self.write("Provider.cs", "changed provider")
        code, report = self.run_audit(inventory)
        self.assertNotEqual(code, 0, report)
        self.assertIn("RUNTIME_PROVIDER", {row["code"] for row in report["diagnostics"]})

    def test_runtime_image_declaration_cannot_replace_font_bytes(self):
        page = self.page(head="<style>@font-face{font-family:Local;src:url(memory.ttf);}</style>")
        code, report = self.run_audit(self.runtime_inventory(page, "memory.ttf"))
        self.assertNotEqual(code, 0, report)
        self.assertIn("RESOURCE_MISSING", {row["code"] for row in report["diagnostics"]})

    def test_strict_end_tags_cannot_pass_as_fragments(self):
        self.write("item.html", "<div><span>Broken</span>")
        code, report = self.run_audit({"files": [{"path": "item.html", "kind": "fragment"}]})
        self.assertNotEqual(code, 0)
        self.assertTrue(any(row["code"].startswith("HTML_") for row in report["diagnostics"]))

    def test_inline_and_embedded_css_use_native_url_tokens(self):
        self.page("<div style=\"background-image:url('inline image.png');\"></div>",
                  "<style>body{mask-image:url(embedded.png);--label:'url(fake.png)';}</style>")
        self.write("inline image.png", "inline")
        self.write("embedded.png", "embedded")
        code, report = self.run_audit()
        self.assertEqual(code, 0, report)
        page = next(row for row in report["files"] if row["path"] == "page.html")
        self.assertEqual(set(page["closure"]), {"inline image.png", "embedded.png"})

    def test_legacy_files_and_unsupported_css_fail(self):
        self.write("old.css.txt", "body{width:1dp;}")
        self.write("old.txt", "<rml><body></body></rml>")
        self.write("bad.css", "@import 'hidden.css';body{not-a-property:1;}")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertEqual(sum(row["code"] == "LEGACY_FORMAT" for row in report["diagnostics"]), 2)
        self.assertTrue(any(row["code"].startswith("CSS_") for row in report["diagnostics"]))

    def test_explicit_css_role_cannot_keep_a_plain_text_extension(self):
        self.write("style.txt", "body{color:red;}")
        code, report = self.run_audit({"files": [{"path": "style.txt", "kind": "css"}]})
        self.assertNotEqual(code, 0)
        self.assertIn("LEGACY_FORMAT", {row["code"] for row in report["diagnostics"]})

    def test_invalid_utf8_and_nul_never_reach_c_strings(self):
        (self.assets / "bad.css").write_bytes(b"body{color:red;}\x00hidden")
        (self.assets / "bad.html").write_bytes(b"\xff")
        code, report = self.run_audit()
        self.assertNotEqual(code, 0)
        self.assertEqual({row["code"] for row in report["diagnostics"]}, {"SOURCE_ENCODING"})


if __name__ == "__main__":
    unittest.main()
