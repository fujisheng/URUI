"""Use Unity's package Sample.Import, then run the short sample Editor acceptance."""
import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[1]
project = repo.parent
sample = repo / "Packages/com.fujisheng.urui/Samples~/Game Flow"
fixtures = repo / "Packages/com.fujisheng.urui/Tests~/Editor/Fixtures"
target = project / "Assets/URUI/Verification"
output = project / "Artifacts/GameFlow"
output.mkdir(parents=True, exist_ok=True)
stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
archive = output / "ImportedCopies" / stamp

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def archive_owned(directory, marker_name):
    if not directory.exists():
        return
    marker = directory / marker_name
    if not marker.exists():
        raise SystemExit("Refusing to move an unowned import: " + str(directory))
    records = json.loads(marker.read_text())
    expected_files = {name for name in records if not name.endswith(".meta")}
    actual_files = {str(path.relative_to(directory)) for path in directory.rglob("*")
                    if path.is_file() and path.suffix != ".meta" and path != marker}
    if actual_files != expected_files:
        raise SystemExit("Imported sample contains added or removed files; preserve it before validation: " + str(directory))
    for relative, expected in records.items():
        path = directory / relative
        if path.suffix != ".meta" and digest(path) != expected:
            raise SystemExit("Imported sample was edited; preserve it before validation: " + str(path))
    # Preserve the whole previous generated import, including metadata, outside Assets.
    archive.mkdir(parents=True, exist_ok=True)
    shutil.move(str(directory), str(archive / directory.name))
    meta = directory.with_name(directory.name + ".meta")
    if meta.exists():
        shutil.move(str(meta), str(archive / meta.name))

# One-time migration of the earlier tool-owned development copy. It is never a
# source or destination for sample import. Preserve it to avoid duplicate GUIDs/assemblies.
archive_owned(project / "Assets/Game Flow Showcase", ".urui-sample-import.json")
previous_import = output / "package-import.json"
if previous_import.exists():
    previous = json.loads(previous_import.read_text())
    prior = Path(previous["importPath"])
    if not prior.is_absolute():
        prior = project / prior
    if prior.exists():
        prior.resolve().relative_to((project / "Assets/Samples").resolve())
        archive_owned(prior, ".urui-upm-sample-owner.json")
    previous_import.replace(output / (stamp + "-previous-package-import.json"))

# These fixtures reference the sample assembly. Archive them during bootstrap so
# Unity can compile and import a previously unimported sample using only Editor APIs.
for relative in ("GameFlowSmoke.cs", "GameFlowSmoke.cs.meta", "Editor/GameFlowValidation.cs", "Editor/GameFlowValidation.cs.meta", "GameFlowRecording.cs", "GameFlowRecording.cs.meta", "Editor/GameFlowRecordingValidation.cs", "Editor/GameFlowRecordingValidation.cs.meta"):
    path = target / relative
    if path.exists():
        destination = archive / "Verification" / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(path), str(destination))
(target / "Editor").mkdir(parents=True, exist_ok=True)
shutil.copy2(fixtures / "Editor/GameFlowPackageImport.cs", target / "Editor/GameFlowPackageImport.cs")
unity = "/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity"
bootstrap = [unity, "-batchmode", "-projectPath", str(project), "-executeMethod",
             "URUI.Tests.Editor.GameFlowPackageImport.Run", "-logFile", str(output / "package-import.log")]
print(json.dumps({"stage": "native Unity Sample.Import", "command": bootstrap}), flush=True)
result = subprocess.run(bootstrap).returncode
if result:
    raise SystemExit(result)
report = json.loads((output / "package-import.json").read_text())
assert report["passed"] and report["nativeUpmImport"] and report["displayName"] == "Game Flow"
destination = Path(report["importPath"])
if not destination.is_absolute():
    destination = project / destination
destination.resolve().relative_to((project / "Assets/Samples").resolve())
assert Path(report["resolvedPath"]).resolve() == sample.resolve(), report["resolvedPath"]
source_files = [p for p in sample.rglob("*") if p.is_file() and p.suffix != ".meta"]
for path in source_files:
    imported = destination / path.relative_to(sample)
    assert imported.is_file() and digest(imported) == digest(path), str(imported)
report["sourceFilesMatched"] = len(source_files)
report["runtimePngs"] = len(list((destination / "Resources/URUI/GameFlow/Images").glob("*.png")))
report["sourceIsPackageSample"] = True
(output / "package-import.json").write_text(json.dumps(report, indent=2) + "\n")
marker = destination / ".urui-upm-sample-owner.json"
marker.write_text(json.dumps({str(p.relative_to(destination)): digest(p)
                             for p in destination.rglob("*") if p.is_file() and p != marker}, indent=2) + "\n")
shutil.copy2(fixtures / "GameFlowSmoke.cs", target / "GameFlowSmoke.cs")
shutil.copy2(fixtures / "Editor/GameFlowValidation.cs", target / "Editor/GameFlowValidation.cs")
prior_report = output / "Editor/game-flow.json"
if prior_report.exists():
    history = output / "PreviousAcceptance"
    history.mkdir(exist_ok=True)
    shutil.move(str(prior_report), str(history / (str(prior_report.stat().st_mtime_ns) + "-game-flow.json")))
command = [unity, "-batchmode", "-projectPath", str(project), "-executeMethod",
           "URUI.Tests.Editor.GameFlowValidation.Run", "-logFile", str(output / "editor.log")]
if "--checkpoint" in sys.argv:
    command.append("--game-flow-checkpoint")
if "--reset-demo-preferences" in sys.argv:
    command.append("--reset-game-flow-demo-preferences")
print(json.dumps({"stage": "package-imported sample acceptance", "sampleImport": str(destination), "command": command}), flush=True)
faults = [destination / ("Resources/URUI/GameFlow/Images/" + name + ".png")
          for name in ("item-03", "action-middle")]
for path in faults:
    path.unlink()
try:
    result = subprocess.run(command).returncode
finally:
    for path in faults:
        if not path.exists():
            shutil.copy2(sample / path.relative_to(destination), path)
if result == 0:
    log = (output / "editor.log").read_text(errors="replace")
    diagnostics = [line for line in log.splitlines() if any(text in line for text in (
        "Syntax error parsing property", "Unable to open file", "No font face defined", "Failed to load style sheet"))]
    if diagnostics:
        print("Native resource/style diagnostics:", *diagnostics, sep="\n")
        result = 2
raise SystemExit(result)
