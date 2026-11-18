"""从原生作者解析闭包生成浏览器和 Unity 共用的输入清单。"""
import argparse
import hashlib
import json
from pathlib import Path

from audit_resources import NativeAuthoring, audit


def prepare(root, library, output):
    root = Path(root).resolve()
    fixtures = Path("URUI/Packages/com.fujisheng.urui/Tests~/HtmlCss/fixtures")
    native = NativeAuthoring(library)
    try:
        inventory = json.loads((root / "URUI/Tools/HtmlCss/resource_inventory.json").read_text())
        report = audit(root, native, inventory, {"/fixtures": fixtures.as_posix()})
    finally:
        native.close()
    output = Path(output); output.mkdir(parents=True, exist_ok=True)
    (output / "resource-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    if report["status"] != "passed": raise ValueError("Author dependency audit failed: " + str(report["diagnostics"][:3]))
    records = {row["path"]: row for row in report["files"]}

    def resource(name):
        uri = None
        for prefix, directory in sorted(report["mounts"].items(), key=lambda pair: len(pair[1]), reverse=True):
            if name.startswith(directory + "/"):
                uri = prefix + name[len(directory):]; break
        if uri is None: raise ValueError("Comparison resource has no explicit URL: " + name)
        row = records[name]
        return {"uri": uri, "path": name, "sha256": row["sha256"], "font": name.lower().endswith((".ttf.bytes", ".otf.bytes", ".ttf", ".otf", ".woff", ".woff2"))}

    result = {"root": str(root), "nativeSha256": hashlib.sha256(Path(library).read_bytes()).hexdigest(),
              "cases": [], "viewports": [{"width": 1280, "height": 720},
                {"width": 1920, "height": 1080}, {"width": 1170, "height": 2532}]}
    for case in json.loads((root / fixtures / "cases.json").read_text())["cases"]:
        name = (fixtures / (case["name"] + ".html")).as_posix()
        entry = dict(case, uri="/fixtures/" + case["name"] + ".html", path=name)
        entry["resources"] = [resource(target) for target in sorted({name, *records[name]["closure"]})]
        result["cases"].append(entry)
    preview = root / "URUI/Packages/com.fujisheng.urui/Web~/preview.js"
    result["previewPath"] = str(preview)
    result["previewSha256"] = hashlib.sha256(preview.read_bytes()).hexdigest()
    (output / "inputs.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"cases": len(result["cases"]), "viewports": len(result["viewports"]), "auditFiles": len(records)}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--native-library", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.root, args.native_library, args.output)


if __name__ == "__main__":
    main()
