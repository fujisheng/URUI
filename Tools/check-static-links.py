"""Full-link iOS/WebGL archives; native Wasm smoke is separate from Unity Player."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
package = repo / "Packages/com.fujisheng.urui"
output = (args.output or repo.parent / "Artifacts/ElementRuntime/StaticLinks").resolve()
output.mkdir(parents=True, exist_ok=True)
engine = Path(os.environ.get("URUI_UNITY_PLAYBACK_ENGINES", "/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/PlaybackEngines"))
em = Path(os.environ.get("URUI_EMSCRIPTEN_ROOT", str(engine / "WebGLSupport/BuildTools/Emscripten")))
env = os.environ.copy()
env.update(DEVELOPER_DIR="/Applications/Xcode.app/Contents/Developer", EM_CONFIG=str(em / ".emscripten"), EMCC_SKIP_SANITY_CHECK="1")
results = {}


def run(name, command):
    with (output / (name + ".log")).open("w") as log:
        try:
            code = subprocess.run(command, env=env, cwd=output, stdout=log, stderr=subprocess.STDOUT).returncode
        except OSError as error:
            log.write(str(error)); code = 127
    results[name] = {"exit_code": code, "log": str(output / (name + ".log"))}
    (output / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(name, code, flush=True)
    return code


sdk = subprocess.check_output(["xcrun", "--sdk", "iphoneos", "--show-sdk-path"], env=env, text=True).strip()
plugins = {row["platform"]: repo / row["package_path"] for row in json.loads((package / "Documentation~/PLUGINS.json").read_text())["plugins"]}
ios = plugins["ios-arm64"]
run("ios-full-link", ["xcrun", "--sdk", "iphoneos", "clang++", "-target", "arm64-apple-ios13.0", "-isysroot", sdk,
    "-dynamiclib", "-Wl,-force_load," + str(ios), "-framework", "Foundation", "-o", str(output / "ios-link-check.dylib")])
headers = "".join((package / "Native~" / name).read_text() for name in (
    "urui.h", "element_runtime.h", "document_resources.h",
    "render_plan_protocol.h", "render_program_parameters.h",
    "style_properties.h", "authoring.h"))
exports = sorted(set(re.findall(r"URUI_API\s+[^;{]+?\b(urui_\w+)\s*\(", headers)))
wasm = plugins["webgl"]
status = run("webgl-full-link", [str(em / "emscripten/em++"), "--no-entry", "-Wl,--whole-archive", str(wasm),
    "-Wl,--no-whole-archive", "-fexceptions", "-sDISABLE_EXCEPTION_CATCHING=0", "-sALLOW_MEMORY_GROWTH=1",
    "-sMODULARIZE=1", "-sENVIRONMENT=node", "-sWASM_BIGINT=1", "-sEXPORTED_RUNTIME_METHODS=" + json.dumps(["ccall"]),
    "-sEXPORTED_FUNCTIONS=" + json.dumps(["_" + name for name in exports] + ["_malloc", "_free"]), "-o", str(output / "element-runtime.js")])
if status == 0:
    script = output / "native-smoke.cjs"
    script.write_text('''const assert = require('node:assert/strict');
const create = require('./element-runtime.js');
create().then(m => {
  assert.equal(m._urui_bridge_abi_version(), 13);
  assert.equal(m._urui_element_api_version(), 4);
  assert.equal(m._urui_texture_protocol_version(), 1);
  const view = () => new DataView(m.HEAPU8.buffer);
  const surfaceOut = m._malloc(8);
  assert.equal(m._urui_surface_create(640, 480, surfaceOut), 1);
  const surface = view().getBigUint64(surfaceOut, true);
  assert.ok(surface > 0n);
  m._free(surfaceOut);
  const docOut = m._malloc(8);
  assert.equal(m._urui_document_create(surface, docOut), 1);
  const doc = view().getBigUint64(docOut, true);
  assert.ok(doc > 0n);
  m._free(docOut);
  assert.equal(m._urui_document_set_presentation(surface, doc, 0, 0, 1, 0), 1);
  const id = (1n << 63n) + 1n;
  assert.equal(m.ccall('urui_document_register_external_texture', 'number', ['bigint','bigint','string','number','number','bigint'], [surface,doc,'/borrow.png',64,32,id]), 1);
  assert.equal(m._urui_get_texture_kind(surface, id), 2);
  const info = m._malloc(12);
  assert.equal(m._urui_get_texture_info(surface, id, info, info + 4, info + 8), 1);
  assert.equal(m.HEAP32[(info + 8) >> 2], 0);
  m._free(info);
  assert.equal(m.ccall('urui_document_load_html', 'number', ['bigint','bigint','string','string'], [surface,doc,"<!doctype html><html><head><style>body{margin:0;}img{width:64px;height:32px;}</style></head><body><img src='borrow.png'></body></html>","/smoke.html"]), 1);
  assert.equal(m._urui_surface_update(surface), 1);
  assert.equal(m._urui_surface_render(surface), 1);
  const count = m._urui_command_count(surface);
  assert.ok(count > 0);
  const commands = m._malloc(count * 48);
  assert.equal(m._urui_get_commands(surface, commands, count), count);
  let found = false;
  const v = view();
  for (let i = 0; i < count; i++) if (v.getBigUint64(commands + i * 48 + 8, true) === id) found = true;
  assert.ok(found, '32-bit engine handles must emit full uint64 logical ID');
  m._free(commands);
  assert.equal(m._urui_document_close(surface, doc), 1);
  assert.equal(m._urui_get_texture_kind(surface, id), 0);
  assert.equal(m._urui_surface_destroy(surface), 1);
  console.log(JSON.stringify({passed:true, nativeWasm:true, unityPlayer:false, abi:13, elementApi:4, textureProtocol:1, highBitHandlePreserved:true}));
}).catch(e => { console.error(e); process.exit(1); });
''')
    run("webgl-native-smoke", [str(em / "node/node"), str(script)])
if any(row["exit_code"] for row in results.values()):
    raise SystemExit(1)
