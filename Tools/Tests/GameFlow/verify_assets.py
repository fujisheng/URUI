from pathlib import Path
import json, xml.etree.ElementTree as ET
import struct, hashlib
root = Path(__file__).resolve().parents[3] / "Packages/com.fujisheng.urui/Samples~/Game Flow"
resources = root / "Resources/URUI/GameFlow"
for page in ["Loading", "Login", "Home", "Inventory", "Settings", "Leaderboard"]:
    path = resources / "Rml" / (page + ".rml.txt")
    assert path.is_file(), f"Missing importable page: {page}"
    tree = ET.fromstring(path.read_text())
    links = tree.findall("./head/link")
    assert len(links) == 1 and links[0].get("href") == "Game.rcss", f"External stylesheet: {page}"
    ids = [e.get("id") for e in tree.iter() if e.get("id")]
    assert len(ids) == len(set(ids)), f"Duplicate page identity: {page}"
assert (resources / "Game.rcss.txt").is_file()
home = ET.fromstring((resources / "Rml/Home.rml.txt").read_text())
inventory = ET.fromstring((resources / "Rml/Inventory.rml.txt").read_text())
assert home.find(".//*[@id='player-hud']") is not None, "Game home needs a player HUD"
assert home.find(".//*[@id='open-expedition']") is not None, "Game home needs a primary adventure action"
assert inventory.find(".//*[@id='item-grid']") is not None, "Inventory must use item slots"
assert "sidebar" not in (resources / "Rml/Home.rml.txt").read_text(), "Do not retain website navigation"
assert (resources / "Fonts/LatoLatin-Regular.ttf.bytes").stat().st_size > 10000
for name in ["world", "operator", "avatar", "slot-normal", "slot-empty", "slot-selected", "action-left", "action-middle", "action-right"] + ["item-%02d" % i for i in range(1,11)]:
    asset = resources / "Images" / (name + ".png")
    data = asset.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", asset
    assert min(struct.unpack(">II", data[16:24])) > 0, asset
    assert "isReadable: 0" in asset.with_name(asset.name + ".meta").read_text(), asset
manifest = json.loads((resources / "asset-manifest.json.txt").read_text())
pngs = {p.name for p in (resources / "Images").glob("*.png")}
assert len(pngs) == 40 and {Path(a["runtimeFile"]).name for a in manifest["assets"]} == pngs
aliases = {a["runtimeAlias"] for a in manifest["assets"]}
for asset in manifest["assets"]:
    path = resources / asset["runtimeFile"]
    assert hashlib.sha256(path.read_bytes()).hexdigest() == asset["sha256"], path
    assert "design-master" not in path.name
for page in (resources / "Rml").glob("*.txt"):
    for node in ET.fromstring(page.read_text()).iter("img"):
        assert node.get("src") in aliases, (page.name, node.get("src"))
assert (root / "THIRD_PARTY_NOTICES.txt").is_file()
catalog = json.loads((resources / "Catalog.json.txt").read_text())
assert len(catalog["inventory"]) >= 8 and len(catalog["rankings"]) >= 30
for cs in (root / "Scripts").glob("*.cs"):
    content = cs.read_text()
    assert "URUI.Internal" not in content and "URUI_DEBUG" not in content, cs
    assert "<rml" not in content and "<style" not in content and "SetInnerMarkup(" not in content, cs
assert (root / "GameFlow.unity").is_file()
package = json.loads((root.parents[1] / "package.json").read_text())
assert package["samples"][0]["displayName"] == "Game Flow", "Primary package sample must be Game Flow"
assert package["samples"][0]["path"] == "Samples~/Game Flow"
assert any(s["path"] == "Samples~/Game Flow" for s in package["samples"])
print("GAME_FLOW_ASSET_PASS six pages, external stylesheet, independent catalog/font and public sample API")
