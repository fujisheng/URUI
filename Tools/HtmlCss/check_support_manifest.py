"""将真实引擎注册表与声明的支持清单进行交叉核对。"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    args = parser.parse_args()
    if not args.manifest.is_file():
        print("FAIL: support manifest has not been produced")
        return 1
    manifest = json.loads(args.manifest.read_text())
    with tempfile.TemporaryDirectory() as directory:
        snapshot = Path(directory) / "native.json"
        subprocess.run([str(args.native), str(snapshot)], check=True)
        actual = json.loads(snapshot.read_text())
    entries = manifest["entries"]
    declared = {(e["category"], e["name"]) for e in entries}
    expected = {("property", p["name"]) for p in actual["properties"]}
    expected.update(("shorthand", name) for name in actual["shorthands"])
    missing = sorted(expected - declared)
    if missing:
        print("FAIL: runtime capabilities missing from manifest:", missing)
        return 1
    required = {"name", "category", "status", "values", "initial",
                "inherited", "percentageBase", "source", "test"}
    for entry in entries:
        if not required <= entry.keys() or entry["status"] not in {
                "standard", "fix", "extension", "remove"}:
            print("FAIL: unclassified capability:", entry)
            return 1
    print(f"PASS: {len(expected)} native registrations covered; "
          f"{len(entries)} total capability entries")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
