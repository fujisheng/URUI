"""Compile current runtime against Unity's generated reference/define contract."""
import argparse
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--project", type=Path)
parser.add_argument("--debug", action="store_true")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
project = (args.project or repo.parent / "Tests/ElementRuntime").resolve()
responses = list((project / "Library/Bee/artifacts").glob("*/URUI.Runtime.rsp"))
if not responses:
    parser.error("Run verify-element-runtime.py once to generate the Unity compiler response")
output = project / "Evidence/Compilation"
output.mkdir(parents=True, exist_ok=True)
lines = []
for row in responses[0].read_text(encoding="utf-8-sig").splitlines():
    if row.strip('"').endswith('.cs') or row.startswith(('-out:', '-refout:', '/additionalfile:', '-analyzer:', '/analyzer:', '-doc:')):
        continue
    lines.append(row)
if args.debug:
    lines.append('-define:URUI_DEBUG')
lines.append('-out:"' + str(output / 'URUI.Runtime.dll') + '"')
lines.extend('"' + str(p) + '"' for p in (repo / 'Packages/com.fujisheng.urui/Runtime').rglob('*.cs'))
rsp = output / 'runtime.rsp'
rsp.write_text('\n'.join(lines))
compiler = '/usr/local/share/dotnet/sdk/8.0.424/Roslyn/bincore/csc.dll'
raise SystemExit(subprocess.run(['dotnet', compiler, '@' + str(rsp)], cwd=project).returncode)
