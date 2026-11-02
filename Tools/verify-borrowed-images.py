"""Bounded Editor texture contracts in the existing development project; no Player."""
from pathlib import Path
import json
import re
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[1]
project = repo.parent
package = repo / 'Packages/com.fujisheng.urui'
fixtures = package / 'Tests~/Editor/Fixtures'
output = project / 'Artifacts/ImageMemoryAudit/Implementation'
output.mkdir(parents=True, exist_ok=True)
target = project / 'Assets/URUI/Verification'
(target / 'Editor').mkdir(parents=True, exist_ok=True)
for rel in ('BorrowedImageValidation.cs', 'Editor/TextureSamplingGpuValidation.cs'):
    shutil.copy2(fixtures / rel, target / rel)
shutil.copytree(package / 'Tests~/TextureBorrowing', project / 'Assets/URUITextureBorrowing', dirs_exist_ok=True)
unity = '/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity'
settings = project / 'ProjectSettings/ProjectSettings.asset'
original = settings.read_bytes()
match = re.search(rb'(?m)^  m_ActiveColorSpace: ([01])$', original)
assert match, 'Cannot resolve original color space'
original_color = 'Linear' if match[1] == b'1' else 'Gamma'


def run(method, log, *extra):
    command = [unity, '-batchmode', '-projectPath', str(project), '-executeMethod', method,
               '-logFile', str(output / log), *extra]
    status = subprocess.run(command, cwd=project).returncode
    print(json.dumps({'method': method, 'status': status, 'log': str(output / log)}), flush=True)
    return status


try:
    for linear in (False, True):
        flags = ['--urui-gpu-original=' + original_color] + (['--urui-gpu-linear'] if linear else [])
        name = 'linear' if linear else 'gamma'
        status = run('TextureSamplingGpuValidation.Run', 'gpu-' + name + '-editor.log', *flags)
        if status == 3:  # Restart once after the requested color-space switch.
            status = run('TextureSamplingGpuValidation.Run', 'gpu-' + name + '-editor.log', *flags)
        if status:
            raise SystemExit(status)
    for method, log in [('BorrowedImageValidation.Run', 'borrowed-editor.log'),
                        ('URUI.Tests.BorrowedRenderPathValidation.Run', 'render-paths-editor.log')]:
        status = run(method, log)
        if status:
            raise SystemExit(status)
    if '--batching' in sys.argv:
        status = run('URUI.Tests.BorrowedBatchValidation.Run', 'batching-editor.log')
        if status:
            raise SystemExit(status)
finally:
    # Only revert our known color-space field. Never overwrite concurrent changes.
    current = settings.read_bytes()
    restored = re.sub(rb'(?m)^  m_ActiveColorSpace: [01]$', match[0], current)
    if restored != current:
        settings.write_bytes(restored)
    if restored != original:
        print('Other ProjectSettings changes were preserved; inspect the project diff.', flush=True)
for file in ('gpu-gamma.json', 'gpu-linear.json', 'borrowed-editor.json', 'render-paths.json'):
    assert json.loads((output / file).read_text())['passed'], file
print('BORROWED_TEXTURE_EDITOR_PASS', flush=True)
