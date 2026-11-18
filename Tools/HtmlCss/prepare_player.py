#!/usr/bin/env python3
"""Bundle unchanged author inputs and browser geometry/color evidence for a Player."""
import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare_manifest(inputs, browser_root, native_sha):
    root = Path(inputs["root"])
    blobs = {}
    cases = []
    for fixture in inputs["cases"]:
        resources = []
        for resource in fixture["resources"]:
            data = (root / resource["path"]).read_bytes()
            if sha(data) != resource["sha256"]:
                raise ValueError("source changed: " + resource["path"])
            key = "URUIHtmlCssPlayer/files/" + sha(data)
            blobs[key] = data
            resources.append({"uri": resource["uri"], "key": key, "sha256": sha(data)})
        html = next((item["key"] for item in resources if item["uri"] == fixture["uri"]), None)
        if html is None:
            raise ValueError("HTML is absent from the declared source closure")
        expected_inputs = {item["uri"]: item["sha256"] for item in resources}
        expected_fonts = {item["uri"]: item["sha256"] for item in fixture["resources"] if item.get("font")}
        regions = {item["name"]: item["rect"] for item in fixture["regions"]}
        for viewport in inputs["viewports"]:
            width, height = viewport["width"], viewport["height"]
            name = f'{fixture["name"]}-{width}x{height}'
            directory = browser_root / name
            snapshot = json.loads((directory / "browser.json").read_text())
            if snapshot.get("inputs") != expected_inputs:
                raise ValueError("browser inputs differ: " + name)
            if not snapshot.get("fontsLoaded") or snapshot.get("fonts") != expected_fonts:
                raise ValueError("browser font evidence differs: " + name)
            if snapshot.get("viewport") != {"width": width, "height": height, "dpr": 1}:
                raise ValueError("browser viewport differs: " + name)
            if snapshot.get("schemaVersion") != 1 or snapshot.get("runtime") != "browser" or snapshot.get("time") != 0:
                raise ValueError("browser capture contract differs: " + name)
            image_path = directory / snapshot["screenshot"]
            if sha(image_path.read_bytes()) != snapshot.get("screenshotSha256"):
                raise ValueError("browser screenshot changed: " + name)
            if snapshot.get("flatRegions") != regions:
                raise ValueError("browser probe regions differ: " + name)
            nodes = []
            for key, node in snapshot["nodes"].items():
                rect = node["rect"]
                if len(rect) != 4 or not all(math.isfinite(value) for value in rect):
                    raise ValueError("invalid browser node geometry: " + key)
                nodes.append({"key": key, "tag": node["tag"], "parent": node["parent"] or "", "rect": rect,
                              "fontSize": node["styles"]["font-size"], "opacity": node["styles"]["opacity"]})
            if not nodes:
                raise ValueError("browser key nodes are missing: " + name)
            probes = []
            with Image.open(image_path) as image:
                if image.size != (width, height):
                    raise ValueError("browser screenshot viewport differs: " + name)
                image = image.convert("RGBA")
                for key, rect in regions.items():
                    x, y, w, h = rect
                    if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > width or y + h > height:
                        raise ValueError("browser probe lies outside screenshot: " + key)
                    rgba = [channel for pixel in image.crop((x, y, x + w, y + h)).getdata() for channel in pixel]
                    probes.append({"name": key, "rect": rect, "rgba": rgba})
            cases.append({"name": name, "uri": fixture["uri"], "html": html, "width": width, "height": height,
                          "resources": resources, "nodes": nodes, "regions": probes,
                          "preview": fixture.get("preview", False)})
    if not cases:
        raise ValueError("no Player cases")
    return {"schema": 1, "nativeSha256": native_sha, "cases": cases}, blobs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", required=True, type=Path)
    parser.add_argument("--browser-root", required=True, type=Path)
    parser.add_argument("--native-library", required=True, type=Path)
    parser.add_argument("--project", required=True, type=Path, help="Stopped isolated Unity project")
    args = parser.parse_args()
    manifest, blobs = prepare_manifest(json.loads(args.inputs.read_text()), args.browser_root, sha(args.native_library.read_bytes()))
    resources = args.project / "Assets/Resources"
    for key, data in blobs.items():
        file = resources / (key + ".bytes")
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_bytes(data)
    file = resources / "URUIHtmlCssPlayer/manifest.json"
    file.write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    print(json.dumps({"cases": len(manifest["cases"]), "sourceBlobs": len(blobs), "manifest": str(file)}))


if __name__ == "__main__":
    main()
