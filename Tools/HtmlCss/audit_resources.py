"""使用 URUI 原生作者解析结果审计 HTML/CSS 文件及本地资源闭包。"""
import argparse
import ctypes
import hashlib
import json
import posixpath
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit


EXCLUDED = {".git", "Library", "Temp", "obj", "bin", "node_modules", "artifacts", "ThirdParty", "Engine", ".superpowers"}
LEGACY_SUFFIXES = (".css.txt", ".rml", ".rml.txt", ".rcss", ".rcss.txt")


class NativeAuthoring:
    def __init__(self, library):
        self.library = ctypes.CDLL(str(Path(library).resolve()))
        if self.library.urui_bridge_abi_version() != 11 or self.library.urui_authoring_protocol_version() != 1:
            raise ValueError("作者审计需要 ABI11 / authoring1 插件")
        self.html = self.library.urui_validate_html
        self.html.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
        self.css = self.library.urui_validate_stylesheet
        self.css.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_int)]
        self.library.urui_create.argtypes = [ctypes.c_int, ctypes.c_int]
        if self.library.urui_create(1, 1) != 1:
            raise ValueError("无法初始化原生作者解析器")

    def close(self):
        self.library.urui_destroy()

    def parse(self, source, uri, kind):
        text = source.encode("utf-8")
        if b"\0" in text:
            raise ValueError("作者源不能包含 NUL")
        arguments = [text, uri.encode("utf-8"), int(kind == "fragment")] if kind != "css" else [uri.encode("utf-8"), text]
        function = self.html if kind != "css" else self.css
        required = ctypes.c_int()
        output = ctypes.create_string_buffer(4096)
        result = function(*arguments, output, len(output), ctypes.byref(required))
        if result == -3 and len(output) < required.value <= 64 * 1024 * 1024:
            output = ctypes.create_string_buffer(required.value)
            result = function(*arguments, output, len(output), ctypes.byref(required))
        if result < 0:
            raise ValueError("原生作者解析失败：" + str(result))
        return json.loads(output.value.decode("utf-8"))


def authored_files(root):
    # 仓库入口限定为实际分发/测试资产，不把上游源码示例当作游戏作者文件。
    scopes = [root]
    if (root / "Client/Assets/Resources/URUI").is_dir():
        scopes = [root / "Client/Assets/Resources/URUI"]
        for owner in ("URUI", "FUI"):
            packages = root / owner / "Packages"
            if packages.exists():
                for package in packages.iterdir():
                    scopes.extend(package / name for name in ("Runtime/Resources", "Tests/Runtime/Resources", "Tests~/HtmlCss/fixtures", "Samples~"))
    for scope in scopes:
        if not scope.exists():
            continue
        for path in sorted(scope.rglob("*")):
            if not path.is_file() or any(part in EXCLUDED for part in path.relative_to(root).parts):
                continue
            if path.suffix.lower() in {".html", ".css", ".txt", ".rml", ".rcss"}:
                yield path


def audit(root, native, inventory=None, mounts=None):
    root = root.resolve()
    mounted = {}
    if (root / "Client/Assets/Resources/URUI").is_dir():
        mounted.update({"/Assets": root / "Client/Assets", "/URUI": root / "Client/Assets/Resources/URUI"})
    defaults = root / "URUI/Packages/com.fujisheng.urui/Runtime/Resources/URUI"
    if defaults.is_dir():
        mounted["/urui"] = defaults
    effects = root / "URUI/Packages/com.fujisheng.urui.effect/Runtime/Resources/URUI.Effect"
    if effects.is_dir():
        mounted["/urui-effect"] = effects
    for prefix, directory in (mounts or {}).items():
        prefix = "/" + prefix.strip("/")
        target = (root / directory).resolve()
        if prefix == "/" or ".." in prefix.split("/") or not target.is_relative_to(root) or not target.is_dir():
            raise ValueError("资源挂载必须指向根目录内的有效子目录：" + prefix)
        mounted[prefix] = target
    roles = {row["path"]: row["kind"] for row in (inventory or {}).get("files", [])}
    records, diagnostics, edges, parsed = {}, [], {}, set()

    def error(code, message, source, line=0):
        diagnostics.append({"code": code, "message": message, "path": source, "line": line})

    def relative(path):
        return path.relative_to(root).as_posix()

    runtime_resources = {}
    for declaration in (inventory or {}).get("runtimeResources", []):
        owner = declaration.get("path", "") if isinstance(declaration, dict) else ""
        owner = owner if isinstance(owner, str) else ""
        try:
            if not isinstance(declaration, dict) or any(not isinstance(declaration.get(key), str) or not declaration[key] for key in ("path", "provider", "source", "providerSha256", "ownerSha256")):
                raise ValueError("运行时图片声明字段必须是非空字符串")
            owner_path = (root / owner).resolve()
            provider = (root / declaration["provider"]).resolve()
            source = declaration["source"]
            if not owner_path.is_relative_to(root) or not provider.is_relative_to(root) or provider.suffix != ".cs" or not source:
                raise ValueError("运行时图片必须有根目录内明确的作者文件和 C# 提供方")
            provider_bytes = provider.read_bytes()
            if (hashlib.sha256(provider_bytes).hexdigest() != declaration["providerSha256"] or
                    hashlib.sha256(owner_path.read_bytes()).hexdigest() != declaration["ownerSha256"] or
                    source not in provider_bytes.decode("utf-8", errors="strict")):
                raise ValueError("运行时图片声明与当前作者/提供方源文件不一致")
            key = (owner, source)
            if key in runtime_resources:
                raise ValueError("运行时图片声明重复")
            runtime_resources[key] = {"source": source, "provider": relative(provider), "providerSha256": declaration["providerSha256"]}
        except (KeyError, UnicodeError, ValueError, OSError) as exception:
            error("RUNTIME_PROVIDER", str(exception), owner)

    def public_url(source):
        path = root / source
        for prefix, directory in sorted(mounted.items(), key=lambda pair: len(str(pair[1])), reverse=True):
            if path.is_relative_to(directory):
                return prefix + "/" + path.relative_to(directory).as_posix()
        return None

    def register(path):
        name = relative(path)
        if name not in records:
            data = path.read_bytes()
            records[name] = {"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(), "kind": "resource", "dependencies": [], "runtimeDependencies": [], "closure": []}
        return name

    def dependency(source, reference, base_source=None, kind=None, owner=None, runtime_image=False):
        if not reference or reference.startswith("#"):
            return
        try:
            url = urlsplit(reference)
            if url.scheme or url.netloc:
                error("RESOURCE_EXTERNAL", "未声明本地文件身份的资源：" + reference, source)
                return
            decoded = unquote(url.path, encoding="utf-8", errors="strict")
            if "\0" in decoded or "\\" in decoded:
                raise ValueError("资源路径包含 NUL 或反斜杠")
            base_source = base_source or source
            if not decoded.startswith("/") and not (root / Path(base_source).parent / decoded).resolve().is_relative_to(root):
                error("RESOURCE_ESCAPE", "资源路径越出根目录：" + reference, source)
                return
            source_url = public_url(base_source)
            if not decoded.startswith("/") and source_url is None:
                candidate = root / Path(base_source).parent / decoded
            else:
                decoded = posixpath.normpath(decoded if decoded.startswith("/") else posixpath.dirname(source_url) + "/" + decoded)
                candidate = root / decoded.lstrip("/")
                for prefix in sorted(mounted, key=len, reverse=True):
                    if decoded.startswith(prefix + "/"):
                        candidate = mounted[prefix] / decoded[len(prefix) + 1:]
                        break
            resolved = candidate.resolve()
            if not resolved.is_relative_to(root):
                error("RESOURCE_ESCAPE", "资源路径越出根目录：" + reference, source)
                return
            provided = runtime_resources.get((source, reference)) if runtime_image else None
            if provided:
                dependencies = records[source]["runtimeDependencies"]
                if provided not in dependencies:
                    dependencies.append(provided)
                return
            if not resolved.is_file():
                error("RESOURCE_MISSING", "资源文件不存在：" + reference, source)
                return
            target = register(resolved)
            edges.setdefault(source, set()).add(target)
            if kind:
                if target in roles and roles[target] != kind:
                    error("RESOURCE_ROLE", "资源声明角色冲突：" + reference, source)
                    return
                roles[target] = kind
            if resolved.suffix.lower() in {".html", ".css"}:
                inspect(resolved, owner)
        except (UnicodeError, ValueError, OSError) as exception:
            error("RESOURCE_URL", str(exception) + "：" + reference, source)

    def css(source, text, base_source=None):
        packet = native.parse(text, source, "css")
        for diagnostic in packet["diagnostics"]:
            error(diagnostic["code"], diagnostic["message"], source, diagnostic.get("line", 0))

        def rules(values):
            for rule in values:
                for declaration in rule["declarations"]:
                    if "urls" not in declaration:
                        raise ValueError("原生作者包缺少 URL 解析结果，不能完成资源审计")
                    for url in declaration["urls"]:
                        dependency(source, url, base_source, runtime_image=rule["atRule"] != "font-face")
                rules(rule["children"])
        rules(packet["rules"])

    def inspect(path, owner=None):
        name = relative(path)
        key = (name, owner if roles.get(name) == "fragment" else None)
        if key in parsed:
            return
        parsed.add(key)
        row = records[register(path)]
        try:
            source = path.read_bytes().decode("utf-8", errors="strict")
            if "\0" in source:
                raise UnicodeError("作者源不能包含 NUL")
        except UnicodeError as exception:
            error("SOURCE_ENCODING", str(exception), name)
            return
        expected_suffix = {"css": ".css", "document": ".html", "fragment": ".html"}.get(roles.get(name))
        if expected_suffix and path.suffix.lower() != expected_suffix:
            row["kind"] = "legacy"
            error("LEGACY_FORMAT", "声明的作者角色必须使用 " + expected_suffix + " 文件", name)
            return
        if path.name.lower().endswith(LEGACY_SUFFIXES) or (path.suffix.lower() == ".txt" and re.search(r"<[A-Za-z][\w-]*\b", source)):
            row["kind"] = "legacy"
            error("LEGACY_FORMAT", "作者文件必须迁为 .html / .css", name)
            return
        if path.suffix.lower() not in {".html", ".css"}:
            return
        kind = "css" if path.suffix.lower() == ".css" else roles.get(name, "document")
        if kind not in {"css", "document", "fragment"}:
            error("INVENTORY_ROLE", "无效作者角色：" + str(kind), name)
            return
        row["kind"] = kind
        try:
            if kind == "css":
                css(name, source)
            else:
                packet = native.parse(source, name, kind)
                document_source = owner if kind == "fragment" and owner else name
                for diagnostic in packet["diagnostics"]:
                    error(diagnostic["code"], diagnostic["message"], name, diagnostic.get("line", 0))
                for node in packet["nodes"]:
                    if "inlineStyle" not in node or "styleText" not in node or "attributes" not in node:
                        raise ValueError("原生作者包缺少内联样式/属性结果，不能完成资源审计")
                    if node["tag"] == "link" and "stylesheet" in node["rel"].lower().split():
                        if not node["href"] or not urlsplit(node["href"]).path.lower().endswith(".css"):
                            error("STYLESHEET_URL", "stylesheet 必须指向真实 .css 文件", name, node["line"])
                        else:
                            dependency(name, node["href"], document_source)
                    if node["tag"] == "link" and "urui-fragment" in node["rel"].lower().split():
                        if not node["href"] or not urlsplit(node["href"]).path.lower().endswith(".html"):
                            error("FRAGMENT_URL", "urui-fragment 必须指向真实 .html 文件", name, node["line"])
                        else:
                            dependency(name, node["href"], document_source, "fragment", document_source)
                    if node["tag"] in {"img", "input", "source", "video", "audio"}:
                        dependency(name, node["source"], document_source, runtime_image=node["tag"] in {"img", "input", "source"})
                    if node["inlineStyle"]:
                        css(name, "urui-audit-inline{" + node["inlineStyle"] + "}", document_source)
                    if node["styleText"]:
                        css(name, node["styleText"], document_source)
        except (KeyError, ValueError, OSError) as exception:
            error("NATIVE_AUTHORING", str(exception), name)

    paths = list(authored_files(root))
    documents = []
    # 先检查完整文档，让作者链接确定片段角色和资源域，避免文件名排序影响结果。
    for path in paths:
        if not path.resolve().is_relative_to(root) or path.suffix.lower() != ".html" or roles.get(relative(path)) == "fragment":
            continue
        try:
            source = path.read_bytes().decode("utf-8", errors="strict")
            packet = native.parse(source, relative(path), "document")
            if not packet["diagnostics"]:
                documents.append(path)
        except (UnicodeError, ValueError, OSError):
            pass  # 正式检查负责报告原始编码或解析错误。
    for path in documents + [path for path in paths if path not in documents]:
        if not path.resolve().is_relative_to(root):
            error("RESOURCE_ESCAPE", "作者文件链接越出根目录", relative(path))
        elif roles.get(relative(path)) == "fragment" and any(name == relative(path) for name, _ in parsed):
            continue
        else:
            inspect(path)
    for name in roles:
        if name not in records:
            error("INVENTORY_MISSING", "清单文件没有出现在实际作者资产中", name)
    for name, row in records.items():
        row["dependencies"] = sorted(edges.get(name, set()))
        closure, pending = set(), list(row["dependencies"])
        while pending:
            target = pending.pop()
            if target == name or target in closure:
                continue
            closure.add(target)
            pending.extend(edges.get(target, set()))
        row["closure"] = sorted(closure)
    if not parsed:
        error("AUTHORING_EMPTY", "没有找到作者文件，不能视为审计通过", "")
    return {"schema": 1, "status": "failed" if diagnostics else "passed", "root": str(root),
            "mounts": {prefix: relative(directory) for prefix, directory in mounted.items()},
            "native": {"abi": 9, "authoring": 1}, "files": sorted(records.values(), key=lambda row: row["path"]), "diagnostics": diagnostics}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--native-library", required=True, type=Path)
    parser.add_argument("--inventory", type=Path)
    parser.add_argument("--mount", action="append", default=[], metavar="URL_PREFIX=ROOT_SUBDIRECTORY")
    args = parser.parse_args()
    native = None
    try:
        native = NativeAuthoring(args.native_library)
        inventory_path = args.inventory
        if inventory_path is None:
            candidate = args.root / "URUI/Tools/HtmlCss/resource_inventory.json"
            if candidate.is_file():
                inventory_path = candidate
        inventory = json.loads(inventory_path.read_text()) if inventory_path else None
        mounts = dict(value.split("=", 1) for value in args.mount)
        report = audit(args.root, native, inventory, mounts)
    except (AttributeError, ValueError, OSError) as exception:
        report = {"schema": 1, "status": "unverified", "files": [], "diagnostics": [{"code": "AUDIT_UNAVAILABLE", "message": str(exception)}]}
    finally:
        if native:
            native.close()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"status": report["status"], "files": len(report["files"]), "diagnostics": len(report["diagnostics"])}))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
