# URUI Local Agent HTTP API v1

## Connection, Threading & Async Jobs

Enable **Local API** in Unity menu and inspect `<projectRoot>/Library/URUI/Authoring/endpoint.json` for `url`, `token`, `editorSession`, `project`, and `apiVersion`. Binds strictly to `127.0.0.1` (`0600` user permissions on macOS/Linux; re-read endpoint metadata across Editor restarts or domain reloads).

Send headers on every HTTP request:

```text
Authorization: Bearer <token>
X-URUI-Session: <editorSession>
Content-Type: application/json
```

`POST /v1/commands` accepts up to 1 MiB strict JSON (rejecting duplicate keys, non-finite numbers, invalid escapes, disallowed Origin/Host) and returns `HTTP 202` with `{job, status, editorSession}`. Poll `GET /v1/jobs/<id>` until `done`, `failed`, or `cancelled` (`DELETE /v1/jobs/<id>` cancels queued jobs only; returns `409` once running/done). Commands execute on Unity's main thread.

## Operations & Parameters

Supply `mode: "authoring" | "live"` on every call except `capabilities` / `documents.list`, plus `session` and integer `expectedRevision` on writes (distinct from header `editorSession`):

| `op` | Arguments & Behavior |
| --- | --- |
| `capabilities` | Protocol versions, active session, property schema, installed extensions, supported ops |
| `documents.list` | Lists currently loaded runtime documents (`id`, name, source) |
| `document.open` | (`authoring`) `path` — opens HTML working copy and refreshes preview |
| `document.select` | (`live`) `document` ID from `documents.list` |
| `document.read` | (`authoring`) optional `path` — returns UTF-16 offsets, `text`, `dirty`, `fileRevision` |
| `document.edit` | (`authoring`) optional `path`, candidate full `text` — validates cleanly before applying |
| `document.reload` | (`authoring`) `discardLocal` (`false` by default; must be explicit to drop edits) |
| `document.save` | (`authoring`) writes dirty working set checking external disk modifications |
| `hierarchy.children` | optional `target`, `offset` (`0`), `limit` (`100`, max `500`) |
| `hierarchy.find` | exact `id` match `name` (up to `500` items; returns distinct element identities) |
| `selection.set` | selects `target` in URUI Inspector |
| `properties.read` | `target`, optional `properties` array — returns `value`, `computed`, `origin` |
| `properties.set` | `target, property, value` or batch `edits: [{target, property, value, unset}]` |
| `structure.insert` | (`authoring`) `parent, html, index` (`-1` appends) |
| `structure.remove` | (`authoring`) `target` |
| `structure.move` | (`authoring`) `target, parent, index` (rejects cycles) |
| `style.rule` | (`authoring`) `path, ruleStart, property, value/unset` (`ruleStart` UTF-16 offset) |
| `history.undo` / `history.redo` | Active session history step |
| `preview.viewport` | (`authoring`) `width, height` clamped `64..8192` |
| `preview.render` | Refreshes active authoring preview (yields `Surface` during Play Mode) |
| `preview.capture` | Renders real frame to PNG under `Library/`, returns absolute path, `renderedRevision`, `frameId` |
| `preview.input` | `kind: pointer` (`phase: Down/Move/Up`, `x, y`, `button`, `pressedButtons`), `key`, `text`, or `reset` |
| `extension` | `extension: "effect", operation, args` + `target` |
| `persistence.apply` | (`live`) `target, properties: [...]` — commits mapped live tweaks to HTML/CSS source |

Editable properties match `capabilities` (`style.width`, `style.opacity`, `attr.id`, `attr.src`, `text`, `value`, `image`, etc.). Node handles are valid only within the current source revision / active Play session—never cache handles across reloads. Writes reject stale `expectedRevision` atomically.

## Effect Extension Operations

Returns `effect_not_installed` when `com.fujisheng.urui.effect` is absent:

| `operation` | `args` |
| --- | --- |
| `asset.open` / `asset.read` | `path` (`EffectPreset`, `Material`, or particle Prefab) / reads working copy & `assetRevision` |
| `asset.setParticle` | `assetRevision, property, value` (`duration`, `loop`, `startLifetime`, `startSpeed`, `startSize`, `maxParticles`, `emissionRate`, `shapeEnabled`, `shapeRadius`, `startColor`) |
| `asset.setMaterial` | `assetRevision, property, value` (Shader uniforms; comma-separated colors/vectors, asset path strings for textures) |
| `asset.setPreset` | `assetRevision, property, value` (clamped `EffectParameters` fields) |
| `asset.apply` / `asset.saveAs` / `asset.discard` | Explicit asset commit (`assetRevision`), save to new path, or discard working draft |
| `particles.play/pause/restart/refresh` | Targets active `urui-particles` node |
| `particles.seek` / `particles.step` | `time` (s) / `delta` (s) |

## Python Stdlib Client Example

```python
import json, time, urllib.request

with open("Library/URUI/Authoring/endpoint.json", encoding="utf-8") as f:
    endpoint = json.load(f)

def command(**body):
    headers = {"Content-Type": "application/json",
               "Authorization": "Bearer " + endpoint["token"],
               "X-URUI-Session": endpoint["editorSession"]}
    req = urllib.request.Request(endpoint["url"] + "v1/commands",
        data=json.dumps(body, ensure_ascii=False).encode("utf-8"), headers=headers)
    with urllib.request.urlopen(req, timeout=15) as response:
        job = json.load(response)["job"]
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        req = urllib.request.Request(endpoint["url"] + "v1/jobs/" + job, headers=headers)
        with urllib.request.urlopen(req, timeout=15) as response:
            state = json.load(response)
        if state["status"] == "done":
            return state["result"]
        if state["status"] in ("failed", "cancelled"):
            raise RuntimeError(state)
        time.sleep(.05)
    raise TimeoutError(job)

caps = command(op="capabilities")
opened = command(op="document.open", mode="authoring", session=caps["session"],
    expectedRevision=caps["sourceRevision"], path="Assets/MyUI/Inventory.html")
found = command(op="hierarchy.find", mode="authoring", name="page-title")
target = found["data"]["nodes"][0]["id"]
edited = command(op="properties.set", mode="authoring", session=found["session"],
    expectedRevision=found["sourceRevision"], target=target, property="text", value="Equipment")
capture = command(op="preview.capture", mode="authoring")
print(capture["renderedRevision"], capture["frameId"], capture["data"]["path"])
# Commit explicitly when verified:
# command(op="document.save", mode="authoring", session=edited["session"],
#         expectedRevision=edited["sourceRevision"])
```

External edits to HTML/CSS files on disk are supported; re-read revisions and handle `source_conflict` explicitly if dirty.

## Animation Commands

Shares `mode`, `session`, and `expectedRevision` guards. Switch UI mode via `workspace.mode` (`"layout" | "animation" | "live"`). Source writes require `authoring`; timeline scrub (`animation.sample`, `play`, `stop`) runs under `authoring` in Edit Mode and `live` in Play Mode:

| `op` | Parameters |
| --- | --- |
| `animation.read` | Returns `path`, `definition`, `time`, `playing`, `recording`, `selectedTrack` |
| `animation.open` / `animation.create` | Open explicit `.urui-sequence.json` / initialize matching page sequence draft |
| `animation.preset` | `target` (source node key), `preset`, `start` (`0`), `duration` (`.3`) → `selectedTrack` |
| `animation.particles` | `target` (`urui-particles` key), `source`, `start` (`0`), `duration` (`1`) |
| `animation.track` | `track` id + `changes` (`start`, `duration`, `iterations`, `alternate`, `muted`, `easing`, `target`, `keyframes`, `source`, `seed`, `speed`) |
| `animation.lock` / `animation.remove` | Toggle lock (`track, locked`) / remove track while retaining shared CSS rules |
| `animation.key` | `track, position (0..1), property, value` (or batch `keys`, `unset: true` to clear) |
| `animation.keys.move` / `animation.keys.remove` | Atomic `keys: [{track, position}]` transform (`seconds`, `copy`) / removal |
| `animation.sample` / `animation.play/pause/stop` | Seek deterministically (no completion event) / control timeline playback |
| `animation.record` | `enabled` (`track, target` when enabling so `properties.set` writes keyframes) |

Note `animation.track.changes.target` uses unique runtime HTML `id` (matching `.urui-sequence.json`), whereas command top-level `target` expects the session source node key. `document.save` writes HTML/CSS/JSON and returns `data.pendingAssets` listing dirty Effects assets (commit those explicitly via `asset.apply`).

```python
state = command(op="capabilities")
mode = "authoring"
session = state["session"]

def write(op, **args):
    global state
    state = command(op=op, mode=mode, session=session,
                    expectedRevision=state["sourceRevision"], **args)
    return state

write("workspace.mode", workspace="layout")
write("document.open", path="Assets/UI/Reward.html")
found = command(op="hierarchy.find", mode=mode, name="reward")
target = found["data"]["nodes"][0]["id"]
write("animation.preset", target=target, preset="popup", duration=0.3)
track = state["data"]["selectedTrack"]
write("animation.key", track=track, position=0.5, property="opacity", value="0.7")
write("animation.sample", time=0.15)
shot = command(op="preview.capture", mode=mode)
write("document.save")
```
