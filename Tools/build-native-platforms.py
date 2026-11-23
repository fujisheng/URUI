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
engine = Path(os.environ.get("URUI_UNITY_PLAYBACK_ENGINES", "/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/PlaybackEngines"))
cmake = os.environ.get("URUI_CMAKE") or shutil.which("cmake") or str(engine / "AndroidPlayer/SDK/cmake/3.22.1/bin/cmake")
em = Path(os.environ.get("URUI_EMSCRIPTEN_ROOT", str(engine / "WebGLSupport/BuildTools/Emscripten")))
ndk = Path(os.environ.get("ANDROID_NDK_ROOT", str(engine / "AndroidPlayer/NDK")))
zig_command = os.environ.get("URUI_ZIG") or shutil.which("zig")
if not zig_command or not Path(zig_command).is_file():
    raise RuntimeError("Set URUI_ZIG to an installed Zig executable, or put zig on PATH.")
zig = Path(zig_command).resolve()
manifest_path = repo / "Packages/com.fujisheng.urui/Documentation~/PLUGINS.json"
manifest = json.loads(manifest_path.read_text())


def source_hash():
    digest = hashlib.sha256()
    paths = [p for p in native.iterdir() if p.is_file() and (p.suffix in (".cpp", ".h", ".inl", ".cmake") or p.name in ("CMakeLists.txt", "DEPENDENCIES.json"))]
    for folder in ("Engine/Include", "Engine/Source"):
        paths.extend(p for p in (native / folder).rglob("*") if p.is_file() and (p.suffix in (".cpp", ".h", ".inl", ".cmake") or p.name == "CMakeLists.txt"))
    for path in sorted(paths):
        digest.update(str(path.relative_to(native)).encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()


def protocol_version(name):
    sources = "\n".join(path.read_text() for path in sorted(native.iterdir()) if path.suffix in (".cpp", ".inl"))
    matches = re.findall(r"\bint\s+" + re.escape(name) + r"\s*\([^)]*\)\s*\{\s*return\s+(\d+)\s*;", sources)
    if len(matches) != 1:
        raise ValueError("Cannot read the owning native protocol version: " + name)
    return int(matches[0])


bridge_abi = protocol_version("urui_bridge_abi_version")
render_plan_version = protocol_version("urui_render_plan_version")
element_api_version = protocol_version("urui_element_api_version")
texture_protocol_version = protocol_version("urui_texture_protocol_version")
sequence_api_version = protocol_version("urui_sequence_api_version")


expected_exports = set(re.findall(r"URUI_API\s+[^;{]+?\b(urui_\w+)\s*\(",
                                 "\n".join(path.read_text() for path in sorted(native.glob("*.h")))))


def check_exports(platform, binary):
    llvm = ndk / "toolchains/llvm/prebuilt/darwin-x86_64/bin"
    if platform == "windows-x86_64":
        command = [str(llvm / "llvm-readobj"), "--coff-exports", str(binary)]
        raw = subprocess.check_output(command, text=True)
        actual = set(re.findall(r"Name: (urui_\w+)", raw))
    else:
        nm = em / "llvm/llvm-nm" if platform == "webgl" else llvm / "llvm-nm"
        raw = subprocess.check_output([str(nm), "--defined-only", "--extern-only", str(binary)], text=True)
        actual = set(re.findall(r"\b_?(urui_\w+)\s*$", raw, re.MULTILINE))
        if platform == "webgl":
            private_symbols = set(re.findall(r"^#define (\w+) URUI_FT_", (native / "freetype_symbols.h").read_text(), re.MULTILINE))
            defined = {line.split()[-1] for line in raw.splitlines() if re.fullmatch(r"[0-9a-fA-F]+ [A-Za-z] [A-Za-z_][A-Za-z_0-9]*", line)}
            collisions = private_symbols & defined
            if collisions or any(name.startswith(("FT_", "ft_")) for name in defined):
                raise ValueError("unisolated FreeType definitions: " + str(sorted(collisions)))

    if actual != expected_exports:
        raise ValueError("export mismatch: missing=" + str(sorted(expected_exports - actual)) + ", extra=" + str(sorted(actual - expected_exports)))
    return len(actual)


def invalidate_runtime_evidence(entry):
    # Architecture/export checks do not carry runtime evidence to a rebuilt binary.
    for name in ("protocol_versions", "player_validation", "static_link_validation", "build_audit"):
        entry.pop(name, None)
    entry["runtime_status"] = "not_run"
    entry["runtime_validation"] = None


def build(platform):
    before = source_hash()
    directory = output / platform
    env = os.environ.copy()
    env.update(URUI_CMAKE=str(cmake), URUI_DEBUG="OFF", ANDROID_NDK_ROOT=str(ndk),
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
                strip = ndk / "toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-strip"
                subprocess.run([str(strip), "--strip-debug", str(binary)], check=True)
            exports = check_exports(platform, binary)
            row.update(bytes=binary.stat().st_size, sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),
                       abi_version=bridge_abi, render_plan_version=render_plan_version, element_api_version=element_api_version, texture_protocol_version=texture_protocol_version, sequence_api_version=sequence_api_version, urui_exports=exports, native_source_sha256=before)
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
    # 复制前复核，避免编译期间新启动的 Editor 仍持有将被替换的插件。
    editors = subprocess.run(["pgrep", "-x", "Unity"], capture_output=True, text=True)
    if editors.returncode != 1:
        raise RuntimeError("Close Unity Editors normally before installing native plugins; detected PIDs: " + editors.stdout.strip())
    by_platform = {row["platform"]: row for row in results}
    for entry in manifest["plugins"]:
        row = by_platform[entry["platform"]]
        destination = repo / entry["package_path"]
        temporary = destination.with_name(destination.name + ".new")
        shutil.copy2(row["binary"], temporary)
        temporary.replace(destination)
        invalidate_runtime_evidence(entry)
        entry.update(bytes=row["bytes"], sha256=row["sha256"], abi_version=row["abi_version"], render_plan_version=row["render_plan_version"], element_api_version=element_api_version, texture_protocol_version=texture_protocol_version, sequence_api_version=sequence_api_version,
                     runtime_validation=None, compiled=True, URUI_DEBUG=False,
                     urui_exports=row["urui_exports"], native_source_sha256=row["native_source_sha256"],
                     abi_validation=f"ABI{bridge_abi} / render-plan{render_plan_version}; export set verified against declarations; runtime evidence is separate")
        if "load_segment_alignments" in row:
            entry["load_segment_alignments"] = row["load_segment_alignments"]
    manifest["required_abi_version"] = bridge_abi
    manifest["render_plan_version"] = render_plan_version
    manifest["texture_protocol_version"] = texture_protocol_version
    manifest["sequence_api_version"] = sequence_api_version
    manifest["sourceSha256"] = source_hash()
    manifest["production_exports"] = sorted(expected_exports)
    manifest["production_export_set_sha256"] = hashlib.sha256(("\n".join(sorted(expected_exports)) + "\n").encode()).hexdigest()
    manifest["production_exports_scope"] = f"All seven packaged ABI{bridge_abi}/render-plan{render_plan_version} plugins have the same {len(expected_exports)} production exports; diagnostics excluded"
    manifest["runtimeValidationScope"] = "Runtime evidence must be rerun after rebuilding; see individual plugin entries"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
