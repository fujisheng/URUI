"""Compile package sources with the existing Unity compiler contract; no UnityCLI."""
from pathlib import Path
import argparse
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path)
parser.add_argument('--output', type=Path, help='Separate compilation evidence for the current task.')
parser.add_argument('--player', action='store_true', help='Compile Runtime/Effect with a real Player response contract; does not build a Player.')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
project = (args.project or repo.parent / 'Client').resolve()
candidates = list((project / 'Library/Bee/artifacts').glob('*/' + ('URUI.Runtime.rsp' if args.player else 'URUI.Editor.rsp')))
rsp = next(path for path in candidates if ('UNITY_EDITOR' not in path.read_text(encoding='utf-8-sig')) == args.player)
output = (args.output or repo / '.superpowers/sdd/2026-11-11-urui-authoring/Compilation').resolve()
if args.player:
    output /= 'Player'
output.mkdir(parents=True, exist_ok=True)
base = []
for row in rsp.read_text(encoding='utf-8-sig').splitlines():
    if row.strip('"').endswith('.cs') or row.startswith(('-out:', '-refout:', '-analyzer:', '/analyzer:', '/additionalfile:', '-doc:')):
        continue
    if row.startswith(('-r:', '-reference:')) and ('URUI.' in row or 'Assembly-CSharp' in row):
        continue
    base.append(row)
if args.player:
    # The saved build predates optional media/effect code; use the same Player's engine modules.
    core = next(row for row in base if 'UnityEngine.CoreModule.dll' in row)
    managed = Path(re.search(r'"([^"]+)"', core).group(1)).parent
    for module in managed.glob('UnityEngine.*.dll'):
        reference = '-r:"' + str(module) + '"'
        if reference not in base:
            base.append(reference)
compiler = '/usr/local/share/dotnet/sdk/8.0.424/Roslyn/bincore/csc.dll'
packages = repo / 'Packages'
groups = [
    ('URUI.Runtime', 'com.fujisheng.urui/Runtime', []),
    ('URUI.Editor', 'com.fujisheng.urui/Editor', ['URUI.Runtime']),
    ('URUI.Effect', 'com.fujisheng.urui.effect/Runtime', ['URUI.Runtime']),
    ('URUI.Effect.Editor', 'com.fujisheng.urui.effect/Editor', ['URUI.Runtime', 'URUI.Editor', 'URUI.Effect']),
    ('URUI.Authoring.Editor', 'com.fujisheng.urui.authoring/Editor', ['URUI.Runtime', 'URUI.Editor']),
    ('URUI.Authoring.Validation', 'com.fujisheng.urui.authoring/Tests~/Editor', ['URUI.Runtime', 'URUI.Editor', 'URUI.Effect', 'URUI.Effect.Editor', 'URUI.Authoring.Editor']),
]
for name, folder, references in groups:
    if args.player and name not in ('URUI.Runtime', 'URUI.Effect'):
        continue
    sources = list((packages / folder).rglob('*.cs'))
    if not sources:
        continue
    rows = base + ['-out:"' + str(output / (name + '.dll')) + '"']
    rows += ['-r:"' + str(output / (ref + '.dll')) + '"' for ref in references]
    rows += ['"' + str(source) + '"' for source in sources]
    response = output / (name + '.rsp')
    response.write_text('\n'.join(rows))
    result = subprocess.run(['dotnet', compiler, '@' + str(response)], cwd=project, capture_output=True, text=True)
    (output / (name + '.log')).write_text(result.stdout + result.stderr)
    print(f'{name}: {len(sources)} sources, exit {result.returncode}')
    if result.returncode:
        print((result.stdout + result.stderr)[-11000:])
        raise SystemExit(result.returncode)
