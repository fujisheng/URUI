"""只读收集当前能力、作者资源和来源摘要，不把未验证项标为通过。"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re


PRIVATE = {
    "--urui-image-color", "--urui-image-effect", "clip", "drag", "tab-index", "focus",
    "--urui-scrollbar-margin", "decorator", "--urui-font-effect", "--urui-fill-image",
    "--urui-caret-width", "--urui-caret-blink-rate",
}
FIX = {
    "opacity", "background", "font", "border", "border-top", "border-right",
    "border-bottom", "border-left", "min-width", "min-height", "cursor",
    "mask-image", "animation", "transition", "transform-origin",
    "perspective-origin", "color", "text-decoration", "overflow-x", "overflow-y",
}
FONT_SUFFIXES = (".ttf", ".otf", ".woff", ".woff2", ".ttf.bytes", ".otf.bytes", ".woff.bytes", ".woff2.bytes")


def treatment(name):
    if (name in PRIVATE or name.startswith(("image-slice-", "image-fill-", "nav-", "-urui-", "--urui-"))
            or name in {"--urui-transform-origin-x", "--urui-transform-origin-y", "--urui-transform-origin-z",
                        "--urui-perspective-origin-x", "--urui-perspective-origin-y"}):
        return "extension"
    return "fix" if name in FIX else "standard"


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n")


def collect(root, native_snapshot):
    package = root / "URUI/Packages/com.fujisheng.urui"
    native = package / "Native~"
    engine = native / "Engine"
    if not engine.exists():
        engine = native / "Engine"
    specification = engine / "Source/Core/StyleSheetSpecification.cpp"
    declaration = specification.read_text() if specification.exists() else ""
    entries = []
    texts = {}
    files = []
    scopes = [
        native, package / "Runtime", package / "Samples~",
        root / "FUI/Packages/com.fujisheng.fui.urui/Runtime",
        root / "URUI/Packages/com.fujisheng.urui.effect/Runtime",
        root / "Client/Assets/Scripts/UI", root / "Client/Assets/Resources/URUI",
        root / "Client/Assets/Editor", root / "Client/Assets/Scripts/Editor",
    ]
    for scope in scopes:
        if not scope.exists():
            continue
        for file in sorted(scope.rglob("*")):
            is_font = file.name.lower().endswith(FONT_SUFFIXES)
            if not file.is_file() or (not is_font and file.suffix not in {
                    ".cpp", ".h", ".inl", ".cs", ".txt", ".html", ".css", ".json", ".asmdef"}):
                continue
            if "ThirdParty" in file.parts and engine not in file.parents:
                continue
            relative = str(file.relative_to(root))
            data = file.read_bytes()
            if not is_font:
                texts[relative] = data.decode("utf-8", errors="replace")
            files.append({"path": relative, "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})

    def source_for(name):
        quoted = re.compile(r'["\']' + re.escape(name) + r'["\']')
        ordered = sorted(texts, key=lambda p: (not p.startswith(str(native.relative_to(root))), p))
        for relative in ordered:
            match = quoted.search(texts[relative])
            if match:
                return {"path": relative, "line": texts[relative][:match.start()].count("\n") + 1}
        return {"path": str(specification.relative_to(root)), "line": 1}

    def entry(name, category, status=None, initial=None, inherited=None, values=None, source=None):
        status = status or treatment(name)
        task = {"property": "T07", "shorthand": "T06", "unit": "T06",
                "selector": "T06", "at-rule": "T07", "control": "T11", "api": "T08"}[category]
        return {"name": name, "category": category, "status": status,
                "values": values or [], "initial": initial, "inherited": inherited,
                "percentageBase": None, "source": source or source_for(name),
                "test": {"owner": task, "state": "pending"}}

    def property_source(name):
        # 先取声明而不是同名属性的某个消费点；循环注册保留所在循环来源。
        for relative, text in texts.items():
            if not relative.startswith(str(native.relative_to(root))):
                continue
            direct = re.search(
                r'RegisterProperty\((?:PropertyId::\w+,\s*)?"' +
                re.escape(name) + r'"[^;]*;', text, re.S)
            if direct:
                block = direct[0]
                prefix = text[max(0, direct.start() - 50):direct.start()]
                binding = re.search(r'auto\s*&\s*(\w+)\s*=\s*(?:[\w:]+::)?$', prefix)
                if binding:
                    additional = re.search(
                        re.escape(binding[1]) + r'\.AddParser\([^;]*;',
                        text[direct.end():], re.S)
                    if additional:
                        block += additional[0]
                return relative, direct.start(), block
            for occurrence in re.finditer('"' + re.escape(name) + '"', text):
                position = occurrence.start()
                loop = re.search(r'RegisterProperty\(name,[^;]*;', text[position:position + 900], re.S)
                if loop:
                    return relative, position, loop[0]
        return None

    runtime = json.loads(native_snapshot.read_text()) if native_snapshot else None
    native_properties = runtime["properties"] if runtime else [
        {"name": name, "initial": default, "inherited": inherited == "true"}
        for name, default, inherited in re.findall(
            r'RegisterProperty\(PropertyId::\w+,\s*"([^"]+)",\s*"([^"]*)",\s*(true|false)',
            declaration)]
    for prop in native_properties:
        item = entry(prop["name"], "property", initial=prop.get("initial"),
                     inherited=prop.get("inherited"))
        declaration_source = property_source(prop["name"])
        if declaration_source:
            relative, offset, block = declaration_source
            item["source"] = {"path": relative, "line": texts[relative][:offset].count("\n") + 1}
            item["values"] = re.findall(r'\.AddParser\("([^"]+)"(?:,\s*"([^"]*)")?', block)
        item["percentageBase"] = prop.get("relativeTarget")
        entries.append(item)
    # 语义变量由托管控件/特效消费，可能保留为普通 CSS token，并非全局原生注册项。
    # 只收录库代码中的保留前缀；应用作者的任意 --name 不会变成引擎能力声明。
    native_names = {prop["name"] for prop in native_properties}
    for relative, text in texts.items():
        if "/Runtime/" not in relative or not relative.endswith(".cs"):
            continue
        for match in re.finditer(r'''["'](--urui-[A-Za-z0-9_-]+)["']''', text):
            name = match[1]
            names = [name]
            if name == "--urui-mask-feather-":
                names = [name + edge for edge in ("left", "top", "right", "bottom")]
            elif name.endswith("-"):
                continue
            for variable in names:
                if variable in native_names:
                    continue
                item = entry(variable, "property", "extension", source={
                    "path": relative, "line": text[:match.start()].count("\n") + 1})
                item["registration"] = "consumer-defined"
                item["test"]["owner"] = "T11"
                entries.append(item)
    shorthands = runtime["shorthands"] if runtime else re.findall(
        r'RegisterShorthand\(ShorthandId::\w+,\s*"([^"]+)"', declaration)
    entries.extend(entry(name, "shorthand") for name in shorthands)
    number_parser = engine / "Source/Core/PropertyParserNumber.cpp"
    if number_parser.exists():
        units = re.findall(r'\{"([^"]*)",\s*Unit::\w+', number_parser.read_text())
        entries.extend(entry(name or "<number>", "unit",
                             "extension" if name in {"dp", "x"} else "standard") for name in units)
    selector_factory = engine / "Source/Core/StyleSheetFactory.cpp"
    if selector_factory.exists():
        selectors = re.findall(r'\{"([^"]+)",\s*StructuralSelectorType::', selector_factory.read_text())
        entries.extend(entry(name, "selector", "fix") for name in selectors)
    for name in ["type", "*", "#id", ".class", "[attribute]", "descendant", ">", "+", "~"]:
        entries.append(entry(name, "selector", "fix"))
    pseudo_names = set()
    for relative, text in texts.items():
        if relative.startswith(str(native.relative_to(root))):
            pseudo_names.update(re.findall(r'SetPseudoClass\("([^"]+)"', text))
    entries.extend(entry(":" + name, "selector", "fix") for name in sorted(pseudo_names))
    rules = set()
    for name in ("CssParser.cpp", "StyleSheetParser.cpp"):
        stylesheet_parser = engine / "Source/Core" / name
        if not stylesheet_parser.exists():
            continue
        text = stylesheet_parser.read_text()
        rules.update(re.findall(r'(?:at_rule_identifier|rule\.atRule)\s*==\s*"([^"]+)"', text))
        if "@property" in text:
            rules.add("property")
    # 删除的作者语言仍保留去向记录，不因实际分派移除而从迁移清单消失。
    rules.add("spritesheet")
    for name in sorted(rules):
        item = entry("@" + name, "at-rule", "remove" if name == "spritesheet" else "extension" if name == "decorator" else "fix")
        if name == "spritesheet":
            item["values"] = ["ImageAsset.Sprites", "data-urui-sprite"]
            item["test"]["owner"] = "T11"
        entries.append(item)
    factories = package / "Runtime/Controls/ControlFactories.cs"
    if factories.exists():
        for tag, kind in re.findall(
                r'RegisterFactory\("([^"]+)",\s*context\s*=>\s*new\s+(\w+)', factories.read_text()):
            entries.append(entry(kind, "control", "extension" if "-" in tag or tag == "foldout" else "fix",
                                 values=[tag], source={"path": str(factories.relative_to(root))}))
    for relative, text in texts.items():
        if not relative.startswith(str((package / "Runtime").relative_to(root))) or not relative.endswith(".cs"):
            continue
        namespace = re.search(r"namespace\s+([\w.]+)", text)
        for name in re.findall(r"public\s+(?:(?:sealed|abstract|readonly|partial|static)\s+)*(?:class|struct|enum|interface)\s+(\w+)", text):
            full_name = (namespace[1] + "." if namespace else "") + name
            entries.append(entry(full_name, "api", "fix", source={"path": relative}))
    unique = {(e["category"], e["name"]): e for e in entries}
    assets = {"css": [], "html": [], "legacy_markup": [],
              "fonts": [file["path"] for file in files if file["path"].lower().endswith(FONT_SUFFIXES)]}
    css_usage = Counter()
    for relative, text in texts.items():
        name = Path(relative).name
        if name.endswith((".css.txt", ".css")):
            assets["css"].append(relative)
            css_usage.update(re.findall(r"(?:[;{]\s*)([-\w]+)\s*:", re.sub(r"/\*.*?\*/", "", text, flags=re.S)))
        elif name.endswith(".html"):
            assets["html"].append(relative)
        elif (name.endswith(".txt") and name != "CMakeLists.txt" and not name.upper().startswith("LICENSE")
              and re.search(r"<[A-Za-z][\w-]*\b", text)):
            assets["legacy_markup"].append(relative)
    return (
        {"schema": 1, "meaning": "planned treatment; not conformance or passing evidence",
         "runtimeCoverage": "verified" if runtime else "unverified",
         "entries": sorted(unique.values(), key=lambda e: (e["category"], e["name"]))},
        {"runtimeCoverage": "verified" if runtime else "unverified",
         "abi": runtime.get("abi") if runtime else None, "assets": assets,
         "cssUsage": dict(css_usage), "files": files},
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--native-snapshot", type=Path)
    args = parser.parse_args()
    snapshot = args.native_snapshot
    if snapshot is None:
        existing = args.output / "capabilities.json"
        if existing.is_file():
            snapshot = existing
    manifest, inventory = collect(args.root.resolve(), snapshot)
    write_json(args.output / "support-manifest.json", manifest)
    write_json(args.output / "inventory.json", inventory)
    write_json(args.output / "impact-map.json", {
        "files": inventory["files"], "assets": inventory["assets"],
        "tasks": dict(Counter(e["test"]["owner"] for e in manifest["entries"])),
    })
    print(json.dumps({"entries": len(manifest["entries"]),
                      "runtimeCoverage": manifest["runtimeCoverage"],
                      "assets": {k: len(v) for k, v in inventory["assets"].items()}}))
    return 0 if snapshot else 3


if __name__ == "__main__":
    raise SystemExit(main())
