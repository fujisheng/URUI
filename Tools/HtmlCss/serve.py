"""本地静态预览：保留 HTML/CSS/字体原始字节，不生成浏览器补丁。"""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit


class StaticHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, root, **kwargs):
        self.root = root
        super().__init__(*args, directory=str(root), **kwargs)

    def translate_path(self, path):
        # 别名与运行时资源 URI 一致；仍然只读同一仓库里的原文件。
        aliases = {
            "/urui/": "URUI/Packages/com.fujisheng.urui/Runtime/Resources/URUI",
            "/URUI/": "Client/Assets/Resources/URUI",
            "/Assets/": "Client/Assets",
            "/urui-effect/": "URUI/Packages/com.fujisheng.urui.effect/Runtime/Resources/URUI.Effect",
            "/samples/urui-effects/": "URUI/Packages/com.fujisheng.urui.effect/Samples~/EffectGallery",
            "/fixtures/": "URUI/Packages/com.fujisheng.urui/Tests~/HtmlCss/fixtures",
        }
        try:
            decoded = unquote(urlsplit(path).path, errors="strict")
            if "\0" in decoded or "\\" in decoded or ".." in decoded.split("/"): raise ValueError("invalid path")
            target = self.root / decoded.lstrip("/")
            for prefix, directory in aliases.items():
                if decoded.startswith(prefix):
                    target = self.root / directory / decoded[len(prefix):]
                    break
            target = target.resolve()
            target.relative_to(self.root)
            return str(target)
        except (UnicodeError, ValueError, OSError):
            # 不将坏地址规范化成其他可读取资源。
            return str(self.root / ".urui-invalid-preview-request" / "missing")

    def guess_type(self, path):
        if str(path).lower().endswith(".ttf.bytes"): return "font/ttf"
        if str(path).lower().endswith(".otf.bytes"): return "font/otf"
        return super().guess_type(path)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, *args):
        pass


def create_server(root, port):
    root = Path(root).resolve(strict=True)
    if not root.is_dir(): raise ValueError("Preview root must be a directory")
    return ThreadingHTTPServer(("127.0.0.1", port), partial(StaticHandler, root=root))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    with create_server(args.root, args.port) as server:
        print(f"URUI preview: http://127.0.0.1:{server.server_port}/", flush=True)
        try: server.serve_forever()
        except KeyboardInterrupt: pass


if __name__ == "__main__":
    main()
