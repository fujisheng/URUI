"""预览服务器只读取原文件，路径和别名不能逃出声明根。"""
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import urlopen

from serve import create_server


class StaticServerTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory(); self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        (self.root / "page.html").write_bytes(b"<!doctype html><html><head></head><body></body></html>")
        self.server = create_server(self.root, 0)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()
        self.addCleanup(self.close)
        self.url = "http://127.0.0.1:" + str(self.server.server_port)

    def close(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join()

    def test_html_bytes_are_unchanged(self):
        with urlopen(self.url + "/page.html") as response:
            self.assertEqual(response.read(), (self.root / "page.html").read_bytes())
            self.assertEqual(response.headers.get_content_type(), "text/html")

    def test_unknown_file_has_no_html_fallback(self):
        with self.assertRaises(HTTPError) as caught: urlopen(self.url + "/missing.css")
        self.assertEqual(caught.exception.code, 404)

    def test_escape_and_invalid_utf8_are_rejected(self):
        for path in ("/%2e%2e/secret", "/%FF", "/%00"):
            with self.subTest(path=path), self.assertRaises(HTTPError) as caught: urlopen(self.url + path)
            self.assertEqual(caught.exception.code, 404)

    def test_symlink_escape_is_rejected(self):
        (self.root / "escape").symlink_to(self.root.parent, target_is_directory=True)
        with self.assertRaises(HTTPError) as caught: urlopen(self.url + "/escape/")
        self.assertEqual(caught.exception.code, 404)

    def test_repo_mount_serves_css_without_rewriting(self):
        target = self.root / "URUI/Packages/com.fujisheng.urui/Runtime/Resources/URUI/base.css"
        target.parent.mkdir(parents=True); target.write_bytes(b"body{color:#334455;}\n")
        with urlopen(self.url + "/urui/base.css") as response:
            self.assertEqual(response.read(), target.read_bytes())
            self.assertEqual(response.headers.get_content_type(), "text/css")


if __name__ == "__main__":
    unittest.main()
