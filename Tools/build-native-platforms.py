"""Build independent native targets using installed toolchains; optional package installation."""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import json
import os
import re
import struct
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path)
parser.add_argument("--install", action="store_true")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
native = repo / "Packages/com.fujisheng.urui/Native~"
output = (args.output or repo.parent / "Artifacts/ElementRuntime/Native").resolve()
output.mkdir(parents=True, exist_ok=True)
engine = Path("/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/PlaybackEngines")
cmake = engine / "AndroidPlayer/SDK/cmake/3.22.1/bin/cmake"
em = Path("/Volumes/FData/MacStorage/Applications/Unity/6000.3.22f1/PlaybackEngines/WebGLSupport/BuildTools/Emscripten")
zig = repo.parent / "Work/PackageAndApi/toolchains/zig-aarch64-macos-0.15.2/zig"
manifest_path = repo / "Packages/com.fujisheng.urui/Documentation~/PLUGINS.json"
manifest = json.loads(manifest_path.read_text())


def source_hash():
    digest = hashlib.sha256()
    for path in sorted(native.iterdir()):
        if path.is_file() and (path.suffix in (".cpp", ".h", ".inl", ".cmake") or path.name in ("CMakeLists.txt", "DEPENDENCIES.json")):
            digest.update(path.name.encode())
            digest.update(path.read_bytes())
    return digest.hexdigest()


expected_exports = set(re.findall(r"URUI_API\s+[^;{]+?\b(urui_\w+)\s*\(",
                                 (native / "urui.h").read_text() + (native / "element_runtime.h").read_text()))


def check_exports(platform, binary):
    llvm = engine / "AndroidPlayer/NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin"
    if platform == "windows-x86_64":
        command = [str(llvm / "llvm-readobj"), "--coff-exports", str(binary)]
        raw = subprocess.check_output(command, text=True)
        actual = set(re.findall(r"Name: (urui_\w+)", raw))
    else:
        nm = em / "llvm/llvm-nm" if platform == "webgl" else llvm / "llvm-nm"
        raw = subprocess.check_output([str(nm), "--defined-only", "--extern-only", str(binary)], text=True)
        actual = set(re.findall(r"\b_?(urui_\w+)\s*$", raw, re.MULTILINE))
    if actual != expected_exports:
        raise ValueError("export mismatch: missing=" + str(sorted(expected_exports - actual)) + ", extra=" + str(sorted(actual - expected_exports)))
    return len(actual)


def build(platform):
    before = source_hash()
    directory = output / platform
    env = os.environ.copy()
    env.update(URUI_CMAKE=str(cmake), URUI_DEBUG="OFF", ANDROID_NDK_ROOT=str(engine / "AndroidPlayer/NDK"),
               URUI_ZIG=str(zig), DEVELOPER_DIR="/Applications/Xcode.app/Contents/Developer",
               EMSCRIPTEN=str(em / "emscripten"), EM_CONFIG=str(em / ".emscripten"), EMCC_SKIP_SANITY_CHECK="1")
    log = output / (platform + ".log")
    command = ["bash", str(native / "build-platform.sh"), platform, str(directory)]
    with log.open("w") as stream:
        status = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT).returncode
    name = "URUIBridge.dll" if platform == "windows-x86_64" else "libURUIBridge.a" if platform in ("ios-arm64", "webgl") else "libURUIBridge.so" if platform.startswith("android") else "libURUIBridge.dylib"
    binary = directory / name
    row = {"platform": platform, "exit_code": status, "log": str(log), "binary": str(binary), "URUI_DEBUG": False}
    if status == 0:
        try:
            if platform.startswith("android"):
                strip = engine / "AndroidPlayer/NDK/toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-strip"
                subprocess.run([str(strip), "--strip-debug", str(binary)], check=True)
            exports = check_exports(platform, binary)
            row.update(bytes=binary.stat().st_size, sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),
                       abi_version=3, element_api_version=1, texture_protocol_version=1, urui_exports=exports, native_source_sha256=before)
            if source_hash() != before:
                raise ValueError("Native sources changed during this build; rebuild incrementally")
            if platform.startswith("android"):
                data = binary.read_bytes()
                start = struct.unpack_from("<Q", data, 32)[0]
                size, count = struct.unpack_from("<HH", data, 54)
                row["load_segment_alignments"] = [struct.unpack_from("<Q", data, start + i * size + 48)[0]
                    for i in range(count) if struct.unpack_from("<I", data, start + i * size)[0] == 1]
                if min(row["load_segment_alignments"]) < 16384:
                    raise ValueError("Android load segment is not 16 KiB aligned")
        except Exception as error:
            row.update(exit_code=2, verification_error=str(error))
    return row


results = []
with ThreadPoolExecutor(max_workers=2) as pool:
    futures = [pool.submit(build, row["platform"]) for row in manifest["plugins"]]
    for future in as_completed(futures):
        row = future.result()
        results.append(row)
        (output / "build-results.json").write_text(json.dumps(results, indent=2) + "\n")
        print(row["platform"], row["exit_code"], flush=True)
if any(row["exit_code"] for row in results):
    raise SystemExit(1)
if args.install:
    by_platform = {row["platform"]: row for row in results}
    for entry in manifest["plugins"]:
        row = by_platform[entry["platform"]]
        destination = repo / entry["package_path"]
        temporary = destination.with_name(destination.name + ".new")
        shutil.copy2(row["binary"], temporary)
        temporary.replace(destination)
        entry.update(bytes=row["bytes"], sha256=row["sha256"], element_api_version=1, texture_protocol_version=1,
                     runtime_validation=None, compiled=True, URUI_DEBUG=False,
                     urui_exports=row["urui_exports"], native_source_sha256=row["native_source_sha256"],
                     abi_validation="ABI3 layouts unchanged; element v1 / texture v1 entry points present; runtime versions require matching evidence")
        if "load_segment_alignments" in row:
            entry["load_segment_alignments"] = row["load_segment_alignments"]
    manifest["texture_protocol_version"] = 1
    manifest["production_exports"] = sorted(expected_exports)
    manifest["production_export_set_sha256"] = hashlib.sha256(("\n".join(sorted(expected_exports)) + "\n").encode()).hexdigest()
    manifest["production_exports_scope"] = f"All seven packaged ABI3 plugins have the same {len(expected_exports)} production exports; diagnostics excluded"
    manifest["runtimeValidationScope"] = "Runtime evidence must be rerun after rebuilding; see individual plugin entries"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
