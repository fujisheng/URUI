"""Run Authoring checks in an isolated Unity project, never the user's game."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path)
parser.add_argument('--unity', type=Path)
parser.add_argument('--unity-version', default='6000.3.22f1')
parser.add_argument('--without-effect', action='store_true')
parser.add_argument('--method', default='URUI.Authoring.Tests.AuthoringValidation.Run')
parser.add_argument('--interactive', action='store_true')
parser.add_argument('--regression-platform', choices=['EditMode', 'PlayMode'])
parser.add_argument('--test-filter')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
project = (args.project or repo / 'Tests~' / ('AuthoringCoreOnly' if args.without_effect else 'Authoring')).resolve()
game = (repo.parent / 'Client').resolve()
if project in (repo, *repo.parents, repo / 'Packages') or project == game or game in project.parents or (repo / 'Packages') in project.parents:
    parser.error('Use an isolated validation project; the repository, package sources and Client are protected.')
project.mkdir(parents=True, exist_ok=True)
recovery = project / 'Library/URUI/Authoring/working-copy.json'
if recovery.exists():
    backup = project / 'Evidence' / ('working-copy-before-checks-' + uuid.uuid4().hex + '.json')
    backup.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(recovery, backup)
    print(json.dumps({'preserved_prior_working_copy': str(backup)}), flush=True)
packages = ['com.fujisheng.urui', 'com.fujisheng.urui.authoring']
if not args.without_effect:
    packages.append('com.fujisheng.urui.effect')
dependencies = {name: 'file:' + str(repo / 'Packages' / name) for name in packages}
dependencies.update({'com.unity.modules.imageconversion': '1.0.0', 'com.unity.modules.jsonserialize': '1.0.0', 'com.unity.modules.imgui': '1.0.0'})
manifest = {'dependencies': dependencies}
if args.regression_platform:
    dependencies['com.unity.test-framework'] = '1.6.0'
    manifest['testables'] = [name for name in packages if name != 'com.fujisheng.urui.authoring']
(project / 'Packages').mkdir(exist_ok=True)
(project / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2))
settings = project / 'ProjectSettings'
settings.mkdir(exist_ok=True)
(settings / 'ProjectVersion.txt').write_text(f'm_EditorVersion: {args.unity_version}\n')
fixtures = project / 'Assets/Editor/URUIAuthoringChecks'
fixtures.mkdir(parents=True, exist_ok=True)
for source in (repo / 'Packages/com.fujisheng.urui.authoring/Tests~/Editor').glob('*.cs'):
    if args.without_effect and source.name.endswith('EffectValidation.cs'):
        if (fixtures / source.name).exists():
            (fixtures / source.name).unlink()
        continue
    shutil.copy2(source, fixtures / source.name)
# The package independence Play test deliberately loads the imported gallery assembly and Resources.
# Samples~ is not an active Unity asset folder; prepare the same import in the isolated test project.
if args.regression_platform and not args.without_effect:
    gallery = repo / 'Packages/com.fujisheng.urui.effect/Samples~/EffectGallery'
    imported = project / 'Assets/URUIEffectGallery'
    resources = imported / 'Resources/URUI.Effect.Gallery'
    resources.mkdir(parents=True, exist_ok=True)
    for name in ('EffectGallery.cs', 'Gallery.asmdef'):
        for suffix in ('', '.meta'):
            shutil.copy2(gallery / (name + suffix), imported / (name + suffix))
    for source, target in [('Gallery.html', 'GalleryDocument.html'), ('Gallery.css', 'GalleryStyle.css'), ('Gallery.shader', 'GalleryProgram.shader'),
                           ('white.png', 'white.png'), ('input.png', 'input.png'), ('pattern.png', 'pattern.png'), ('gradient-lut.png', 'gradient-lut.png')]:
        for suffix in ('', '.meta'):
            shutil.copy2(gallery / (source + suffix), resources / (target + suffix))
unity = str(args.unity or os.environ.get('URUI_UNITY') or f'/Applications/Unity/Hub/Editor/{args.unity_version}/Unity.app/Contents/MacOS/Unity')
log = project / 'authoring-editor.log'
cmd = [unity] + ([] if args.interactive else ['-batchmode']) + ['-projectPath', str(project), '-logFile', str(log)]
if args.regression_platform:
    (project / 'Evidence').mkdir(exist_ok=True)
    cmd += ['-runTests', '-testPlatform', args.regression_platform, '-testResults', str(project / 'Evidence' / (args.regression_platform + '.xml'))]
    if args.test_filter:
        cmd += ['-testFilter', args.test_filter]
else:
    cmd += ['-executeMethod', args.method]
print(json.dumps({'project': str(project), 'log': str(log), 'command': cmd}), flush=True)
raise SystemExit(subprocess.run(cmd).returncode)
