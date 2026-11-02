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
em = Path("/Volumes/FData/MacStorage/Applications/Unity/6000.3.22f1/PlaybackEngines/WebGLSupport/BuildTools/Emscripten")
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
headers = (package / "Native~/urui.h").read_text() + (package / "Native~/element_runtime.h").read_text()
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
  assert.equal(m._urui_bridge_abi_version(), 3);
  assert.equal(m._urui_element_api_version(), 1);
  assert.equal(m._urui_texture_protocol_version(), 1);
  assert.equal(m._urui_create(640, 480, 0), 1);
  const id = (1n << 63n) + 1n;
  assert.equal(m.ccall('urui_register_external_texture', 'number', ['string','number','number','bigint'], ['borrow.png',64,32,id]),1);
  assert.equal(m._urui_get_texture_kind(id),2);
  const info=m._malloc(12);
  assert.equal(m._urui_get_texture_info(id,info,info+4,info+8),1);
  assert.equal(m.HEAP32[(info+8)>>2],0);
  m._free(info);
  assert.equal(m.ccall('urui_load_document', 'number', ['string'], ["<rml><body><img src='borrow.png' width='64' height='32'/></body></rml>"]), 1);
  m._urui_update(); m._urui_render();
  const count=m._urui_command_count(); assert.ok(count>0);
  const commands=m._malloc(count*48);
  assert.equal(m._urui_get_commands(commands,count),count);
  let found=false;
  const view=new DataView(m.HEAPU8.buffer);
  for(let i=0;i<count;i++) if(view.getBigUint64(commands+i*48+8,true)===id) found=true;
  assert.ok(found,'32-bit Rml handles must emit full uint64 logical ID'); m._free(commands);
  m._urui_unload_document();
  assert.equal(m.ccall('urui_unregister_texture','number',['string'],['borrow.png']),1);
  assert.equal(m._urui_get_texture_kind(id),0);
  m._urui_destroy();
  console.log(JSON.stringify({passed:true, nativeWasm:true, unityPlayer:false, abi:3, elementApi:1, textureProtocol:1, highBitHandlePreserved:true}));
}).catch(e => { console.error(e); process.exit(1); });
''')
    run("webgl-native-smoke", [str(em / "node/node"), str(script)])
if any(row["exit_code"] for row in results.values()):
    raise SystemExit(1)
