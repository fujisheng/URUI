"""检查活跃自有代码的 URUI 命名，版权和来源保留可追踪豁免。"""
import argparse
import json
from pathlib import Path
import re

LEGACY = re.compile(r"RmlUi|RMLUI|RMLUICORE|\bRml\b|RML|RCSS|rmlui|\brml\b|\brcss\b|InnerRml|inner_rml|"
                    r"\b(?:LoadMarkup|SetInnerMarkup|GetInnerMarkup|FontPath|DefaultFontPath)\b|\bdocument\.Root\b")


def audit(root, final):
    package = root / "URUI/Packages/com.fujisheng.urui"
    violations, allowed = [], []
    scopes = [package / "Native~/Engine", package / "Native~", package / "Runtime",
              package / "Editor", package / "Documentation~", package / "Samples~",
              package / "README.md", package / "CHANGELOG.md"]
    seen = set()
    for scope in scopes:
        if not scope.exists():
            continue
        for file in ([scope] if scope.is_file() else sorted(scope.rglob("*"))):
            if (not file.is_file() or file in seen or file.suffix in {
                    ".meta", ".dylib", ".dll", ".so", ".a", ".png", ".jpg", ".ttf", ".otf"}):
                continue
            seen.add(file)
            if "ThirdParty" in file.parts:
                continue
            relative = str(file.relative_to(root))
            provenance = file.name in {"UPSTREAM.md", "UPSTREAM_CHANGELOG.md", "LICENSE.txt", "LICENSE.md", "LICENSE", "COPYING"}
            try:
                text = file.read_text()
            except (UnicodeDecodeError, OSError):
                continue
            for number, line in enumerate(text.splitlines(), 1):
                if not LEGACY.search(line):
                    continue
                item = {"path": relative, "line": number, "text": line[:240]}
                if provenance or re.search(r"Copyright|copyright|\(c\)", line):
                    item["reason"] = "original-provenance"
                    allowed.append(item)
                elif not LEGACY.search(re.sub(
                        r"https?://(?:github\.com/mikke89/RmlUi|mikke89\.github\.io/RmlUiDoc)[^\s\])\"<>]*",
                        "", line)):
                    item["reason"] = "upstream-reference"
                    allowed.append(item)
                elif "URUI_LEGACY_REJECTION_FIXTURE" in line:
                    item["reason"] = "negative-fixture"
                    allowed.append(item)
                elif file.name == "DEPENDENCIES.json" and re.fullmatch(
                        r'\s*"upstream_name":\s*"RmlUi",?\s*', line):
                    item["reason"] = "upstream-metadata"
                    allowed.append(item)
                elif not final and "URUI_LEGACY_PENDING" in line:
                    item["reason"] = "legacy-pending"
                    allowed.append(item)
                else:
                    violations.append(item)
    return {"violations": violations, "allowed": allowed, "final": final}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--final", action="store_true")
    args = parser.parse_args()
    result = audit(args.root.resolve(), args.final)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"violations": len(result["violations"]), "allowed": len(result["allowed"])}))
    return 1 if result["violations"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
