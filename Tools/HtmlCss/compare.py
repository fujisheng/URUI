"""比较浏览器和 URUI 的同源快照；缺证据不能判为通过。"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re

from PIL import Image, ImageChops


GEOMETRY_TOLERANCE = .5
BASELINE_TOLERANCE = 1
COLOR_TOLERANCE = 2
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def rect(value):
    return (isinstance(value, list) and len(value) == 4 and all(number(v) for v in value)
            and value[2] >= 0 and value[3] >= 0)


def hashes(value):
    return (isinstance(value, dict) and bool(value)
            and all(isinstance(k, str) and k and isinstance(v, str) and SHA256.fullmatch(v) for k, v in value.items()))


def compare_snapshots(browser_path, unity_path):
    failures, missing = [], []
    metrics = {"maxGeometryDelta": 0, "maxBaselineDelta": 0, "maxColorDelta": 0, "pixelCount": 0}
    report = {"status": "unverified", "failures": failures, "unverified": missing, "metrics": metrics,
              "tolerances": {"geometryCssPx": GEOMETRY_TOLERANCE, "baselineCssPx": BASELINE_TOLERANCE,
                             "colorChannelSteps": COLOR_TOLERANCE}}
    snapshots, images = [], []
    for runtime, path in (("browser", Path(browser_path)), ("unity", Path(unity_path))):
        try:
            snapshot = json.loads(path.read_text())
        except (OSError, ValueError) as error:
            missing.append(f"{runtime}: snapshot unavailable: {error}"); return report
        if not isinstance(snapshot, dict) or snapshot.get("schemaVersion") != 1 or snapshot.get("runtime") != runtime:
            missing.append(f"{runtime}: snapshot schema/runtime differs"); return report
        snapshots.append(snapshot)
        if not hashes(snapshot.get("inputs")): missing.append(f"{runtime}: source hashes missing/invalid")
        if not hashes(snapshot.get("fonts")): missing.append(f"{runtime}: font hashes missing/invalid")
        if snapshot.get("fontsLoaded") is not True: missing.append(f"{runtime}: fonts are not ready")
        if hashes(snapshot.get("fonts")) and hashes(snapshot.get("inputs")):
            if any(snapshot["inputs"].get(k) != v for k, v in snapshot["fonts"].items()):
                missing.append(f"{runtime}: fonts differ from source inventory")
        viewport = snapshot.get("viewport")
        if (not isinstance(viewport, dict) or any(not number(viewport.get(k)) or viewport[k] <= 0 for k in ("width", "height", "dpr"))
                or viewport.get("dpr") != 1):
            missing.append(f"{runtime}: invalid viewport/DPR"); return report
        if not number(snapshot.get("time")): missing.append(f"{runtime}: invalid clock")
        image = None
        try:
            name = snapshot.get("screenshot")
            if not isinstance(name, str) or not name: raise ValueError("path missing")
            file = path.parent / name
            if hashlib.sha256(file.read_bytes()).hexdigest() != snapshot.get("screenshotSha256"):
                raise ValueError("SHA-256 differs")
            with Image.open(file) as source:
                image = source.convert("RGBA")
            if image.size != (viewport["width"], viewport["height"]): raise ValueError("dimensions differ from viewport")
        except (OSError, ValueError) as error:
            missing.append(f"{runtime}: screenshot unavailable/invalid: {error}")
        images.append(image)
    browser, unity = snapshots
    for field in ("inputs", "fonts", "viewport", "time"):
        if browser.get(field) != unity.get(field): missing.append(f"{field}: capture inputs differ")
    report["inputs"] = browser.get("inputs")

    def compare_rect(label, left, right):
        if not rect(left) or not rect(right): missing.append(label + ": invalid rectangle"); return
        delta = max(abs(a - b) for a, b in zip(left, right))
        metrics["maxGeometryDelta"] = max(metrics["maxGeometryDelta"], delta)
        if delta > GEOMETRY_TOLERANCE: failures.append(f"{label}: geometry delta {delta:g} CSS px")

    left_nodes, right_nodes = browser.get("nodes"), unity.get("nodes")
    if not isinstance(left_nodes, dict) or not isinstance(right_nodes, dict) or not left_nodes or not right_nodes:
        missing.append("nodes: critical nodes missing")
    else:
        if left_nodes.keys() != right_nodes.keys(): missing.append("nodes: critical node set differs")
        for name in sorted(left_nodes.keys() & right_nodes.keys()):
            left, right = left_nodes[name], right_nodes[name]
            if not isinstance(left, dict) or not isinstance(right, dict): missing.append(name + ": invalid node"); continue
            compare_rect(name, left.get("rect"), right.get("rect"))
            for field in ("tag", "parent", "text", "styles"):
                if field not in left or field not in right:
                    missing.append(f"{name}: {field} evidence missing")
                elif field == "styles" and (not isinstance(left[field], dict) or not isinstance(right[field], dict)
                                            or not left[field] or left[field].keys() != right[field].keys()):
                    missing.append(f"{name}: computed style evidence differs")
                elif left[field] != right[field]: failures.append(f"{name}: {field} differs")
            left_lines, right_lines = left.get("lines"), right.get("lines")
            if not isinstance(left_lines, list) or not isinstance(right_lines, list):
                missing.append(name + ": text line evidence missing"); continue
            if not left_lines and not right_lines and isinstance(left.get("text"), str) and left["text"].strip():
                styles = left.get("styles", {})
                if isinstance(styles, dict) and styles.get("display") != "none" and styles.get("visibility") != "hidden":
                    missing.append(name + ": visible text has no captured lines")
            if len(left_lines) != len(right_lines): failures.append(name + ": text line count differs")
            for index, (a, b) in enumerate(zip(left_lines, right_lines)):
                label = f"{name}/line{index}"
                if not isinstance(a, dict) or not isinstance(b, dict): missing.append(label + ": invalid line"); continue
                compare_rect(label, a.get("rect"), b.get("rect"))
                if not number(a.get("baseline")) or not number(b.get("baseline")):
                    missing.append(label + ": baseline missing/invalid"); continue
                delta = abs(a["baseline"] - b["baseline"])
                metrics["maxBaselineDelta"] = max(metrics["maxBaselineDelta"], delta)
                if delta > BASELINE_TOLERANCE: failures.append(f"{label}: baseline delta {delta:g} CSS px")
    left_regions, right_regions = browser.get("flatRegions"), unity.get("flatRegions")
    if (not isinstance(left_regions, dict) or not isinstance(right_regions, dict) or not left_regions
            or left_regions != right_regions):
        missing.append("flatRegions: flat-color probe evidence missing/different")
    elif all(image is not None for image in images) and browser["viewport"] == unity["viewport"]:
        width, height = images[0].size
        for name, region in left_regions.items():
            if (not rect(region) or any(v != int(v) for v in region) or min(region[:2]) < 0
                    or region[2] <= 0 or region[3] <= 0 or region[0] + region[2] > width or region[1] + region[3] > height):
                missing.append(f"{name}: invalid/outside flat-color region"); continue
            x, y, w, h = map(int, region)
            box = (x, y, x + w, y + h)
            difference = ImageChops.difference(images[0].crop(box), images[1].crop(box))
            delta = max(upper for lower, upper in difference.getextrema())
            metrics["maxColorDelta"] = max(metrics["maxColorDelta"], delta)
            metrics["pixelCount"] += w * h
            if delta > COLOR_TOLERANCE: failures.append(f"{name}: color channel delta {delta}/255")
    report["status"] = "failed" if failures else "unverified" if missing else "passed"
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--browser", type=Path, required=True)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = compare_snapshots(args.browser, args.unity)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "comparison.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"status": report["status"], "failures": len(report["failures"]), "unverified": len(report["unverified"])}))
    return {"passed": 0, "failed": 1, "unverified": 2}[report["status"]]


if __name__ == "__main__":
    raise SystemExit(main())
