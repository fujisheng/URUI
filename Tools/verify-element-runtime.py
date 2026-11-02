"""Prepare a disposable real Unity project from the current package and candidate plugin."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--project", type=Path)
parser.add_argument("--native", type=Path)
parser.add_argument("--method", default="URUI.Tests.Editor.ElementRuntimeValidation.Run")
parser.add_argument("--playground", action="store_true")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
project = (args.project or repo.parent / "Tests/ElementRuntime").resolve()
project.mkdir(parents=True, exist_ok=True)
package = repo / "Packages/com.fujisheng.urui"
embedded = project / "Packages/com.fujisheng.urui"
embedded.mkdir(parents=True, exist_ok=True)
# Replace only generated package/fixture copies; never delete a caller's project.
for name in ["Runtime", "Editor"]:
    if (embedded / name).exists():
        shutil.rmtree(embedded / name)
    shutil.copytree(package / name, embedded / name)
shutil.copy2(package / "package.json", embedded / "package.json")
if args.native:
    shutil.copy2(args.native, embedded / "Runtime/Plugins/macOS/libURUIBridge.dylib")
(project / "Packages/manifest.json").write_text(json.dumps({"dependencies": {
    "com.fujisheng.urui": "file:com.fujisheng.urui",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.imgui": "1.0.0"
}}, indent=2))
settings = project / "ProjectSettings"
settings.mkdir(exist_ok=True)
for name in ["ProjectVersion.txt", "ProjectSettings.asset"]:
    shutil.copy2(repo.parent / "ProjectSettings" / name, settings / name)
fixtures = project / "Assets/ElementRuntimeChecks"
fixtures.mkdir(parents=True, exist_ok=True)
source_fixtures = package / "Tests~/Editor/Fixtures"
for source in source_fixtures.rglob("*.cs"):
    target = fixtures / source.relative_to(source_fixtures)
    if source.name.startswith("Playground"):
        # The --playground copy already contains these dev-only checks.
        if target.exists():
            target.unlink()
        continue
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
if args.playground:
    shutil.copytree(repo.parent / "Assets/URUI", project / "Assets/URUI", dirs_exist_ok=True)
    # Keep one import per sample assembly, including the current development sample.
    generated_controls = project / "Assets/URUISamples/Runtime Controls"
    if generated_controls.exists():
        shutil.rmtree(generated_controls)
    shutil.copytree(package / "Samples~/Runtime Controls", project / "Assets/URUI/Samples/Runtime Controls", dirs_exist_ok=True)
else:
    for sample in ["Hello URUI", "Runtime Controls"]:
        shutil.copytree(package / "Samples~" / sample, project / "Assets/URUISamples" / sample, dirs_exist_ok=True)
unity = Path("/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity")
log = project / "element-runtime-editor.log"
command = [str(unity), "-batchmode", "-projectPath", str(project), "-executeMethod", args.method, "-logFile", str(log)]
print(json.dumps({"project": str(project), "log": str(log), "command": command}), flush=True)
raise SystemExit(subprocess.run(command).returncode)
